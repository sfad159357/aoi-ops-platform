// AlarmRabbitWorker：消費 RabbitMQ alert queue，落 DB（alarms + defects）並推 SignalR。
//
// 為什麼放在 Infrastructure 層：
// - 這個 handler 直接依賴 EF Core DbContext（Infrastructure 才有），
//   寫在 Application 會把 DB 細節漏出去，不符合 Clean Architecture。
// - 仍實作 Application 層定義的 IRabbitMessageHandler 介面，
//   Application 的 hosted service 用介面，不知道實作細節。
//
// 為什麼這支同時寫 alarms + defects 兩張表：
// - 真實 MES：「Kafka defect → RabbitMQ alert → 異常記錄頁 + 物料追溯查詢」是同一個事件的兩個視圖；
//   不該再 seed 假 defect，而要從 stream 落地，alarms 與 defects 共享 payload；
// - 兩張表寫在同一個 transaction，避免 alarm 已寫但 defect 漏寫的時間窗。
//
// 解決什麼問題：
// - 異常記錄即時推播：UI 不再需要輪詢 /api/alarms 才會更新；
// - 物料追溯查詢頁可看到「真實」缺陷分佈，不再是 seed 出來的死資料。

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AOIOpsPlatform.Application.Hubs;
using AOIOpsPlatform.Application.Messaging;
using AOIOpsPlatform.Application.Observability;
using AOIOpsPlatform.Domain.Entities;
using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AOIOpsPlatform.Infrastructure.Workers;

/// <summary>
/// 把 RabbitMQ alert 訊息落地到 alarms + defects 表，並透過 SignalR 推播給前端。
/// </summary>
public sealed class AlarmRabbitWorker : IRabbitMessageHandler
{
    private readonly AoiOpsDbContext _db;
    private readonly IAlarmHubBroker _hubBroker;
    private readonly IRealtimeMetrics _metrics;
    private readonly ILogger<AlarmRabbitWorker> _logger;

    public AlarmRabbitWorker(
        AoiOpsDbContext db,
        IAlarmHubBroker hubBroker,
        IRealtimeMetrics metrics,
        IConfiguration configuration,
        ILogger<AlarmRabbitWorker> logger)
    {
        _db = db;
        _hubBroker = hubBroker;
        _metrics = metrics;
        _logger = logger;
        Queue = configuration["Messaging:RabbitMq:QueueAlert"] ?? "alert";
    }

    public string Queue { get; }

    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        DefectEventPayload? evt;
        try
        {
            evt = JsonSerializer.Deserialize<DefectEventPayload>(body, JsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "alert payload 解析失敗：{Body}", Truncate(body));
            return false;
        }

        if (evt is null) return false;

        // 為什麼 tool 不存在就建一個：
        // - demo 環境裡 ingestion 可能還沒 seed 完整 tools；
        //   讓 alert 第一筆進來時自動建立 tool，避免阻塞落地（與 Python db-sink 行為一致）。
        var toolCode = evt.ToolCode ?? "AOI-A";
        var toolId = await EnsureToolAsync(toolCode, evt.LineCode, cancellationToken);

        // 為什麼順手解 lot/panel id：
        // - alarms / defects 都需要 FK；先 ensure 一次後兩張表共用。
        var (lotId, lotNo) = await EnsureLotAsync(evt.LotNo, cancellationToken);
        var (panelId, panelNo) = await EnsurePanelAsync(lotId, lotNo, evt.PanelNo, evt.WaferNo, cancellationToken);

        // 為什麼 alarm 落地時把全套關聯欄位都寫進去：
        // - 列表 API 不再需要 JOIN；
        // - 即使日後母表被歸檔/刪除，alarms 仍保留歷史快照值。
        var alarm = new Alarm
        {
            Id = Guid.NewGuid(),
            ToolId = toolId,
            ToolCode = toolCode,
            LineCode = evt.LineCode,
            StationCode = evt.StationCode,
            LotNo = lotNo,
            PanelNo = panelNo,
            OperatorCode = evt.OperatorCode,
            OperatorName = evt.OperatorName,
            ProcessRunId = null,
            AlarmCode = evt.DefectCode ?? "DEF-0000",
            AlarmLevel = evt.Severity ?? "low",
            Message = BuildMessage(evt),
            TriggeredAt = evt.Timestamp ?? DateTimeOffset.UtcNow,
            Status = "active",
            Source = "rabbitmq",
        };
        _db.Alarms.Add(alarm);

