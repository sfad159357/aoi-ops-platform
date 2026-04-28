namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Tool（機台/設備）主檔。
/// 為了讓 Dashboard 與製程/告警/缺陷資料能夠依機台聚合查詢，
/// 我們將 tool_code 作為業務上的可讀識別碼，但 DB 仍以 Id 當主鍵。
/// </summary>
/// <remarks>
/// 為什麼這次新增 LineId / LineCode：
/// - 原本機台沒有 FK 連回 line，ERD 看不到「機台屬於哪條線」，
///   SPC / Yield 報表必須靠字串 prefix 推斷產線（例：tool_code = AOI-A），既不安全也不直觀。
/// - 加 LineId 之後 DB 能擋孤兒，並讓 EF navigation 直接讀到 Line 物件；
///   LineCode 同步冗餘，避免讀清單時還要 join lines。
/// </remarks>
public sealed class Tool
{
    public Guid Id { get; set; }

    public string ToolCode { get; set; } = null!;

    public string ToolName { get; set; } = null!;

    public string? ToolType { get; set; }

    public string? Status { get; set; }

    public string? Location { get; set; }

    /// <summary>所屬產線 PK（FK→lines.id）。</summary>
    public Guid? LineId { get; set; }

    /// <summary>冗餘：產線業務代碼，例如 SMT-A。讀清單不必 join lines。</summary>
    public string? LineCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Line? Line { get; set; }

    public ICollection<ProcessRun> ProcessRuns { get; set; } = new List<ProcessRun>();
    public ICollection<Alarm> Alarms { get; set; } = new List<Alarm>();
    public ICollection<Defect> Defects { get; set; } = new List<Defect>();
    public ICollection<SpcMeasurement> SpcMeasurements { get; set; } = new List<SpcMeasurement>();
}
