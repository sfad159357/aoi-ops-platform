using System.Text.Json;
using AOIOpsPlatform.Application.Hubs;
using AOIOpsPlatform.Application.Observability;
using AOIOpsPlatform.Domain.Entities;
using AOIOpsPlatform.Infrastructure.Data;
using AOIOpsPlatform.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AOIOpsPlatform.Api.Tests;

public sealed class AlarmRabbitWorkerTests
{
    [Fact]
    public async Task HandleAsync_sets_panel_status_to_fail_when_severity_is_medium()
    {
        // 為什麼驗證 medium：
        // - 這次需求是把 panel status 改成動態，不要一直停在 in_progress；
        // - Worker 規則把 medium/high/critical 視為需要升級為 fail，這裡鎖住行為避免回歸。
        var options = new DbContextOptionsBuilder<AoiOpsDbContext>()
            .UseInMemoryDatabase(databaseName: $"aoiops-alarm-tests-{Guid.NewGuid()}")
            .Options;

        await using var db = new AoiOpsDbContext(options);
        var lot = new Lot
        {
            Id = Guid.NewGuid(),
            LotNo = "WO-20260428-004",
            ProductCode = "PCB-A",
            Quantity = 25,
            Status = "in_progress",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var panel = new Panel
        {
            Id = Guid.NewGuid(),
            LotId = lot.Id,
            LotNo = lot.LotNo,
            PanelNo = "WO-20260428-004-11",
            Status = "in_progress",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Lots.Add(lot);
        db.Panels.Add(panel);
        await db.SaveChangesAsync();

        var worker = CreateWorker(db);
        var payload = BuildDefectPayload(
            lotNo: lot.LotNo,
            panelNo: panel.PanelNo,
            severity: "medium");

        var ok = await worker.HandleAsync(payload, CancellationToken.None);
        Assert.True(ok);

        var updated = await db.Panels.SingleAsync(p => p.Id == panel.Id);
        Assert.Equal("fail", updated.Status);
    }

    [Fact]
    public async Task HandleAsync_keeps_panel_status_when_severity_is_low()
    {
        // 為什麼驗證 low 不升級：
        // - 低嚴重度異常不應把板直接標記 fail，否則會造成過度告警。
        var options = new DbContextOptionsBuilder<AoiOpsDbContext>()
            .UseInMemoryDatabase(databaseName: $"aoiops-alarm-tests-{Guid.NewGuid()}")
            .Options;

        await using var db = new AoiOpsDbContext(options);
        var lot = new Lot
        {
            Id = Guid.NewGuid(),
            LotNo = "WO-20260428-005",
            ProductCode = "PCB-A",
            Quantity = 25,
            Status = "in_progress",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var panel = new Panel
        {
            Id = Guid.NewGuid(),
            LotId = lot.Id,
            LotNo = lot.LotNo,
            PanelNo = "WO-20260428-005-7",
            Status = "in_progress",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Lots.Add(lot);
        db.Panels.Add(panel);
        await db.SaveChangesAsync();

        var worker = CreateWorker(db);
        var payload = BuildDefectPayload(
            lotNo: lot.LotNo,
            panelNo: panel.PanelNo,
            severity: "low");

        var ok = await worker.HandleAsync(payload, CancellationToken.None);
        Assert.True(ok);

        var updated = await db.Panels.SingleAsync(p => p.Id == panel.Id);
        Assert.Equal("in_progress", updated.Status);
    }

    private static AlarmRabbitWorker CreateWorker(AoiOpsDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:RabbitMq:QueueAlert"] = "alert",
            })
            .Build();

        return new AlarmRabbitWorker(
            db,
            new NoopAlarmHubBroker(),
            new NoopRealtimeMetrics(),
            config,
            NullLogger<AlarmRabbitWorker>.Instance);
    }

    private static string BuildDefectPayload(string lotNo, string panelNo, string severity)
    {
        // 為什麼用 snake_case payload：
        // - AlarmRabbitWorker 的 JsonNamingPolicy 設為 SnakeCaseLower，
        //   測試要貼近實際 RabbitMQ 訊息格式才能驗證真實路徑。
        var obj = new
        {
            event_id = Guid.NewGuid().ToString(),
            tool_code = "AOI-A01",
            line_code = "SMT-A",
            station_code = "AOI",
            lot_no = lotNo,
            panel_no = panelNo,
            operator_code = "OP-001",
            operator_name = "王小明",
            defect_code = "DEF-1001",
            defect_type = "空焊",
            severity,
            timestamp = DateTimeOffset.UtcNow,
        };
        return JsonSerializer.Serialize(obj);
    }

    private sealed class NoopAlarmHubBroker : IAlarmHubBroker
    {
        public Task PushAlarmAsync(object payload, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopRealtimeMetrics : IRealtimeMetrics
    {
        public void RecordSpcPoint(bool hadViolation, double latencyMs) { }
        public void RecordAlarm(double latencyMs) { }
        public void RecordWorkorder(double latencyMs) { }
    }
}
