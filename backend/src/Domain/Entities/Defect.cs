namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Defect（AOI 缺陷）主體資料。
/// 目的：把缺陷與 tool/lot/wafer/process_run 串起來，並提供 review 與相似查詢所需的欄位。
/// </summary>
public sealed class Defect
{
    public long Id { get; set; }

    public long ToolId { get; set; }

    public long LotId { get; set; }

    public long WaferId { get; set; }

    public long ProcessRunId { get; set; }

    public string DefectCode { get; set; } = null!;

    public string? DefectType { get; set; }

    public string? Severity { get; set; }

    public decimal? XCoord { get; set; }

    public decimal? YCoord { get; set; }

    public DateTimeOffset DetectedAt { get; set; }

    public bool IsFalseAlarm { get; set; }
}

