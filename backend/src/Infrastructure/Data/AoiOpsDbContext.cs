using AOIOpsPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Infrastructure.Data;

/// <summary>
/// EF Core DbContext：集中管理 AOI Ops Platform 的資料表映射。
/// 這樣寫的原因：讓 schema 定義在單一位置可被 migrations 與 repository 共用，
/// 避免分散在各處造成欄位/索引不一致。
/// </summary>
public sealed class AoiOpsDbContext : DbContext
{
    public AoiOpsDbContext(DbContextOptions<AoiOpsDbContext> options) : base(options)
    {
    }

    public DbSet<Tool> Tools => Set<Tool>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<Wafer> Wafers => Set<Wafer>();
    public DbSet<ProcessRun> ProcessRuns => Set<ProcessRun>();
    public DbSet<Alarm> Alarms => Set<Alarm>();
    public DbSet<Defect> Defects => Set<Defect>();
    public DbSet<DefectImage> DefectImages => Set<DefectImage>();
    public DbSet<DefectReview> DefectReviews => Set<DefectReview>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<CopilotQuery> CopilotQueries => Set<CopilotQuery>();
    public DbSet<Workorder> Workorders => Set<Workorder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 這裡用 Fluent API 明確指定 table/column 命名與關聯。
        // 原因：避免 EF 的預設命名在長期演進時造成 migration 難以追蹤。
        //
        // 共同約定（新手先記這句就好）：
        // - 所有表都用 Guid 當主鍵，預設由 Postgres 的 gen_random_uuid() 產生。
        // - 這樣做可以讓「事件流（Kafka/RabbitMQ）」在落 DB 前就先產生 id，追溯更容易。

