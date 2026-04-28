namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Defect（AOI 缺陷）主體資料。
/// 目的：把缺陷與 tool/lot/panel/process_run 串起來，並提供 review 與相似查詢所需的欄位。
/// </summary>
/// <remarks>
/// 為什麼把 WaferId 改成 PanelId、並冗餘 ToolCode / LotNo / PanelNo：
/// - 原本 DefectsController 必須 LEFT JOIN tools/lots/wafers 才能取出可讀字串，
///   每次新增欄位都要動 controller。
/// - 改成在 entity 上直接帶這些字串後，DTO 投影只剩 entity 屬性，零 JOIN、零 mapping。
/// </remarks>
public sealed class Defect
{
    public Guid Id { get; set; }

    public Guid ToolId { get; set; }

    public Guid LotId { get; set; }

    public Guid PanelId { get; set; }

    public Guid? ProcessRunId { get; set; }

    public string ToolCode { get; set; } = null!;
    public string LotNo { get; set; } = null!;
    public string PanelNo { get; set; } = null!;

    /// <summary>冗餘：產線代碼，例如 SMT-A。</summary>
    public string? LineCode { get; set; }

    /// <summary>冗餘：站別代碼，例如 AOI / SPI / FQC。</summary>
    public string? StationCode { get; set; }

    /// <summary>冗餘：當下作業員 OperatorCode。</summary>
    public string? OperatorCode { get; set; }

    /// <summary>冗餘：當下作業員顯示名稱。</summary>
    public string? OperatorName { get; set; }

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

    public Tool? Tool { get; set; }
    public Lot? Lot { get; set; }
    public Panel? Panel { get; set; }
    public ProcessRun? ProcessRun { get; set; }

    public ICollection<DefectImage> Images { get; set; } = new List<DefectImage>();
    public ICollection<DefectReview> Reviews { get; set; } = new List<DefectReview>();
}
