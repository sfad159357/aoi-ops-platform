namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Panel（PCB 板）主檔，取代舊版 Wafer。
/// </summary>
/// <remarks>
/// 為什麼把 wafers 改名 panels：
/// - 本平台已聚焦 PCB 高階製程（SPI/SMT/REFLOW/AOI/ICT/FQC），
///   wafer 半導體語意會讓 schema 與前端「板號」對不上，每次都要心算「wafer 就是 panel」。
/// - 直接改名後 controllers/seed/ingestion/前端型別都用 PanelNo，零 mapping。
///
/// 為什麼 PanelNo 從 nullable 升級成必填 + unique：
/// - PCB 工廠的「掃 QR Code 找一張板」流程仰賴全廠唯一字串；
/// - 既有資料即將砍掉重建，因此可以一次把 NOT NULL 約束建好，未來不會再有「沒 panel_no 的孤兒板」。
///
/// 為什麼保留 LotNo 冗餘欄位：
/// - controllers / SignalR payload 直接 Select 就能拿到字串，不需要 join lots；
/// - lot_no 一旦建立後幾乎不會被改寫，冗餘成本極低、查詢成本大幅降低。
/// </remarks>
public sealed class Panel
{
    public Guid Id { get; set; }

    /// <summary>所屬 Lot 的 PK（FK→lots.id）。</summary>
    public Guid LotId { get; set; }

    /// <summary>冗餘欄位：對應 Lot.LotNo，避免 query 時要再 join lots。</summary>
    public string LotNo { get; set; } = null!;

    /// <summary>板號，例如 PCB-20260428-LOT-001-1，必填且全廠唯一。</summary>
    public string PanelNo { get; set; } = null!;

    /// <summary>板的當前狀態：in_progress / pass / fail / scrap。</summary>
    public string? Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>navigation：所屬 Lot。</summary>
    public Lot? Lot { get; set; }

    /// <summary>navigation：所有相關製程紀錄。</summary>
    public ICollection<ProcessRun> ProcessRuns { get; set; } = new List<ProcessRun>();

    /// <summary>navigation：所有相關 defect。</summary>
    public ICollection<Defect> Defects { get; set; } = new List<Defect>();

    /// <summary>navigation：站別歷程。</summary>
    public ICollection<PanelStationLog> StationLogs { get; set; } = new List<PanelStationLog>();

    /// <summary>navigation：用料紀錄。</summary>
    public ICollection<PanelMaterialUsage> MaterialUsages { get; set; } = new List<PanelMaterialUsage>();

    /// <summary>navigation：SPC 量測值。</summary>
    public ICollection<SpcMeasurement> SpcMeasurements { get; set; } = new List<SpcMeasurement>();
}
