using AOIOpsPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Infrastructure.Data;

/// <summary>
/// EF Core DbContext：集中管理 AOI Ops Platform 的資料表映射與關聯。
/// </summary>
/// <remarks>
/// 為什麼 schema 在此次大改：
/// - 原本只有欄位映射、沒有任何 FK / navigation，DB 層完全不擋孤兒資料；
///   ERD 工具也畫不出關聯線。
/// - 重建時把 wafers → panels 改名（PCB 語意），並為所有關聯補 HasOne/HasMany/HasForeignKey，
///   讓 DB 端真的有外鍵約束、EF 可以走 navigation/Include。
/// - 子表額外冗餘了可讀字串（lot_no、tool_code、panel_no…），
///   讓 controllers 直接 Select 即可拿到 JSON 期待的欄位，不需要再寫 join + 投影。
///
/// 為什麼仍保留 SqlServer / PostgreSQL 兩條路：
/// - 開發機 / CI 大多預設 SqlServer，但既有 docker-compose 與少數測試仍可能跑 Postgres；
///   provider 分流只在「default value SQL」與 panel_no filter 兩處有差異，集中在 OnModelCreating。
/// </remarks>
public sealed class AoiOpsDbContext : DbContext
{
    public AoiOpsDbContext(DbContextOptions<AoiOpsDbContext> options) : base(options)
    {
    }

    public DbSet<Line> Lines => Set<Line>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Parameter> Parameters => Set<Parameter>();
    public DbSet<Operator> Operators => Set<Operator>();

    public DbSet<Tool> Tools => Set<Tool>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<Panel> Panels => Set<Panel>();
    public DbSet<ProcessRun> ProcessRuns => Set<ProcessRun>();
    public DbSet<Alarm> Alarms => Set<Alarm>();
    public DbSet<Defect> Defects => Set<Defect>();
    public DbSet<DefectImage> DefectImages => Set<DefectImage>();
    public DbSet<DefectReview> DefectReviews => Set<DefectReview>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<CopilotQuery> CopilotQueries => Set<CopilotQuery>();
    public DbSet<Workorder> Workorders => Set<Workorder>();
    public DbSet<MaterialLot> MaterialLots => Set<MaterialLot>();
    public DbSet<PanelMaterialUsage> PanelMaterialUsages => Set<PanelMaterialUsage>();
    public DbSet<PanelStationLog> PanelStationLogs => Set<PanelStationLog>();
    public DbSet<SpcMeasurement> SpcMeasurements => Set<SpcMeasurement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 為什麼要做 provider 分流：
        // - PostgreSQL 用 gen_random_uuid()，SQL Server 用 NEWID()；
        // - panel_no 的 unique filter 在 SQL Server 用 [panel_no] IS NOT NULL，PG 則是 "panel_no" IS NOT NULL。
        // - 集中在這裡決定，避免散落在各 entity config 內。
        var provider = Database.ProviderName?.ToLowerInvariant() ?? string.Empty;
        var isSqlServer = provider.Contains("sqlserver");
        var defaultGuidSql = isSqlServer ? "NEWID()" : "gen_random_uuid()";

