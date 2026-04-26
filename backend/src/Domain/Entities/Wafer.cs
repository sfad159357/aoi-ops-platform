namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Wafer 表保留半導體語意，PCB profile 透過 domain profile 顯示為「板（panel）」。
/// </summary>
/// <remarks>
/// 為什麼 schema 名稱不改：
/// - 改表名意味要動 migration / 全部 SQL；用 domain profile 改顯示用語就夠，
///   讓「同一份 codebase 跑兩個產業 demo」變得便宜。
/// </remarks>
public sealed class Wafer
{
    public Guid Id { get; set; }

    public Guid LotId { get; set; }

    public string WaferNo { get; set; } = null!;

    /// <summary>
    /// 對外可讀的板 / 晶圓識別碼，例如 PCB-20240422-0087。
    /// </summary>
    /// <remarks>
    /// 為什麼除了 wafer_no 還要 panel_no：
    /// - wafer_no 在 lot 內遞增（"1"/"2"…），跨 lot 不唯一；
    ///   工程師在現場想用「掃 QR Code」直接找一張板的歷程，需要全廠唯一字串。
    /// - 留 nullable 是為了相容既有 seed 資料（W08 之後新生才填）。
    /// </remarks>
    public string? PanelNo { get; set; }

    public string? Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

