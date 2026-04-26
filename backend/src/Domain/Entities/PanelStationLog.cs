namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// 板在每一站的進站 / 出站時間軸 log。
/// </summary>
/// <remarks>
/// 為什麼不靠 process_runs 直接做時間軸：
/// - process_runs 是「製程的執行紀錄」，欄位偏向「機台跑了什麼參數」；
///   時間軸需要的「進 SPI、過 Reflow、AOI 結果」是站別事件，語意層級不同。
/// - 把站別事件獨立成一張表，未來增加新站（例如 X-RAY）只要 insert，
///   不會打亂 process_runs 既有 schema。
///
/// 為什麼欄位選 panel_id（而非 wafer_id）：
/// - 對外語意層次「PCB 板」更貼近 HTML 設計圖（Tab 2 物料追溯）；
///   但 schema 仍指向 wafers.id 維持單一真相。
/// </remarks>
public sealed class PanelStationLog
{
    public Guid Id { get; set; }
    public Guid PanelId { get; set; }

    /// <summary>站別代碼：SPI / SMT / REFLOW / AOI / ICT / FQC。</summary>
    public string StationCode { get; set; } = null!;

    /// <summary>進站時間。</summary>
    public DateTimeOffset EnteredAt { get; set; }

    /// <summary>出站時間（離站才填，否則 null 代表還在站）。</summary>
    public DateTimeOffset? ExitedAt { get; set; }

    /// <summary>該站結果：pass / fail / warn / skip。</summary>
    public string? Result { get; set; }

    /// <summary>站別作業員 / 機台代碼，方便後續 join 找責任方。</summary>
    public string? Operator { get; set; }

    /// <summary>備註，例如 AOI 站可記下 defect_count。</summary>
    public string? Note { get; set; }
}
