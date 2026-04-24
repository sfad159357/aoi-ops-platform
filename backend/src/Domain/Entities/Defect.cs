namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Defect（AOI 缺陷）主體資料。
/// 目的：把缺陷與 tool/lot/wafer/process_run 串起來，並提供 review 與相似查詢所需的欄位。
/// </summary>
public sealed class Defect
{
    public Guid Id { get; set; }

    public Guid ToolId { get; set; }

    public Guid LotId { get; set; }

    public Guid WaferId { get; set; }

    public Guid? ProcessRunId { get; set; }

    public string DefectCode { get; set; } = null!;

    public string? DefectType { get; set; }

    public string? Severity { get; set; }

    public decimal? XCoord { get; set; }

    public decimal? YCoord { get; set; }

    public DateTimeOffset DetectedAt { get; set; }

    public bool IsFalseAlarm { get; set; }

    /// <summary>
    /// Kafka event id（對應 ERD 的 kafka_event_id）。
    /// 為什麼要留：當你需要追查「這筆 defect 是哪一則 Kafka 訊息產生的」時，就靠它串回去。
    /// </summary>
    public string? KafkaEventId { get; set; }
}

