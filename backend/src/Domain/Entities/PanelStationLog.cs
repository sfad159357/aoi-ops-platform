namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// 板在每一站的進站 / 出站時間軸 log。
/// </summary>
/// <remarks>
/// 為什麼不靠 process_runs 直接做時間軸：
/// - process_runs 偏向「機台跑了什麼參數」，而站別事件（進 SPI、過 Reflow、AOI 結果）語意層級不同。
/// - 把站別事件獨立成一張表，未來增加新站（例如 X-RAY）只要 insert，不會打亂 process_runs schema。
///
/// 為什麼新增 PanelNo 冗餘：
/// - Trace API 顯示時間軸時可直接 Select 字串，不必再 JOIN panels 表。
/// </remarks>
public sealed class PanelStationLog
{
    public Guid Id { get; set; }
    public Guid PanelId { get; set; }

    /// <summary>站別代碼：SPI / SMT / REFLOW / AOI / ICT / FQC（FK→stations.station_code）。</summary>
    public string StationCode { get; set; } = null!;

    /// <summary>冗餘：對應板的 panel_no，避免追溯查詢還要 JOIN panels。</summary>
    public string PanelNo { get; set; } = null!;

    /// <summary>進站時間。</summary>
    public DateTimeOffset EnteredAt { get; set; }

    /// <summary>出站時間（離站才填，否則 null 代表還在站）。</summary>
    public DateTimeOffset? ExitedAt { get; set; }

    /// <summary>該站結果：pass / fail / warn / skip。</summary>
    public string? Result { get; set; }

    /// <summary>站別作業員 OperatorCode（沿用舊欄位名稱，下游 alias 為 OperatorCode）。</summary>
    public string? Operator { get; set; }

    /// <summary>冗餘：作業員顯示名稱，例如「王小明」。</summary>
    public string? OperatorName { get; set; }

    /// <summary>冗餘：站別當下使用的機台代碼（同站可能有 2 台 AOI）。</summary>
    public string? ToolCode { get; set; }

    /// <summary>備註，例如 AOI 站可記下 defect_count。</summary>
    public string? Note { get; set; }

    public Panel? Panel { get; set; }
    public Station? Station { get; set; }
}
