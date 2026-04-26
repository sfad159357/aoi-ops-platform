// AlarmRabbitWorker：消費 RabbitMQ alert queue，落 PostgreSQL alarms 表並推 SignalR。
//
// 為什麼放在 Infrastructure 層：
// - 這個 handler 直接依賴 EF Core DbContext（Infrastructure 才有），
//   寫在 Application 會把 DB 細節漏出去，不符合 Clean Architecture。
// - 仍實作 Application 層定義的 IRabbitMessageHandler 介面，
//   Application 的 hosted service 用介面，不知道實作細節。
//
// 為什麼接管原本 Python rabbitmq-db-sink 的職責：
// - 集中由 .NET 處理 → DB 寫入 + SignalR push 在同一段程式內完成，
//   不會出現「DB 已寫入但前端沒收到推播」的時間窗。
// - 也避免 Python 與 .NET 兩邊同時消費同一個 queue 造成訊息搶食。
//
// 解決什麼問題：
// - 異常記錄即時推播：UI 不再需要輪詢 /api/alarms 才會更新。

using System.Diagnostics;
using System.Text.Json;
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
/// 把 RabbitMQ alert 訊息落地到 alarms 表並透過 SignalR 推播給前端。
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
        var toolId = await EnsureToolAsync(evt.ToolCode ?? "AOI-A", cancellationToken);

        var alarm = new Alarm
        {
            Id = Guid.NewGuid(),
            ToolId = toolId,
            ProcessRunId = null,
            AlarmCode = evt.DefectCode ?? "DEF-0000",
            AlarmLevel = evt.Severity ?? "low",
            Message = $"defect_event severity={evt.Severity}",
            TriggeredAt = evt.Timestamp ?? DateTimeOffset.UtcNow,
            Status = "active",
            Source = "rabbitmq",
        };
        _db.Alarms.Add(alarm);
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
            toolCode = evt.ToolCode,
            lotNo = evt.LotNo,
        }, cancellationToken);
        sw.Stop();
        _metrics.RecordAlarm(sw.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "Alarm 已寫入並推送：code={Code} severity={Severity} tool={Tool} pushMs={PushMs}",
            alarm.AlarmCode, alarm.AlarmLevel, evt.ToolCode, sw.Elapsed.TotalMilliseconds);
        return true;
    }

    private async Task<Guid> EnsureToolAsync(string toolCode, CancellationToken cancellationToken)
    {
        var tool = await _db.Tools.FirstOrDefaultAsync(t => t.ToolCode == toolCode, cancellationToken);
        if (tool != null) return tool.Id;

        tool = new Tool
        {
            Id = Guid.NewGuid(),
            ToolCode = toolCode,
            ToolName = $"{toolCode} (auto)",
            ToolType = "AOI",
            Status = "online",
            Location = "FAB-1",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tools.Add(tool);
        await _db.SaveChangesAsync(cancellationToken);
        return tool.Id;
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "…";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Python publisher 在 aoi.defect.event 上送出的 JSON 結構。
    /// </summary>
    /// <remarks>
    /// 為什麼用 nullable 全部欄位：
    /// - publisher 任何欄位缺失都不該讓我們崩潰；缺值改成預設值再寫 DB。
    /// </remarks>
    private sealed class DefectEventPayload
    {
        public string? EventId { get; set; }
        public string? ToolCode { get; set; }
        public string? LotNo { get; set; }
        public string? DefectCode { get; set; }
        public string? Severity { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
    }
}
