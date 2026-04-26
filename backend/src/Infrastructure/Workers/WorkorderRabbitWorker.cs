// WorkorderRabbitWorker：消費 RabbitMQ workorder queue，落 PostgreSQL workorders 表並推 SignalR。
//
// 為什麼放在 Infrastructure 層：
// - 與 AlarmRabbitWorker 同樣需要 EF Core DbContext，屬於基礎設施實作。
//
// 為什麼跟 Alarm 分兩個 handler：
// - workorder 與 alarm 寫入的 table 不同、嚴重度與優先級對應規則也不同；
//   分開後 logic 各管各的，邊界清楚。
// - 未來若想依不同 queue 名稱動態擴充，只要再加 handler。
//
// 解決什麼問題：
// - 工單建立即時推播給前端；不需要重新整理頁面就能看到「新工單」動畫。

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
/// 把 RabbitMQ workorder 訊息落地到 workorders 表並透過 SignalR 推播給前端。
/// </summary>
public sealed class WorkorderRabbitWorker : IRabbitMessageHandler
{
    private readonly AoiOpsDbContext _db;
    private readonly IWorkorderHubBroker _hubBroker;
    private readonly IRealtimeMetrics _metrics;
    private readonly ILogger<WorkorderRabbitWorker> _logger;

    public WorkorderRabbitWorker(
        AoiOpsDbContext db,
        IWorkorderHubBroker hubBroker,
        IRealtimeMetrics metrics,
        IConfiguration configuration,
        ILogger<WorkorderRabbitWorker> logger)
    {
        _db = db;
        _hubBroker = hubBroker;
        _metrics = metrics;
        _logger = logger;
        Queue = configuration["Messaging:RabbitMq:QueueWorkorder"] ?? "workorder";
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
            _logger.LogWarning(ex, "workorder payload 解析失敗：{Body}", Truncate(body));
            return false;
        }
        if (evt is null) return false;

        Guid? lotId = null;
        if (!string.IsNullOrEmpty(evt.LotNo))
        {
            var lot = await _db.Lots.FirstOrDefaultAsync(l => l.LotNo == evt.LotNo, cancellationToken);
            lotId = lot?.Id;
        }

        // 為什麼這樣產 workorder_no：
        // - 對應 Python db-sink 既有規則：WO-yyyymmddhhmmss-{8字 random}；
        //   讓兩邊產出可讀識別碼一致，運維 / 比對 log 時不會混淆。
        var severity = (evt.Severity ?? "low").ToLowerInvariant();
        var priority = severity switch
        {
            "critical" or "high" => "P1",
            "medium" => "P2",
            _ => "P3",
        };

        var wo = new Workorder
        {
            Id = Guid.NewGuid(),
            LotId = lotId,
            WorkorderNo = $"WO-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}",
            Priority = priority,
            Status = "open",
            SourceQueue = "workorder",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Workorders.Add(wo);
        await _db.SaveChangesAsync(cancellationToken);

        // 為什麼從這裡才開始量延遲：理由同 AlarmRabbitWorker，純粹計 SignalR push 段落。
        var sw = Stopwatch.StartNew();
        await _hubBroker.PushWorkorderAsync(new
        {
            id = wo.Id,
            workorderNo = wo.WorkorderNo,
            priority = wo.Priority,
            status = wo.Status,
            createdAt = wo.CreatedAt,
            lotNo = evt.LotNo,
            severity = evt.Severity,
        }, cancellationToken);
        sw.Stop();
        _metrics.RecordWorkorder(sw.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "Workorder 已建立並推送：no={No} priority={Priority} lot={Lot} pushMs={PushMs}",
            wo.WorkorderNo, wo.Priority, evt.LotNo, sw.Elapsed.TotalMilliseconds);
        return true;
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "…";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

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