        modelBuilder.Entity<Tool>(b =>
        {
            b.ToTable("tools");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.ToolCode).HasColumnName("tool_code").HasMaxLength(100).IsRequired();
            b.Property(x => x.ToolName).HasColumnName("tool_name").HasMaxLength(200).IsRequired();
            b.Property(x => x.ToolType).HasColumnName("tool_type").HasMaxLength(100);
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(50);
            b.Property(x => x.Location).HasColumnName("location").HasMaxLength(200);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.ToolCode).IsUnique();
        });

        modelBuilder.Entity<Recipe>(b =>
        {
            b.ToTable("recipes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
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
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).IsRequired();
            b.Property(x => x.ProductCode).HasColumnName("product_code").HasMaxLength(100);
            b.Property(x => x.Quantity).HasColumnName("quantity");
            b.Property(x => x.StartTime).HasColumnName("start_time");
            b.Property(x => x.EndTime).HasColumnName("end_time");
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(50);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.LotNo).IsUnique();
        });

        modelBuilder.Entity<Wafer>(b =>
        {
            b.ToTable("wafers");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.LotId).HasColumnName("lot_id");
            b.Property(x => x.WaferNo).HasColumnName("wafer_no").HasMaxLength(50).IsRequired();
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(50);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new { x.LotId, x.WaferNo }).IsUnique();
        });

        modelBuilder.Entity<ProcessRun>(b =>
        {
            b.ToTable("process_runs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.ToolId).HasColumnName("tool_id");
            b.Property(x => x.RecipeId).HasColumnName("recipe_id");
            b.Property(x => x.LotId).HasColumnName("lot_id");
            b.Property(x => x.WaferId).HasColumnName("wafer_id");
            b.Property(x => x.RunStartAt).HasColumnName("run_start_at");
            b.Property(x => x.RunEndAt).HasColumnName("run_end_at");
            b.Property(x => x.Temperature).HasColumnName("temperature").HasColumnType("decimal(18,4)");
            b.Property(x => x.Pressure).HasColumnName("pressure").HasColumnType("decimal(18,4)");
            b.Property(x => x.YieldRate).HasColumnName("yield_rate").HasColumnType("decimal(5,4)");
            b.Property(x => x.ResultStatus).HasColumnName("result_status").HasMaxLength(50);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.RunStartAt);
        });

        modelBuilder.Entity<Alarm>(b =>
        {
            b.ToTable("alarms");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.ToolId).HasColumnName("tool_id");
            b.Property(x => x.ProcessRunId).HasColumnName("process_run_id");
            b.Property(x => x.AlarmCode).HasColumnName("alarm_code").HasMaxLength(100).IsRequired();
            b.Property(x => x.AlarmLevel).HasColumnName("alarm_level").HasMaxLength(50);
            b.Property(x => x.Message).HasColumnName("message").HasMaxLength(2000);
            b.Property(x => x.TriggeredAt).HasColumnName("triggered_at");
            b.Property(x => x.ClearedAt).HasColumnName("cleared_at");
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(50);
            b.Property(x => x.Source).HasColumnName("source").HasMaxLength(50);
            b.HasIndex(x => x.TriggeredAt);
        });

        modelBuilder.Entity<Defect>(b =>
        {
            b.ToTable("defects");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.ToolId).HasColumnName("tool_id");
            b.Property(x => x.LotId).HasColumnName("lot_id");
            b.Property(x => x.WaferId).HasColumnName("wafer_id");
            b.Property(x => x.ProcessRunId).HasColumnName("process_run_id");
            b.Property(x => x.DefectCode).HasColumnName("defect_code").HasMaxLength(100).IsRequired();
            b.Property(x => x.DefectType).HasColumnName("defect_type").HasMaxLength(100);
            b.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(50);
            b.Property(x => x.XCoord).HasColumnName("x_coord").HasColumnType("decimal(18,4)");
            b.Property(x => x.YCoord).HasColumnName("y_coord").HasColumnType("decimal(18,4)");
            b.Property(x => x.DetectedAt).HasColumnName("detected_at");
            b.Property(x => x.IsFalseAlarm).HasColumnName("is_false_alarm");
            b.Property(x => x.KafkaEventId).HasColumnName("kafka_event_id").HasMaxLength(200);
            b.HasIndex(x => x.DetectedAt);
        });

        modelBuilder.Entity<DefectImage>(b =>
        {
            b.ToTable("defect_images");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.DefectId).HasColumnName("defect_id");
            b.Property(x => x.ImagePath).HasColumnName("image_path").HasMaxLength(2000).IsRequired();
            b.Property(x => x.ThumbnailPath).HasColumnName("thumbnail_path").HasMaxLength(2000);
            b.Property(x => x.Width).HasColumnName("width");
            b.Property(x => x.Height).HasColumnName("height");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.DefectId);
        });

        modelBuilder.Entity<DefectReview>(b =>
        {
            b.ToTable("defect_reviews");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.DefectId).HasColumnName("defect_id");
            b.Property(x => x.Reviewer).HasColumnName("reviewer").HasMaxLength(200).IsRequired();
            b.Property(x => x.ReviewResult).HasColumnName("review_result").HasMaxLength(50).IsRequired();
            b.Property(x => x.ReviewComment).HasColumnName("review_comment").HasMaxLength(4000);
            b.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
            b.HasIndex(x => x.DefectId);
        });

        modelBuilder.Entity<Document>(b =>
        {
            b.ToTable("documents");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
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
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.DocumentId).HasColumnName("document_id");
            b.Property(x => x.ChunkText).HasColumnName("chunk_text").IsRequired();
            b.Property(x => x.ChunkIndex).HasColumnName("chunk_index");
            b.Property(x => x.EmbeddingId).HasColumnName("embedding_id").HasMaxLength(200);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new { x.DocumentId, x.ChunkIndex }).IsUnique();
        });

        modelBuilder.Entity<CopilotQuery>(b =>
        {
            b.ToTable("copilot_queries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.QueryText).HasColumnName("query_text").IsRequired();
            b.Property(x => x.RelatedAlarmId).HasColumnName("related_alarm_id");
            b.Property(x => x.RelatedDefectId).HasColumnName("related_defect_id");
            b.Property(x => x.AnswerText).HasColumnName("answer_text");
            b.Property(x => x.SourceRefs).HasColumnName("source_refs");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<Workorder>(b =>
        {
            b.ToTable("workorders");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.LotId).HasColumnName("lot_id");
            b.Property(x => x.WorkorderNo).HasColumnName("workorder_no").HasMaxLength(100).IsRequired();
            b.Property(x => x.Priority).HasColumnName("priority").HasMaxLength(50);
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(50);
            b.Property(x => x.SourceQueue).HasColumnName("source_queue").HasMaxLength(50).IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.WorkorderNo).IsUnique();
        });
    }
}