        // 為什麼同步寫 defects：
        // - 一筆 Kafka defect 同時是「告警事件」與「板級缺陷紀錄」兩個視圖；
        // - 物料追溯頁面/SPC dashboard 要看真實缺陷分佈，必須有 defects 落地；
        // - panelId / lotId 任一個沒解到時就略過 defects（保留 alarm 即可），避免 FK 違反。
        if (lotId.HasValue && panelId.HasValue)
        {
            _db.Defects.Add(new Defect
            {
                Id = Guid.NewGuid(),
                ToolId = toolId,
                LotId = lotId.Value,
                PanelId = panelId.Value,
                ProcessRunId = null,
                ToolCode = toolCode,
                LotNo = lotNo!,
                PanelNo = panelNo!,
                LineCode = evt.LineCode,
                StationCode = evt.StationCode,
                OperatorCode = evt.OperatorCode,
                OperatorName = evt.OperatorName,
                DefectCode = evt.DefectCode ?? "DEF-0000",
                DefectType = evt.DefectType,
                Severity = evt.Severity,
                XCoord = evt.XCoord.HasValue ? (decimal?)evt.XCoord.Value : null,
                YCoord = evt.YCoord.HasValue ? (decimal?)evt.YCoord.Value : null,
                DetectedAt = alarm.TriggeredAt,
                IsFalseAlarm = false,
                KafkaEventId = evt.EventId,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        // 為什麼從這裡才開始量延遲：
        // - DB 落地在 push 前，量「db + push」會把 EF SaveChanges 的時間混進去；
        //   metrics 主要關心 SignalR 推播，故只計 push 段落。
        var sw = Stopwatch.StartNew();
        await _hubBroker.PushAlarmAsync(new
        {
            id = alarm.Id,
            alarmCode = alarm.AlarmCode,
            alarmLevel = alarm.AlarmLevel,
            message = alarm.Message,
            triggeredAt = alarm.TriggeredAt,
            status = alarm.Status,
            source = alarm.Source,
            toolCode = alarm.ToolCode,
            lineCode = alarm.LineCode,
            stationCode = alarm.StationCode,
            lotNo = alarm.LotNo,
            panelNo = alarm.PanelNo,
            operatorCode = alarm.OperatorCode,
            operatorName = alarm.OperatorName,
            defectType = evt.DefectType,
        }, cancellationToken);
        sw.Stop();
        _metrics.RecordAlarm(sw.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "Alarm+Defect 已寫入並推送：code={Code} severity={Severity} tool={Tool} panel={Panel} op={Op} pushMs={PushMs}",
            alarm.AlarmCode, alarm.AlarmLevel, alarm.ToolCode, alarm.PanelNo, alarm.OperatorCode, sw.Elapsed.TotalMilliseconds);
        return true;
    }

    private static string BuildMessage(DefectEventPayload evt)
    {
        var defectType = string.IsNullOrEmpty(evt.DefectType) ? "未分類" : evt.DefectType;
        var station = string.IsNullOrEmpty(evt.StationCode) ? "?" : evt.StationCode;
        return $"[{station}] {defectType} severity={evt.Severity ?? "low"}";
    }

    private async Task<Guid> EnsureToolAsync(string toolCode, string? lineCode, CancellationToken cancellationToken)
    {
        var tool = await _db.Tools.FirstOrDefaultAsync(t => t.ToolCode == toolCode, cancellationToken);
        if (tool != null) return tool.Id;

        // 為什麼自動建立的 tool 補上 LineCode：
        // - alert 已帶 line_code（ingestion mapping），順手寫進去 tools 主檔，未來查詢能直接走 line filter；
        // - 仍不指派 LineId（我們手上只有字串），LineId 留待維運回填。
        tool = new Tool
        {
            Id = Guid.NewGuid(),
            ToolCode = toolCode,
            ToolName = $"{toolCode} (auto)",
            ToolType = "AOI",
            Status = "online",
            Location = "FAB-1",
            LineId = null,
            LineCode = lineCode,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tools.Add(tool);
        await _db.SaveChangesAsync(cancellationToken);
        return tool.Id;
    }

    private async Task<(Guid? id, string? lotNo)> EnsureLotAsync(string? lotNo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(lotNo)) return (null, null);
        var lot = await _db.Lots.FirstOrDefaultAsync(l => l.LotNo == lotNo, cancellationToken);
        if (lot != null) return (lot.Id, lot.LotNo);

        lot = new Lot
        {
            Id = Guid.NewGuid(),
            LotNo = lotNo!,
            ProductCode = "PCB-AUTO",
            Quantity = 25,
            StartTime = DateTimeOffset.UtcNow,
            Status = "in_progress",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Lots.Add(lot);
        await _db.SaveChangesAsync(cancellationToken);
        return (lot.Id, lot.LotNo);
    }

    private async Task<(Guid? id, string? panelNo)> EnsurePanelAsync(
        Guid? lotId, string? lotNo, string? payloadPanelNo, int? waferNo, CancellationToken cancellationToken)
    {
        // 為什麼 panelNo fallback：
        // - ingestion 已組好 panel_no，但偶爾舊版 producer 沒帶；
        // - 用 lot_no + wafer_no 組合與 panels 表寫入邏輯對齊。
        var panelNo = !string.IsNullOrWhiteSpace(payloadPanelNo)
            ? payloadPanelNo
            : (string.IsNullOrWhiteSpace(lotNo) ? null : $"{lotNo}-{waferNo ?? 0}");
        if (lotId is null || string.IsNullOrWhiteSpace(panelNo)) return (null, panelNo);

        var panel = await _db.Panels.FirstOrDefaultAsync(p => p.PanelNo == panelNo, cancellationToken);
        if (panel != null) return (panel.Id, panel.PanelNo);

        panel = new Panel
        {
            Id = Guid.NewGuid(),
            LotId = lotId.Value,
            LotNo = lotNo!,
            PanelNo = panelNo!,
            Status = "in_progress",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Panels.Add(panel);
        await _db.SaveChangesAsync(cancellationToken);
        return (panel.Id, panel.PanelNo);
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "…";

    // 為什麼把 PropertyNamingPolicy 設為 SnakeCaseLower：
    // - Python publisher 送的 payload 是 snake_case（tool_code / lot_no…），
    //   原本只設 PropertyNameCaseInsensitive 並不會把 snake_case 對應到 PascalCase；
    //   過去能運作純粹是因為共用 toolCode/lotCode 兩個短欄位「碰巧」匹配。
    // - 設 SnakeCaseLower 之後 PoCO 全部用 PascalCase，新增欄位不必每個都寫 [JsonPropertyName]。
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Python publisher 在 aoi.defect.event 上送出的 JSON 結構（snake_case→PascalCase 由 namingPolicy 處理）。
    /// </summary>
    /// <remarks>
    /// 為什麼用 nullable 全部欄位：
    /// - publisher 任何欄位缺失都不該讓我們崩潰；缺值改成預設值再寫 DB。
    /// </remarks>
    private sealed class DefectEventPayload
    {
        public string? EventId { get; set; }
        public string? ToolCode { get; set; }
        public string? LineCode { get; set; }
        public string? StationCode { get; set; }
        public string? LotNo { get; set; }
        public int? WaferNo { get; set; }
        public string? PanelNo { get; set; }
        public string? OperatorCode { get; set; }
        public string? OperatorName { get; set; }
        public string? DefectCode { get; set; }
        public string? DefectType { get; set; }
        public double? XCoord { get; set; }
        public double? YCoord { get; set; }
        public string? Severity { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
    }
}
