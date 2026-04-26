using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AOIOpsPlatform.Infrastructure.Data;

/// <summary>
/// DB 初始化器：在開發環境把「連線 + schema」一次打通。
/// 為什麼這樣寫：
/// - MVP 階段我們還沒引入 migrations 管線時，先用 EnsureCreated 讓 docker-compose / 本機開發能立刻驗證 end-to-end。
/// - Production 不應使用 EnsureCreated（難以演進、也不適合團隊協作），因此這裡嚴格限制只在 Development 執行。
/// 解決什麼問題：
/// - 避免「API 能起來但 DB 沒表」造成 Swagger/前端查詢全失敗，卻難以定位是連線還是 schema 問題。
/// </summary>
public static class AoiOpsDbInitializer
{
    /// <summary>
    /// 在應用程式啟動時初始化資料庫。
    /// 為什麼用 IServiceProvider：
    /// - 讓我們在最小改動下取得 scoped DbContext，符合 EF Core 生命週期。
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (!env.IsDevelopment())
        {
            // Production 走 migrations / DBA 流程；這裡刻意不做任何事，避免誤把開發捷徑帶上線。
            return;
        }

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AOIOpsPlatform.Infrastructure.Data.AoiOpsDbInitializer");

        var db = scope.ServiceProvider.GetRequiredService<AoiOpsDbContext>();

        // EnsureCreated：依目前 model 直接建立資料庫與資料表（僅適合早期 MVP）。
        // 後續若導入 migrations，應移除此段並改為自動/手動執行 dotnet ef database update。
        var created = await db.Database.EnsureCreatedAsync(cancellationToken);
        logger.LogInformation("Database EnsureCreated completed. Created={Created}", created);