        ConfigureMasterTables(modelBuilder, defaultGuidSql);
        ConfigurePcbCoreTables(modelBuilder, defaultGuidSql);
        ConfigureEventTables(modelBuilder, defaultGuidSql);
        ConfigureTraceabilityTables(modelBuilder, defaultGuidSql);
        ConfigureKnowledgeTables(modelBuilder, defaultGuidSql);
    }

    /// <summary>
    /// 設定主檔（lines / stations / parameters / tools / recipes / lots）。
    /// </summary>
    /// <remarks>
    /// 為什麼把 master 與 transaction 分開設定：
    /// - master 的 unique key 與 seed 行為相對單純；
    /// - transaction 表才需要大量 FK / OnDelete 規則。將兩者拆開可以提高可讀性。
    /// </remarks>
    private static void ConfigureMasterTables(ModelBuilder modelBuilder, string defaultGuidSql)
    {
        modelBuilder.Entity<Line>(b =>
        {
            b.ToTable("lines");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.LineCode).HasColumnName("line_code").HasMaxLength(50).IsRequired();
            b.Property(x => x.LineName).HasColumnName("line_name").HasMaxLength(200).IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.LineCode).IsUnique();
        });

        modelBuilder.Entity<Station>(b =>
        {
            b.ToTable("stations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.StationCode).HasColumnName("station_code").HasMaxLength(50).IsRequired();
            b.Property(x => x.StationName).HasColumnName("station_name").HasMaxLength(200).IsRequired();
            b.Property(x => x.Seq).HasColumnName("seq");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.StationCode).IsUnique();
        });

        modelBuilder.Entity<Parameter>(b =>
        {
            b.ToTable("parameters");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.ParameterCode).HasColumnName("parameter_code").HasMaxLength(100).IsRequired();
            b.Property(x => x.ParameterName).HasColumnName("parameter_name").HasMaxLength(200).IsRequired();
            b.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(50);
            // 為什麼用 decimal(18,6)：覆蓋 USL/LSL/target 可能是百分比（0.97）、溫度（248）或微米（235）。
            b.Property(x => x.Usl).HasColumnName("usl").HasColumnType("decimal(18,6)");
            b.Property(x => x.Lsl).HasColumnName("lsl").HasColumnType("decimal(18,6)");
            b.Property(x => x.Target).HasColumnName("target").HasColumnType("decimal(18,6)");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.ParameterCode).IsUnique();
        });

        // 為什麼把 Operator 放在 master 區塊：
        // - 與 lines/stations/parameters 一樣是「相對穩定的編碼表」；
        // - 子表（alarms/workorders/defects/spc_measurements）只冗餘 operator_code/operator_name 字串，
        //   不寫 operator_id FK，避免每次寫入都要查 operators 表。
        modelBuilder.Entity<Operator>(b =>
        {
            b.ToTable("operators");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.OperatorCode).HasColumnName("operator_code").HasMaxLength(50).IsRequired();
            b.Property(x => x.OperatorName).HasColumnName("operator_name").HasMaxLength(200).IsRequired();
            b.Property(x => x.Role).HasColumnName("role").HasMaxLength(50).IsRequired();
            b.Property(x => x.Shift).HasColumnName("shift").HasMaxLength(20);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.OperatorCode).IsUnique();
        });

        modelBuilder.Entity<Tool>(b =>
        {
            b.ToTable("tools");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.ToolCode).HasColumnName("tool_code").HasMaxLength(100).IsRequired();
            b.Property(x => x.ToolName).HasColumnName("tool_name").HasMaxLength(200).IsRequired();
            b.Property(x => x.ToolType).HasColumnName("tool_type").HasMaxLength(100);
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(50);
            b.Property(x => x.Location).HasColumnName("location").HasMaxLength(200);
            b.Property(x => x.LineId).HasColumnName("line_id");
            b.Property(x => x.LineCode).HasColumnName("line_code").HasMaxLength(50);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.ToolCode).IsUnique();
            b.HasIndex(x => x.LineCode);

            // 為什麼 OnDelete 設成 SetNull：
            // - 產線可能整個重整或被砍掉，但歷史機台仍要保留紀錄；
            //   設 SetNull 可避免 cascade 把 tools 一起砍。
            b.HasOne(x => x.Line)
                .WithMany(l => l.Tools)
                .HasForeignKey(x => x.LineId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Recipe>(b =>
        {
            b.ToTable("recipes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.RecipeCode).HasColumnName("recipe_code").HasMaxLength(100).IsRequired();
            b.Property(x => x.RecipeName).HasColumnName("recipe_name").HasMaxLength(200).IsRequired();
            b.Property(x => x.Version).HasColumnName("version").HasMaxLength(50);
            b.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.RecipeCode).IsUnique();
        });

        modelBuilder.Entity<Lot>(b =>
        {
            b.ToTable("lots");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).IsRequired();
            b.Property(x => x.ProductCode).HasColumnName("product_code").HasMaxLength(100);
            b.Property(x => x.Quantity).HasColumnName("quantity");
            b.Property(x => x.StartTime).HasColumnName("start_time");
            b.Property(x => x.EndTime).HasColumnName("end_time");
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(50);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.LotNo).IsUnique();
        });
    }

    /// <summary>
    /// 設定 PCB 核心表（panels）。
    /// </summary>
    /// <remarks>
    /// 為什麼 panel_no 改成 NOT NULL + UNIQUE（不再用 nullable filter）：
    /// - 重建後所有 seed/ingestion 都會主動產生 panel_no，沒有「歷史空值」需要相容；
    /// - NOT NULL 可在 DB 層直接擋住未來忘填的情境，避免再回頭補 patch SQL。
    /// </remarks>
    private static void ConfigurePcbCoreTables(ModelBuilder modelBuilder, string defaultGuidSql)
    {
        modelBuilder.Entity<Panel>(b =>
        {
            b.ToTable("panels");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.LotId).HasColumnName("lot_id");
            b.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).IsRequired();
            b.Property(x => x.PanelNo).HasColumnName("panel_no").HasMaxLength(100).IsRequired();
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(50);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");

            b.HasIndex(x => x.PanelNo).IsUnique();
            b.HasIndex(x => x.LotNo);

            // 為什麼 panel→lot OnDelete 設 Cascade：
            // - 一個 lot 被刪通常代表整個批次廢棄；保留 panel 卻沒有 lot 沒有意義。
            // - PCB 場景批次量小（25 片左右），cascade 不會誤刪海量資料。
            b.HasOne(x => x.Lot)
                .WithMany(l => l.Panels)
                .HasForeignKey(x => x.LotId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>
    /// 設定事件 / 子表（process_runs / alarms / defects / defect_images / defect_reviews / workorders / spc_measurements）。
    /// </summary>
    /// <remarks>
    /// 為什麼集中在這個 method：
    /// - 大量 OnDelete 規則需要互相對齊，
    ///   尤其 Defect → DefectImage / DefectReview 是 cascade，但 Defect → Tool/Lot/Panel 不能 cascade；
    ///   一處看到全部規則才不會踩到 SQL Server 的 multiple cascade paths 限制。
    /// </remarks>
    private static void ConfigureEventTables(ModelBuilder modelBuilder, string defaultGuidSql)
    {
        modelBuilder.Entity<ProcessRun>(b =>
        {
            b.ToTable("process_runs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.ToolId).HasColumnName("tool_id");
            b.Property(x => x.RecipeId).HasColumnName("recipe_id");
            b.Property(x => x.LotId).HasColumnName("lot_id");
            b.Property(x => x.PanelId).HasColumnName("panel_id");
            b.Property(x => x.ToolCode).HasColumnName("tool_code").HasMaxLength(100).IsRequired();
            b.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).IsRequired();
            b.Property(x => x.PanelNo).HasColumnName("panel_no").HasMaxLength(100).IsRequired();
            b.Property(x => x.RunStartAt).HasColumnName("run_start_at");
            b.Property(x => x.RunEndAt).HasColumnName("run_end_at");
            b.Property(x => x.Temperature).HasColumnName("temperature").HasColumnType("decimal(18,4)");
            b.Property(x => x.Pressure).HasColumnName("pressure").HasColumnType("decimal(18,4)");
            b.Property(x => x.YieldRate).HasColumnName("yield_rate").HasColumnType("decimal(5,4)");
            b.Property(x => x.ResultStatus).HasColumnName("result_status").HasMaxLength(50);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.RunStartAt);
            b.HasIndex(x => x.PanelNo);
            b.HasIndex(x => x.LotNo);
            b.HasIndex(x => x.ToolCode);

            // 為什麼 process_run 的所有 FK 都設 Restrict：
            // - process_run 是「歷史紀錄」，刪母表時不該連帶把製程史一起刪；
            //   讓 DB 在誤操作時直接擋下，提示維運人員手動處理。
            b.HasOne(x => x.Tool)
                .WithMany(t => t.ProcessRuns)
                .HasForeignKey(x => x.ToolId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Recipe)
                .WithMany(r => r.ProcessRuns)
                .HasForeignKey(x => x.RecipeId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Lot)
                .WithMany(l => l.ProcessRuns)
                .HasForeignKey(x => x.LotId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Panel)
                .WithMany(p => p.ProcessRuns)
                .HasForeignKey(x => x.PanelId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Alarm>(b =>
        {
            b.ToTable("alarms");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.ToolId).HasColumnName("tool_id");
            b.Property(x => x.ProcessRunId).HasColumnName("process_run_id");
            b.Property(x => x.ToolCode).HasColumnName("tool_code").HasMaxLength(100).IsRequired();
            b.Property(x => x.LineCode).HasColumnName("line_code").HasMaxLength(50);
            b.Property(x => x.StationCode).HasColumnName("station_code").HasMaxLength(50);
            b.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100);
            b.Property(x => x.PanelNo).HasColumnName("panel_no").HasMaxLength(100);
            b.Property(x => x.OperatorCode).HasColumnName("operator_code").HasMaxLength(50);
            b.Property(x => x.OperatorName).HasColumnName("operator_name").HasMaxLength(200);
            b.Property(x => x.AlarmCode).HasColumnName("alarm_code").HasMaxLength(100).IsRequired();
            b.Property(x => x.AlarmLevel).HasColumnName("alarm_level").HasMaxLength(50);
            b.Property(x => x.Message).HasColumnName("message").HasMaxLength(2000);
            b.Property(x => x.TriggeredAt).HasColumnName("triggered_at");
            b.Property(x => x.ClearedAt).HasColumnName("cleared_at");
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(50);
            b.Property(x => x.Source).HasColumnName("source").HasMaxLength(50);
            b.HasIndex(x => x.TriggeredAt);
            b.HasIndex(x => x.ToolCode);
            b.HasIndex(x => x.LotNo);
            b.HasIndex(x => x.PanelNo);
            b.HasIndex(x => x.StationCode);
            b.HasIndex(x => x.OperatorCode);

            b.HasOne(x => x.Tool)
                .WithMany(t => t.Alarms)
                .HasForeignKey(x => x.ToolId)
                .OnDelete(DeleteBehavior.Restrict);
            // 為什麼 alarm→process_run 設 SetNull：
            // - 告警可能在 process_run 被歸檔/刪除後仍要保留歷史；SetNull 可保留 alarm 本體。
            b.HasOne(x => x.ProcessRun)
                .WithMany(pr => pr.Alarms)
                .HasForeignKey(x => x.ProcessRunId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Defect>(b =>
        {
            b.ToTable("defects");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.ToolId).HasColumnName("tool_id");
            b.Property(x => x.LotId).HasColumnName("lot_id");
            b.Property(x => x.PanelId).HasColumnName("panel_id");
            b.Property(x => x.ProcessRunId).HasColumnName("process_run_id");
            b.Property(x => x.ToolCode).HasColumnName("tool_code").HasMaxLength(100).IsRequired();
            b.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).IsRequired();
            b.Property(x => x.PanelNo).HasColumnName("panel_no").HasMaxLength(100).IsRequired();
            b.Property(x => x.LineCode).HasColumnName("line_code").HasMaxLength(50);
            b.Property(x => x.StationCode).HasColumnName("station_code").HasMaxLength(50);
            b.Property(x => x.OperatorCode).HasColumnName("operator_code").HasMaxLength(50);
            b.Property(x => x.OperatorName).HasColumnName("operator_name").HasMaxLength(200);
            b.Property(x => x.DefectCode).HasColumnName("defect_code").HasMaxLength(100).IsRequired();
            b.Property(x => x.DefectType).HasColumnName("defect_type").HasMaxLength(100);
            b.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(50);
            b.Property(x => x.XCoord).HasColumnName("x_coord").HasColumnType("decimal(18,4)");
            b.Property(x => x.YCoord).HasColumnName("y_coord").HasColumnType("decimal(18,4)");
            b.Property(x => x.DetectedAt).HasColumnName("detected_at");
            b.Property(x => x.IsFalseAlarm).HasColumnName("is_false_alarm");
            b.Property(x => x.KafkaEventId).HasColumnName("kafka_event_id").HasMaxLength(200);
            b.HasIndex(x => x.DetectedAt);
            b.HasIndex(x => x.ToolCode);
            b.HasIndex(x => x.LotNo);
            b.HasIndex(x => x.PanelNo);
            b.HasIndex(x => x.StationCode);
            b.HasIndex(x => x.OperatorCode);

            // 為什麼 defect 對母表全 Restrict：
            // - SQL Server 不允許 multiple cascade paths（例如 Lot→Defect、Lot→Panel→Defect 兩條 cascade 會撞）；
            //   故所有母表的刪除都顯式 Restrict，需要清資料時由應用程式或維運手動處理。
            b.HasOne(x => x.Tool)
                .WithMany(t => t.Defects)
                .HasForeignKey(x => x.ToolId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Lot)
                .WithMany(l => l.Defects)
                .HasForeignKey(x => x.LotId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Panel)
                .WithMany(p => p.Defects)
                .HasForeignKey(x => x.PanelId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.ProcessRun)
                .WithMany(pr => pr.Defects)
                .HasForeignKey(x => x.ProcessRunId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DefectImage>(b =>
        {
            b.ToTable("defect_images");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.DefectId).HasColumnName("defect_id");
            b.Property(x => x.ImagePath).HasColumnName("image_path").HasMaxLength(2000).IsRequired();
            b.Property(x => x.ThumbnailPath).HasColumnName("thumbnail_path").HasMaxLength(2000);
            b.Property(x => x.Width).HasColumnName("width");
            b.Property(x => x.Height).HasColumnName("height");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.DefectId);

            // 為什麼 defect_images / defect_reviews 用 Cascade：
            // - 影像/覆判紀錄屬於 defect 的「附屬資料」，沒有母 defect 就沒有意義；
            //   cascade 可避免維運手動清孤兒檔。
            b.HasOne(x => x.Defect)
                .WithMany(d => d.Images)
                .HasForeignKey(x => x.DefectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DefectReview>(b =>
        {
            b.ToTable("defect_reviews");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.DefectId).HasColumnName("defect_id");
            b.Property(x => x.Reviewer).HasColumnName("reviewer").HasMaxLength(200).IsRequired();
            b.Property(x => x.ReviewResult).HasColumnName("review_result").HasMaxLength(50).IsRequired();
            b.Property(x => x.ReviewComment).HasColumnName("review_comment").HasMaxLength(4000);
            b.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
            b.HasIndex(x => x.DefectId);

            b.HasOne(x => x.Defect)
                .WithMany(d => d.Reviews)
                .HasForeignKey(x => x.DefectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Workorder>(b =>
        {
            b.ToTable("workorders");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.LotId).HasColumnName("lot_id");
            b.Property(x => x.ToolId).HasColumnName("tool_id");
            b.Property(x => x.PanelId).HasColumnName("panel_id");
            b.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100);
            b.Property(x => x.PanelNo).HasColumnName("panel_no").HasMaxLength(100);
            b.Property(x => x.ToolCode).HasColumnName("tool_code").HasMaxLength(100);
            b.Property(x => x.LineCode).HasColumnName("line_code").HasMaxLength(50);
            b.Property(x => x.StationCode).HasColumnName("station_code").HasMaxLength(50);
            b.Property(x => x.OperatorCode).HasColumnName("operator_code").HasMaxLength(50);
            b.Property(x => x.OperatorName).HasColumnName("operator_name").HasMaxLength(200);
            b.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(50);
            b.Property(x => x.DefectCode).HasColumnName("defect_code").HasMaxLength(100);
            b.Property(x => x.WorkorderNo).HasColumnName("workorder_no").HasMaxLength(100).IsRequired();
            b.Property(x => x.Priority).HasColumnName("priority").HasMaxLength(50);
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(50);
            b.Property(x => x.SourceQueue).HasColumnName("source_queue").HasMaxLength(50).IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.WorkorderNo).IsUnique();
            b.HasIndex(x => x.LotNo);
            b.HasIndex(x => x.PanelNo);
            b.HasIndex(x => x.ToolCode);
            b.HasIndex(x => x.StationCode);
            b.HasIndex(x => x.OperatorCode);

            // 為什麼 workorder→lot 設 SetNull、panel/tool 設 NoAction：
            // - SQL Server 不允許 multiple cascade paths（lot→panel cascade + workorders→lot SetNull + workorders→panel SetNull 會被視為多路徑風險）；
            // - 工單是事件 + 責任歸檔，母表被刪除時不該抹掉責任歷史，
            //   panel/tool 改 NoAction：刪母表時 DB 會擋下，由應用層自行處理；冗餘字串欄位仍保留供顯示。
            b.HasOne(x => x.Lot)
                .WithMany(l => l.Workorders)
                .HasForeignKey(x => x.LotId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Tool)
                .WithMany()
                .HasForeignKey(x => x.ToolId)
                .OnDelete(DeleteBehavior.NoAction);
            b.HasOne(x => x.Panel)
                .WithMany()
                .HasForeignKey(x => x.PanelId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<SpcMeasurement>(b =>
        {
            b.ToTable("spc_measurements");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.PanelId).HasColumnName("panel_id");
            b.Property(x => x.ToolId).HasColumnName("tool_id");
            b.Property(x => x.ParameterId).HasColumnName("parameter_id");
            b.Property(x => x.PanelNo).HasColumnName("panel_no").HasMaxLength(100);
            b.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100);
            b.Property(x => x.ToolCode).HasColumnName("tool_code").HasMaxLength(100).IsRequired();
            b.Property(x => x.LineCode).HasColumnName("line_code").HasMaxLength(50).IsRequired();
            b.Property(x => x.StationCode).HasColumnName("station_code").HasMaxLength(50);
            b.Property(x => x.ParameterCode).HasColumnName("parameter_code").HasMaxLength(100).IsRequired();
            b.Property(x => x.OperatorCode).HasColumnName("operator_code").HasMaxLength(50);
            b.Property(x => x.OperatorName).HasColumnName("operator_name").HasMaxLength(200);
            b.Property(x => x.Value).HasColumnName("value").HasColumnType("decimal(18,6)");
            b.Property(x => x.MeasuredAt).HasColumnName("measured_at");
            b.Property(x => x.IsViolation).HasColumnName("is_violation");
            b.Property(x => x.ViolationCodes).HasColumnName("violation_codes").HasMaxLength(200);
            b.Property(x => x.KafkaEventId).HasColumnName("kafka_event_id").HasMaxLength(200);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            // 為什麼用 (parameter_code, measured_at) 與 (panel_no, measured_at) 雙索引：
            // - 報表最常見的兩種查詢：「某參數的歷史曲線」「某板的所有量測」。
            b.HasIndex(x => new { x.ParameterCode, x.MeasuredAt });
            b.HasIndex(x => new { x.PanelNo, x.MeasuredAt });
            b.HasIndex(x => new { x.ToolCode, x.MeasuredAt });
            b.HasIndex(x => new { x.LineCode, x.MeasuredAt });
            b.HasIndex(x => x.IsViolation);

            // 為什麼 panel 設 SetNull、tool/parameter 設 Restrict：
            // - 板可能在後段歸檔後被刪除（demo 重置），但歷史量測值仍可保留；
            // - tool/parameter 是核心 master，刪除等同 schema 變更，要由人手動處理。
            b.HasOne(x => x.Panel)
                .WithMany(p => p.SpcMeasurements)
                .HasForeignKey(x => x.PanelId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Tool)
                .WithMany(t => t.SpcMeasurements)
                .HasForeignKey(x => x.ToolId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Parameter)
                .WithMany(p => p.Measurements)
                .HasForeignKey(x => x.ParameterId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>
    /// 設定追溯 / 物料 / 站別歷程相關表。
    /// </summary>
    /// <remarks>
    /// 為什麼 panel_station_log.station_code 用「principal key reference」而不是 Guid FK：
    /// - station_code 是穩定的業務鍵，子表直接吃字串可在報表 SELECT 不需 join；
    /// - HasPrincipalKey(x => x.StationCode) 讓 EF 仍能掛 FK 約束在字串欄位上。
    /// </remarks>
    private static void ConfigureTraceabilityTables(ModelBuilder modelBuilder, string defaultGuidSql)
    {
        modelBuilder.Entity<MaterialLot>(b =>
        {
            b.ToTable("material_lots");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.MaterialLotNo).HasColumnName("material_lot_no").HasMaxLength(100).IsRequired();
            b.Property(x => x.MaterialType).HasColumnName("material_type").HasMaxLength(100).IsRequired();
            b.Property(x => x.MaterialName).HasColumnName("material_name").HasMaxLength(200);
            b.Property(x => x.Supplier).HasColumnName("supplier").HasMaxLength(200);
            b.Property(x => x.ReceivedAt).HasColumnName("received_at");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.MaterialLotNo).IsUnique();
            b.HasIndex(x => x.MaterialType);
        });

        modelBuilder.Entity<PanelMaterialUsage>(b =>
        {
            b.ToTable("panel_material_usage");
            b.HasKey(x => new { x.PanelId, x.MaterialLotId });
            b.Property(x => x.PanelId).HasColumnName("panel_id");
            b.Property(x => x.MaterialLotId).HasColumnName("material_lot_id");
            b.Property(x => x.PanelNo).HasColumnName("panel_no").HasMaxLength(100).IsRequired();
            b.Property(x => x.MaterialLotNo).HasColumnName("material_lot_no").HasMaxLength(100).IsRequired();
            b.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("decimal(18,4)");
            b.Property(x => x.UsedAt).HasColumnName("used_at");
            b.HasIndex(x => x.MaterialLotId);
            b.HasIndex(x => x.PanelNo);
            b.HasIndex(x => x.MaterialLotNo);

            b.HasOne(x => x.Panel)
                .WithMany(p => p.MaterialUsages)
                .HasForeignKey(x => x.PanelId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.MaterialLot)
                .WithMany(m => m.Usages)
                .HasForeignKey(x => x.MaterialLotId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PanelStationLog>(b =>
        {
            b.ToTable("panel_station_log");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.PanelId).HasColumnName("panel_id");
            b.Property(x => x.PanelNo).HasColumnName("panel_no").HasMaxLength(100).IsRequired();
            b.Property(x => x.StationCode).HasColumnName("station_code").HasMaxLength(50).IsRequired();
            b.Property(x => x.EnteredAt).HasColumnName("entered_at");
            b.Property(x => x.ExitedAt).HasColumnName("exited_at");
            b.Property(x => x.Result).HasColumnName("result").HasMaxLength(50);
            b.Property(x => x.Operator).HasColumnName("operator").HasMaxLength(100);
            b.Property(x => x.OperatorName).HasColumnName("operator_name").HasMaxLength(200);
            b.Property(x => x.ToolCode).HasColumnName("tool_code").HasMaxLength(100);
            b.Property(x => x.Note).HasColumnName("note").HasMaxLength(2000);
            b.HasIndex(x => new { x.PanelId, x.EnteredAt });
            b.HasIndex(x => x.StationCode);
            b.HasIndex(x => x.PanelNo);

            b.HasOne(x => x.Panel)
                .WithMany(p => p.StationLogs)
                .HasForeignKey(x => x.PanelId)
                .OnDelete(DeleteBehavior.Cascade);
            // 為什麼 station_code 設 PrincipalKey + Restrict：
            // - 我們希望 station 主檔不會被誤刪而留下孤兒紀錄；
            // - PrincipalKey 讓 FK 落在 string 欄位（station_code），符合報表「直接 GROUP BY station_code」的習慣。
            b.HasOne(x => x.Station)
                .WithMany(s => s.StationLogs)
                .HasForeignKey(x => x.StationCode)
                .HasPrincipalKey(s => s.StationCode)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>
    /// 設定 Knowledge Copilot 相關表（documents / document_chunks / copilot_queries）。
    /// </summary>
    /// <remarks>
    /// 為什麼 copilot_queries 的 alarm/defect FK 都設 SetNull：
    /// - Copilot 問答記錄要留作 audit；母事件被刪除時不該連帶把對話消失。
    /// </remarks>
    private static void ConfigureKnowledgeTables(ModelBuilder modelBuilder, string defaultGuidSql)
    {
        modelBuilder.Entity<Document>(b =>
        {
            b.ToTable("documents");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
            b.Property(x => x.DocType).HasColumnName("doc_type").HasMaxLength(100);
            b.Property(x => x.Version).HasColumnName("version").HasMaxLength(50);
            b.Property(x => x.SourcePath).HasColumnName("source_path").HasMaxLength(2000);
            b.Property(x => x.UploadedAt).HasColumnName("uploaded_at");
        });

        modelBuilder.Entity<DocumentChunk>(b =>
        {
            b.ToTable("document_chunks");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.DocumentId).HasColumnName("document_id");
            b.Property(x => x.ChunkText).HasColumnName("chunk_text").IsRequired();
            b.Property(x => x.ChunkIndex).HasColumnName("chunk_index");
            b.Property(x => x.EmbeddingId).HasColumnName("embedding_id").HasMaxLength(200);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new { x.DocumentId, x.ChunkIndex }).IsUnique();

            b.HasOne(x => x.Document)
                .WithMany(d => d.Chunks)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CopilotQuery>(b =>
        {
            b.ToTable("copilot_queries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql(defaultGuidSql);
            b.Property(x => x.QueryText).HasColumnName("query_text").IsRequired();
            b.Property(x => x.RelatedAlarmId).HasColumnName("related_alarm_id");
            b.Property(x => x.RelatedDefectId).HasColumnName("related_defect_id");
            b.Property(x => x.AnswerText).HasColumnName("answer_text");
            b.Property(x => x.SourceRefs).HasColumnName("source_refs");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.CreatedAt);

            b.HasOne(x => x.RelatedAlarm)
                .WithMany()
                .HasForeignKey(x => x.RelatedAlarmId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.RelatedDefect)
                .WithMany()
                .HasForeignKey(x => x.RelatedDefectId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
