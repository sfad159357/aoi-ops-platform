using AOIOpsPlatform.Application.Domain;
using AOIOpsPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AOIOpsPlatform.Infrastructure.Data;

/// <summary>
/// DB 初始化器：在開發環境跑 migrations + seed PCB 高階製程的最小可用資料集。
/// </summary>
/// <remarks>
/// 為什麼從 EnsureCreated 改成 MigrateAsync：
/// - EnsureCreated 不會 track schema 演進，下一個 schema 變動只能 drop volume，這是過去 panel_no
///   / material_lots 等欄位反覆要 patch 的根因。
/// - MigrateAsync 會跑 Migrations 產生的 SQL（含 FK / index / 主鍵），同時也是團隊協作的單一真相。
///
/// 為什麼把 seed 拆成多個 method：
/// - master / pcb / event / traceability 各自有自己的 Anchor（例如 master 是 lines/stations/parameters），
///   分開後每段都能獨立 idempotent，避免一支 method 上千行難以維護。
///
/// 為什麼仍限制只在 Development 執行：
/// - production 要交給 DBA 流程，避免測試 seed 汙染正式資料。
/// </remarks>
public static class AoiOpsDbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (!env.IsDevelopment())
        {
            // production 走 migrations / DBA 流程；這裡刻意不做任何事，避免誤把開發捷徑帶上線。
            return;
        }

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AOIOpsPlatform.Infrastructure.Data.AoiOpsDbInitializer");
        var db = scope.ServiceProvider.GetRequiredService<AoiOpsDbContext>();
        var profile = scope.ServiceProvider.GetRequiredService<DomainProfileService>().Current;

        // 為什麼先 MigrateAsync：
        // - migrations 表會自動建立並追蹤已套用的 migration；新版 schema 直接生效，舊版資料庫也會自動補欄位。
        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrations applied.");

        // 為什麼用 strategy.ExecuteAsync：未來導入 retrying execution strategy 也能自動處理；
        //   即使現在沒設定，也不會因加上 transaction 而出錯。
        await SeedMasterAsync(db, profile, logger, cancellationToken);

        var hasAnyTool = await db.Tools.AnyAsync(cancellationToken);
        if (!hasAnyTool)
        {
            await SeedPcbDemoAsync(db, profile, logger, cancellationToken);
        }
        else
        {
            logger.LogInformation("Tools 已有資料，跳過 PCB demo seed。");
        }
    }

    /// <summary>
    /// 從 domain profile 載入 lines / stations / parameters 主檔。
    /// </summary>
    /// <remarks>
    /// 為什麼以 profile 為單一真相：
    /// - 前端、SPC Worker、報表都依 profile.code 驗證輸入，DB 的主檔代碼也對齊 profile，避免雙份維護。
    ///
    /// 為什麼用 upsert 邏輯：
    /// - 既存 code 只更新中文 label / spec，不會破壞已經產生的 transaction 紀錄（FK 仍指向同一個 id）。
    /// </remarks>
    private static async Task SeedMasterAsync(
        AoiOpsDbContext db,
        DomainProfile profile,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // lines
        var existingLineCodes = await db.Lines.Select(x => x.LineCode).ToListAsync(cancellationToken);
        foreach (var line in profile.Lines)
        {
            if (existingLineCodes.Contains(line.Code, StringComparer.OrdinalIgnoreCase)) continue;
            db.Lines.Add(new Line
            {
                Id = Guid.NewGuid(),
                LineCode = line.Code,
                LineName = string.IsNullOrWhiteSpace(line.LabelZh) ? line.Code : line.LabelZh,
                CreatedAt = now,
            });
        }

        // stations
        var existingStationCodes = await db.Stations.Select(x => x.StationCode).ToListAsync(cancellationToken);
        foreach (var s in profile.Stations)
        {
            if (existingStationCodes.Contains(s.Code, StringComparer.OrdinalIgnoreCase)) continue;
            db.Stations.Add(new Station
            {
                Id = Guid.NewGuid(),
                StationCode = s.Code,
                StationName = string.IsNullOrWhiteSpace(s.LabelZh) ? s.Code : s.LabelZh,
                Seq = s.Seq,
                CreatedAt = now,
            });
        }

        // parameters
        var existingParameterCodes = await db.Parameters.Select(x => x.ParameterCode).ToListAsync(cancellationToken);
        foreach (var p in profile.Parameters)
        {
            if (existingParameterCodes.Contains(p.Code, StringComparer.OrdinalIgnoreCase)) continue;
            db.Parameters.Add(new Parameter
            {
                Id = Guid.NewGuid(),
                ParameterCode = p.Code,
                ParameterName = string.IsNullOrWhiteSpace(p.LabelZh) ? p.Code : p.LabelZh,
                Unit = p.Unit,
                Usl = (decimal)p.Usl,
                Lsl = (decimal)p.Lsl,
                Target = (decimal)p.Target,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Master seed completed. lines={Lines} stations={Stations} parameters={Parameters}",
            profile.Lines.Count, profile.Stations.Count, profile.Parameters.Count);
    }

    /// <summary>
    /// 種一份 PCB 高階製程的豐富 demo 資料：員工 / 多機台 / 多批次 / 多板 / 6 站歷程 / 物料 / SPC 量測 / 歷史 alarms+workorders+defects。
    /// </summary>
    /// <remarks>
    /// 為什麼一次種完整鏈路 + 大量歷史：
    /// - 面試 demo 只看「即時推播」會很單調；要讓 4 個前端頁面（SPC / 異常 / 工單 / 物料追溯）打開就有歷史可看，
    ///   並且各欄位（產線、機台、站別、批次、板號、人員、時間）都有明顯差異與關聯，才看得出 MES 落地價值。
    /// - 即時資料則由 ingestion 持續灌入，與 seed 共存。
    ///
    /// 為什麼 panel_no 用「lot_no-序號」格式：
    /// - 與 ingestion 端 _resolve_panel_no 完全一致，這樣 producer 即使再寫一筆 (lot, wafer) 進來，
    ///   _get_or_create_panel_id 會命中既有 panel，不會重複塞。
    /// </remarks>
    private static async Task SeedPcbDemoAsync(
        AoiOpsDbContext db,
        DomainProfile profile,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Seeding PCB demo data (rich) ...");
        var now = DateTimeOffset.UtcNow;
        var rng = new Random(20260428);

        // ─── 0. operators 員工池 ──────────────────────────────────────
        // 為什麼把人員寫到 DB：
        // - 雖然子表（alarms/workorders/defects）只冗餘字串，但有獨立 operators 主檔
        //   能讓報表「依負責人聚合」、未來補 RBAC 時也有 anchor。
        var operatorsToSeed = new (string Code, string Name, string Role, string Shift)[]
        {
            ("OP-001", "王小明", "operator", "A"),
            ("OP-002", "李大華", "operator", "A"),
            ("OP-003", "張美玲", "operator", "B"),
            ("OP-004", "林志偉", "operator", "B"),
            ("OP-005", "陳怡君", "operator", "C"),
            ("OP-006", "黃文豪", "operator", "C"),
            ("LEADER-A", "周建華", "leader", "A"),
            ("LEADER-B", "吳秀英", "leader", "B"),
            ("LEADER-C", "韓立群", "leader", "C"),
            ("ENG-001", "趙俊傑", "engineer", "A"),
            ("QC-001", "蔡佳玲", "qc", "A"),
        };
        var operators = operatorsToSeed.Select(o => new Operator
        {
            Id = Guid.NewGuid(),
            OperatorCode = o.Code,
            OperatorName = o.Name,
            Role = o.Role,
            Shift = o.Shift,
            CreatedAt = now,
        }).ToList();
        db.Operators.AddRange(operators);

        var opByCode = operators.ToDictionary(o => o.OperatorCode, o => o);
        string PickOperatorCode(string stationCode)
        {
            // 為什麼用 hash + jitter：
            // - 想讓「SPI 站常常是 OP-001、AOI 站常常是 LEADER-B」這種模式出現，又保留隨機性，
            //   讓報表上看到「同站別偶爾換人」更貼近真實。
            var idx = (Math.Abs(stationCode.GetHashCode()) + rng.Next(operators.Count)) % operators.Count;
            return operators[idx].OperatorCode;
        }

        // ─── 1. lines（從 master 撈出，支援 PCB profile 的 SMT-A / SMT-B / AOI-A） ───
        var lines = await db.Lines.ToListAsync(cancellationToken);
        var smtLineA = lines.FirstOrDefault(l => l.LineCode.Equals("SMT-A", StringComparison.OrdinalIgnoreCase));
        var smtLineB = lines.FirstOrDefault(l => l.LineCode.Equals("SMT-B", StringComparison.OrdinalIgnoreCase));
        var defaultLine = lines.FirstOrDefault();
        smtLineA ??= defaultLine;
        smtLineB ??= defaultLine;

        // ─── 2. tools 機台：與 ingestion mapping 對齊（SMT-A 線跑完整 6 站，SMT-B 線跑簡化 2 站） ───
        var toolDefs = new (string Code, string Name, string Type, string Station, Line? Line)[]
        {
            ("SPI-A01", "SPI Station A01", "SPI", "SPI", smtLineA),
            ("SMT-A01", "SMT Mounter A01", "SMT", "SMT", smtLineA),
            ("REFLOW-A01", "Reflow Oven A01", "REFLOW", "REFLOW", smtLineA),
            ("AOI-A01", "AOI Inspector A01", "AOI", "AOI", smtLineA),
            ("ICT-A01", "ICT Tester A01", "ICT", "ICT", smtLineA),
            ("FQC-A01", "FQC Station A01", "FQC", "FQC", smtLineA),
            ("SMT-B01", "SMT Mounter B01", "SMT", "SMT", smtLineB),
            ("AOI-B01", "AOI Inspector B01", "AOI", "AOI", smtLineB),
        };
        var tools = toolDefs.Select(t => new Tool
        {
            Id = Guid.NewGuid(),
            ToolCode = t.Code,
            ToolName = t.Name,
            ToolType = t.Type,
            Status = "online",
            Location = "FAB-1",
            LineId = t.Line?.Id,
            LineCode = t.Line?.LineCode,
            CreatedAt = now,
        }).ToList();
        db.Tools.AddRange(tools);
        var toolByCode = tools.ToDictionary(t => t.ToolCode, t => t);
        var toolStationByCode = toolDefs.ToDictionary(t => t.Code, t => t.Station);

        // ─── 3. recipes ────────────────────────────────────────────────
        var recipe = new Recipe
        {
            Id = Guid.NewGuid(),
            RecipeCode = "RCP-PCB-001",
            RecipeName = "PCB Baseline Recipe",
            Version = "v1",
            Description = "Demo recipe for PCB SMT/AOI line",
            CreatedAt = now,
        };
        db.Recipes.Add(recipe);

        // ─── 4. lots 工單批次：8 張 lot，狀態混合 ─────────────────────
        // 為什麼 8 張：3 in_progress + 3 completed + 2 queued，
        //   涵蓋工單管理頁的 P1/P2/P3 工單與 lifecycle 不同階段。
        var lotStatuses = new[] { "in_progress", "in_progress", "in_progress", "completed", "completed", "completed", "queued", "queued" };
        var lots = Enumerable.Range(1, 8).Select(i => new Lot
        {
            Id = Guid.NewGuid(),
            LotNo = $"WO-{now:yyyyMMdd}-{i:000}",
            ProductCode = "PCB-A",
            Quantity = 25,
            StartTime = now.AddHours(-i * 1.5),
            EndTime = lotStatuses[i - 1] == "completed" ? now.AddHours(-i * 1.5 + 4) : (DateTimeOffset?)null,
            Status = lotStatuses[i - 1],
            CreatedAt = now.AddHours(-i * 1.5),
        }).ToList();
        db.Lots.AddRange(lots);

        // ─── 5. panels：每 lot 10 板，總 80 板 ─────────────────────────
        // 為什麼一個 lot 10 板：剛好對應 ingestion 端 random.randint(1, 25) 中常見的 1-10 區間，
        //   ingestion 即使再灌新事件，也容易命中既有 panel（不會無限長新板）。
        const int panelsPerLot = 10;
        var panels = new List<Panel>();
        foreach (var lot in lots)
        {
            for (var idx = 1; idx <= panelsPerLot; idx++)
            {
                panels.Add(new Panel
                {
                    Id = Guid.NewGuid(),
                    LotId = lot.Id,
                    LotNo = lot.LotNo,
                    PanelNo = $"{lot.LotNo}-{idx}",
                    Status = lot.Status == "completed" ? "pass" : "in_progress",
                    CreatedAt = lot.CreatedAt.AddSeconds(idx),
                });
            }
        }
        db.Panels.AddRange(panels);

        // ─── 6. material_lots：5 種常見 PCB 物料 ───────────────────────
        var materialDefs = new (string Code, string Type, string Name, string Supplier)[]
        {
            ($"SOLDER-{now:yyyyMMdd}-001", "solder_paste", "錫膏 SAC305", "ABC Solder Co."),
            ($"FR4-{now:yyyyMMdd}-001", "fr4", "FR4 1.6mm", "XYZ Laminate Inc."),
            ($"CAP-{now:yyyyMMdd}-001", "capacitor", "0402 10uF", "Capacitor Ltd."),
            ($"RES-{now:yyyyMMdd}-001", "resistor", "0402 10kΩ", "Resistor Co."),
            ($"IC-{now:yyyyMMdd}-001", "ic", "STM32F407 LQFP100", "ST Microelectronics"),
        };
        var materials = materialDefs.Select((m, i) => new MaterialLot
        {
            Id = Guid.NewGuid(),
            MaterialLotNo = m.Code,
            MaterialType = m.Type,
            MaterialName = m.Name,
            Supplier = m.Supplier,
            ReceivedAt = now.AddDays(-(i + 2)),
            CreatedAt = now,
        }).ToList();
        db.MaterialLots.AddRange(materials);

        // ─── 7. panel_material_usage：每張板用 3 種物料 ────────────────
        // 為什麼每板 3 種：對應 PCB 真實情境（錫膏 + FR4 板材 + 元件），
        //   讓物料追溯查詢頁打開就能看到「板 ↔ 物料 ↔ 同物料其他板」三層關聯。
        var usages = new List<PanelMaterialUsage>();
        foreach (var panel in panels)
        {
            // 為什麼錫膏與 FR4 是固定，元件可變：
            // - 錫膏與板材是基礎物料，每張板都會用；元件根據產品有所差異，這裡輪流用 3 種來增加可看性。
            var componentMaterial = materials[2 + (Math.Abs(panel.LotNo.GetHashCode()) % 3)];
            usages.Add(new PanelMaterialUsage
            {
                PanelId = panel.Id,
                MaterialLotId = materials[0].Id,
                PanelNo = panel.PanelNo,
                MaterialLotNo = materials[0].MaterialLotNo,
                Quantity = 0.85m,
                UsedAt = panel.CreatedAt.AddMinutes(2),
            });
            usages.Add(new PanelMaterialUsage
            {
                PanelId = panel.Id,
                MaterialLotId = materials[1].Id,
                PanelNo = panel.PanelNo,
                MaterialLotNo = materials[1].MaterialLotNo,
                Quantity = 1m,
                UsedAt = panel.CreatedAt.AddMinutes(1),
            });
            usages.Add(new PanelMaterialUsage
            {
                PanelId = panel.Id,
                MaterialLotId = componentMaterial.Id,
                PanelNo = panel.PanelNo,
                MaterialLotNo = componentMaterial.MaterialLotNo,
                Quantity = 24m,
                UsedAt = panel.CreatedAt.AddMinutes(5),
            });
        }
        db.PanelMaterialUsages.AddRange(usages);

        // ─── 8. station codes（profile 優先；fallback 寫死 6 站） ──────
        var stationCodes = profile.Stations.Count > 0
            ? profile.Stations.OrderBy(s => s.Seq).Select(s => s.Code).ToArray()
            : new[] { "SPI", "SMT", "REFLOW", "AOI", "ICT", "FQC" };

        // 站別 ↔ 機台對照（決定每個 station 哪台機跑）
        var stationToTool = new Dictionary<string, Tool>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tools)
        {
            if (toolStationByCode.TryGetValue(t.ToolCode, out var st) && !stationToTool.ContainsKey(st))
            {
                stationToTool[st] = t;
            }
        }

        // ─── 9. panel_station_log：每張板跑完整 6 站（process_runs 同步記錄） ───
        // 為什麼每張板都種 6 站：
        // - 物料追溯查詢頁的時間軸要看到「進站 → 出站 → 下一站」六段；
        // - 即使 ingestion 之後寫進更多 row，已 seed 的 row 確保打開頁面立刻有東西。
        var processRuns = new List<ProcessRun>();
        var stationLogs = new List<PanelStationLog>();
        foreach (var panel in panels)
        {
            var lot = lots.First(l => l.Id == panel.LotId);
            for (var i = 0; i < stationCodes.Length; i++)
            {
                var stationCode = stationCodes[i];
                stationToTool.TryGetValue(stationCode, out var stationTool);
                stationTool ??= tools[i % tools.Count];
                var operatorCode = PickOperatorCode(stationCode);
                var op = opByCode[operatorCode];

                var enteredAt = panel.CreatedAt.AddMinutes(i * 4);
                var exitedAt = enteredAt.AddMinutes(3);
                stationLogs.Add(new PanelStationLog
                {
                    Id = Guid.NewGuid(),
                    PanelId = panel.Id,
                    PanelNo = panel.PanelNo,
                    StationCode = stationCode,
                    EnteredAt = enteredAt,
                    ExitedAt = exitedAt,
                    Result = i == 3 && panel.PanelNo.EndsWith("-1") ? "warn" : "pass",
                    Operator = op.OperatorCode,
                    OperatorName = op.OperatorName,
                    ToolCode = stationTool.ToolCode,
                    Note = i == 3 && panel.PanelNo.EndsWith("-1") ? "AOI defect=1" : null,
                });

                // 每張板每兩站對應一筆 process_run（demo 用，省略 stations 過站全跑）
                if (i % 2 == 0)
                {
                    processRuns.Add(new ProcessRun
                    {
                        Id = Guid.NewGuid(),
                        ToolId = stationTool.Id,
                        RecipeId = recipe.Id,
                        LotId = lot.Id,
                        PanelId = panel.Id,
                        ToolCode = stationTool.ToolCode,
                        LotNo = lot.LotNo,
                        PanelNo = panel.PanelNo,
                        RunStartAt = enteredAt,
                        RunEndAt = exitedAt,
                        Temperature = 248m + (decimal)((rng.NextDouble() - 0.5) * 4),
                        Pressure = 120m + (decimal)((rng.NextDouble() - 0.5) * 4),
                        YieldRate = 0.97m + (decimal)((rng.NextDouble() - 0.5) * 0.04),
                        ResultStatus = "pass",
                        CreatedAt = enteredAt,
                    });
                }
            }
        }
        db.PanelStationLogs.AddRange(stationLogs);
        db.ProcessRuns.AddRange(processRuns);

        // ─── 10. 歷史 defects + alarms + workorders：選 25 張板，每張隨機 1 個缺陷 ───
        // 為什麼選 25 張板：與 ingestion 邏輯一致（每 12 筆 inspection 才一筆 defect），
        //   給足 4 頁面 demo 用的歷史量；後續即時 stream 還會繼續累加。
        var defectTypes = new[] { "短路", "斷路", "錫橋", "空焊", "偏移", "缺件", "異物", "極性反向" };
        var severityWeights = new[] { ("low", 0.5), ("medium", 0.35), ("high", 0.15) };
        var alarms = new List<Alarm>();
        var defects = new List<Defect>();
        var workorders = new List<Workorder>();
        var defectPanels = panels.OrderBy(_ => rng.Next()).Take(25).ToList();
        var historyAnchor = now.AddHours(-3);
        for (var i = 0; i < defectPanels.Count; i++)
        {
            var panel = defectPanels[i];
            var lot = lots.First(l => l.Id == panel.LotId);
            // 為什麼 station 選 AOI：
            // - 真實場景下 AOI 才會驗出板級缺陷；FQC 多半在缺陷修復後做最後檢驗。
            //   把缺陷集中在 AOI 站可以對應到「定位 X-Y」資訊。
            var stationCode = "AOI";
            stationToTool.TryGetValue(stationCode, out var stationTool);
            stationTool ??= toolByCode["AOI-A01"];
            var operatorCode = PickOperatorCode(stationCode);
            var op = opByCode[operatorCode];

            var sev = WeightedPick(severityWeights, rng);
            var defectType = defectTypes[rng.Next(defectTypes.Length)];
            var detectedAt = historyAnchor.AddMinutes(i * 6);

            var defect = new Defect
            {
                Id = Guid.NewGuid(),
                ToolId = stationTool.Id,
                LotId = lot.Id,
                PanelId = panel.Id,
                ToolCode = stationTool.ToolCode,
                LotNo = lot.LotNo,
                PanelNo = panel.PanelNo,
                LineCode = stationTool.LineCode,
                StationCode = stationCode,
                OperatorCode = op.OperatorCode,
                OperatorName = op.OperatorName,
                DefectCode = $"DEF-{1000 + i:0000}",
                DefectType = defectType,
                Severity = sev,
                XCoord = (decimal)(rng.NextDouble() * 250),
                YCoord = (decimal)(rng.NextDouble() * 200),
                DetectedAt = detectedAt,
                IsFalseAlarm = false,
                KafkaEventId = $"seed-defect-{i:000}",
            };
            defects.Add(defect);

            alarms.Add(new Alarm
            {
                Id = Guid.NewGuid(),
                ToolId = stationTool.Id,
                ToolCode = stationTool.ToolCode,
                LineCode = stationTool.LineCode,
                StationCode = stationCode,
                LotNo = lot.LotNo,
                PanelNo = panel.PanelNo,
                OperatorCode = op.OperatorCode,
                OperatorName = op.OperatorName,
                AlarmCode = defect.DefectCode,
                AlarmLevel = sev,
                Message = $"[{stationCode}] {defectType} severity={sev}",
                TriggeredAt = detectedAt,
                Status = sev == "high" ? "active" : "ack",
                Source = "rabbitmq",
            });

            // 為什麼 high/medium 才開工單：對應 RabbitMQ publisher 的路由規則，避免低嚴重度淹沒工單頁面。
            if (sev == "high" || sev == "medium")
            {
                workorders.Add(new Workorder
                {
                    Id = Guid.NewGuid(),
                    LotId = lot.Id,
                    ToolId = stationTool.Id,
                    PanelId = panel.Id,
                    LotNo = lot.LotNo,
                    PanelNo = panel.PanelNo,
                    ToolCode = stationTool.ToolCode,
                    LineCode = stationTool.LineCode,
                    StationCode = stationCode,
                    OperatorCode = op.OperatorCode,
                    OperatorName = op.OperatorName,
                    Severity = sev,
                    DefectCode = defect.DefectCode,
                    WorkorderNo = $"WO-{detectedAt:yyyyMMddHHmmss}-{i:000}",
                    Priority = sev == "high" ? "P1" : "P2",
                    Status = "open",
                    SourceQueue = "workorder",
                    CreatedAt = detectedAt.AddSeconds(2),
                });
            }
        }
        db.Defects.AddRange(defects);
        db.Alarms.AddRange(alarms);
        db.Workorders.AddRange(workorders);

        // ─── 11. SPC 量測：所有機台 × 3 個常用參數 × 8 個觀測點 ───
        // 為什麼每組 8 點：滿足 SPC「規則 2：連續 9 點同側」需要的點數，未來灌入 1 點就能觸發；
        //   demo 一打開頁面就能看到歷史曲線。
        var parameters = await db.Parameters.AsNoTracking().ToListAsync(cancellationToken);
        var spcPoints = new List<SpcMeasurement>();
        foreach (var t in tools)
        {
            var stationCode = toolStationByCode[t.ToolCode];
            foreach (var p in parameters.Take(3))
            {
                for (var k = 0; k < 8; k++)
                {
                    var measuredAt = now.AddMinutes(-30 + k * 3);
                    var basePanel = panels[(Math.Abs(t.ToolCode.GetHashCode()) + k) % panels.Count];
                    var operatorCode = PickOperatorCode(stationCode);
                    var op = opByCode[operatorCode];
                    spcPoints.Add(new SpcMeasurement
                    {
                        Id = Guid.NewGuid(),
                        PanelId = basePanel.Id,
                        ToolId = t.Id,
                        ParameterId = p.Id,
                        PanelNo = basePanel.PanelNo,
                        LotNo = basePanel.LotNo,
                        ToolCode = t.ToolCode,
                        LineCode = t.LineCode ?? "UNKNOWN",
                        StationCode = stationCode,
                        ParameterCode = p.ParameterCode,
                        OperatorCode = op.OperatorCode,
                        OperatorName = op.OperatorName,
                        Value = ComputeDemoValue(p, k),
                        MeasuredAt = measuredAt,
                        IsViolation = false,
                        ViolationCodes = null,
                        KafkaEventId = $"seed-spc-{t.ToolCode}-{p.ParameterCode}-{k}",
                        CreatedAt = measuredAt,
                    });
                }
            }
        }
        db.SpcMeasurements.AddRange(spcPoints);

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "PCB demo seed completed (rich). operators={Operators} tools={Tools} lots={Lots} panels={Panels} " +
            "stations={Stations} alarms={Alarms} workorders={WO} defects={Defects} spc={Spc}",
            operators.Count, tools.Count, lots.Count, panels.Count,
            stationCodes.Length, alarms.Count, workorders.Count, defects.Count, spcPoints.Count);
    }

    /// <summary>依權重挑選一個 string 值；用於 severity 隨機分佈。</summary>
    private static string WeightedPick(IReadOnlyList<(string Value, double Weight)> items, Random rng)
    {
        var roll = rng.NextDouble() * items.Sum(x => x.Weight);
        var acc = 0.0;
        foreach (var (v, w) in items)
        {
            acc += w;
            if (roll <= acc) return v;
        }
        return items[^1].Value;
    }

    /// <summary>
    /// 計算 demo 量測值：以 target 為基準微幅波動。
    /// </summary>
    /// <remarks>
    /// 為什麼不用 random：
    /// - seed 必須可重現（測試 / demo 多次執行結果相同）。
    /// - 用 k 當 step 微調，能展示控制圖的「正常波動」樣態。
    /// </remarks>
    private static decimal ComputeDemoValue(Parameter p, int step)
    {
        var jitter = (decimal)(((step % 3) - 1) * 0.5);
        return p.Target + jitter;
    }
}