        // Seed（開發用假資料）
        // 為什麼要在開發環境 seed：
        // - 新手最容易卡「表有了，但畫面不知道要顯示什麼」。
        // - 先塞最小 demo 資料，才能立刻做 W02 的 list API 與前端顯示。
        //
        // 這裡的原則：只在資料表是空的時才 seed，避免你手動新增資料後被覆蓋。
        var hasAnyTool = await db.Tools.AnyAsync(cancellationToken);
        if (!hasAnyTool)
        {
            logger.LogInformation("Seeding development data (tools/lots/defects) ...");

            var now = DateTimeOffset.UtcNow;

            var toolA = new Domain.Entities.Tool
            {
                Id = Guid.NewGuid(),
                ToolCode = "AOI-A",
                ToolName = "AOI Machine A",
                ToolType = "AOI",
                Status = "online",
                Location = "FAB-1",
                CreatedAt = now
            };

            var toolB = new Domain.Entities.Tool
            {
                Id = Guid.NewGuid(),
                ToolCode = "AOI-B",
                ToolName = "AOI Machine B",
                ToolType = "AOI",
                Status = "online",
                Location = "FAB-1",
                CreatedAt = now
            };

            var recipe = new Domain.Entities.Recipe
            {
                Id = Guid.NewGuid(),
                RecipeCode = "RCP-001",
                RecipeName = "Baseline Recipe",
                Version = "v1",
                Description = "MVP demo recipe",
                CreatedAt = now
            };

            db.Tools.AddRange(toolA, toolB);
            db.Recipes.Add(recipe);

            // 5 個 lot：讓 list API 有東西可看
            var lots = Enumerable.Range(1, 5).Select(i => new Domain.Entities.Lot
            {
                Id = Guid.NewGuid(),
                LotNo = $"LOT-{i:000}",
                ProductCode = "PROD-A",
                Quantity = 25,
                StartTime = now.AddHours(-i * 2),
                EndTime = null,
                Status = "in_progress",
                CreatedAt = now.AddHours(-i * 2)
            }).ToList();

            db.Lots.AddRange(lots);

            // 每個 lot 建 2 片 wafer（兼任 PCB 板）
            //
            // 為什麼順手填 panel_no：
            // - W08 的物料追溯 API 用 panel_no 當入口，
            //   seed 階段就帶上 demo 用的可讀識別碼，前端打開頁面就能查到資料。
            var wafers = lots.SelectMany(l => new[]
            {
                new Domain.Entities.Wafer
                {
                    Id = Guid.NewGuid(),
                    LotId = l.Id,
                    WaferNo = "1",
                    PanelNo = $"PCB-{now:yyyyMMdd}-{l.LotNo}-1",
                    Status = "in_progress",
                    CreatedAt = now
                },
                new Domain.Entities.Wafer
                {
                    Id = Guid.NewGuid(),
                    LotId = l.Id,
                    WaferNo = "2",
                    PanelNo = $"PCB-{now:yyyyMMdd}-{l.LotNo}-2",
                    Status = "in_progress",
                    CreatedAt = now
                }
            }).ToList();

            db.Wafers.AddRange(wafers);

            // 建一筆 process_run + 一筆 defect：讓你後面做 defect list / trend 有基礎資料
            var firstLot = lots.First();
            var firstWafer = wafers.First(w => w.LotId == firstLot.Id);

            var run = new Domain.Entities.ProcessRun
            {
                Id = Guid.NewGuid(),
                ToolId = toolA.Id,
                RecipeId = recipe.Id,
                LotId = firstLot.Id,
                WaferId = firstWafer.Id,
                RunStartAt = now.AddMinutes(-30),
                RunEndAt = now.AddMinutes(-10),
                Temperature = 180.5m,
                Pressure = 120.3m,
                YieldRate = 0.972m,
                ResultStatus = "pass",
                CreatedAt = now.AddMinutes(-30)
            };

            var defect = new Domain.Entities.Defect
            {
                Id = Guid.NewGuid(),
                ToolId = toolA.Id,
                LotId = firstLot.Id,
                WaferId = firstWafer.Id,
                ProcessRunId = run.Id,
                DefectCode = "DEF-0042",
                DefectType = "scratch",
                Severity = "high",
                XCoord = 12.34m,
                YCoord = 56.78m,
                DetectedAt = now.AddMinutes(-20),
                IsFalseAlarm = false,
                KafkaEventId = "seed-event-0001"
            };

            var alarm = new Domain.Entities.Alarm
            {
                Id = Guid.NewGuid(),
                ToolId = toolA.Id,
                ProcessRunId = run.Id,
                AlarmCode = "ALM-9001",
                AlarmLevel = "warning",
                Message = "Seed alarm for demo",
                TriggeredAt = now.AddMinutes(-19),
                ClearedAt = null,
                Status = "active",
                Source = "manual"
            };

            var workorder = new Domain.Entities.Workorder
            {
                Id = Guid.NewGuid(),
                LotId = firstLot.Id,
                WorkorderNo = "WO-0001",
                Priority = "normal",
                Status = "pending",
                SourceQueue = "workorder",
                CreatedAt = now
            };

            db.ProcessRuns.Add(run);
            db.Defects.Add(defect);
            db.Alarms.Add(alarm);
            db.Workorders.Add(workorder);

            // W08 seed：物料批號 + 用料 + 6 站時間軸（針對第一張板）
            //
            // 為什麼一定要 seed：
            // - Traceability 頁面打開若沒有任何 panel 有 6 站歷程，UI 永遠是空白；
            //   先給一張完整 demo 板，讓 acceptance 與面試 demo 立刻看得到效果。
            var solder = new Domain.Entities.MaterialLot
            {
                Id = Guid.NewGuid(),
                MaterialLotNo = $"SOLDER-{now:yyyyMMdd}-001",
                MaterialType = "solder_paste",
                MaterialName = "錫膏 SAC305",
                Supplier = "ABC Solder Co.",
                ReceivedAt = now.AddDays(-3),
                CreatedAt = now,
            };
            var fr4 = new Domain.Entities.MaterialLot
            {
                Id = Guid.NewGuid(),
                MaterialLotNo = $"FR4-{now:yyyyMMdd}-001",
                MaterialType = "fr4",
                MaterialName = "FR4 1.6mm",
                Supplier = "XYZ Laminate Inc.",
                ReceivedAt = now.AddDays(-7),
                CreatedAt = now,
            };
            var capacitor = new Domain.Entities.MaterialLot
            {
                Id = Guid.NewGuid(),
                MaterialLotNo = $"CAP-{now:yyyyMMdd}-001",
                MaterialType = "capacitor",
                MaterialName = "0402 10uF",
                Supplier = "Capacitor Ltd.",
                ReceivedAt = now.AddDays(-2),
                CreatedAt = now,
            };
            db.MaterialLots.AddRange(solder, fr4, capacitor);

            // 為什麼第一張板（firstWafer）就 seed 完整 6 站歷程：
            // - 讓 demo / 自動測試只需挑這張板就能驗收完整 timeline。
            var stations = new[] { "SPI", "SMT", "REFLOW", "AOI", "ICT", "FQC" };
            for (var i = 0; i < stations.Length; i++)
            {
                var enteredAt = now.AddMinutes(-30 + i * 4);
                db.PanelStationLogs.Add(new Domain.Entities.PanelStationLog
                {
                    Id = Guid.NewGuid(),
                    PanelId = firstWafer.Id,
                    StationCode = stations[i],
                    EnteredAt = enteredAt,
                    ExitedAt = enteredAt.AddMinutes(3),
                    Result = i == 3 ? "warn" : "pass",
                    Operator = i < 3 ? "OP-001" : "AOI-A",
                    Note = i == 3 ? "AOI defect=1（DEF-0042）" : null,
                });
            }

            db.PanelMaterialUsages.AddRange(
                new Domain.Entities.PanelMaterialUsage
                {
                    PanelId = firstWafer.Id,
                    MaterialLotId = solder.Id,
                    Quantity = 0.85m,
                    UsedAt = now.AddMinutes(-30),
                },
                new Domain.Entities.PanelMaterialUsage
                {
                    PanelId = firstWafer.Id,
                    MaterialLotId = fr4.Id,
                    Quantity = 1m,
                    UsedAt = now.AddMinutes(-30),
                },
                new Domain.Entities.PanelMaterialUsage
                {
                    PanelId = firstWafer.Id,
                    MaterialLotId = capacitor.Id,
                    Quantity = 24m,
                    UsedAt = now.AddMinutes(-26),
                });

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seed completed.");
        }
    }
}
