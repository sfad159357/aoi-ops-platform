namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Ncr（不良單／異常處置單）。
/// </summary>
/// <remarks>
/// 為什麼從 Workorder 改名成 NCR：
/// - 在 MES 業務語言中，Work Order 通常指「生產工單／製令」；
///   本系統原本的 workorders 實際上是「由缺陷/異常事件觸發的處置追蹤單」，更接近 NCR/CAPA ticket。
/// - 改名後可以避免使用者把它誤解成「規劃生產數量/料號/途程」那種生產指令單。
///
/// 為什麼仍保留 lot/panel/tool/站別/人員冗餘字串：
/// - NCR 列表頁主要是查詢/稽核與責任追蹤；用冗餘快照避免每次查詢都需要 JOIN，
///   也避免母表歸檔後 NCR 無法顯示當時資訊。
/// </remarks>
public sealed class Ncr
{
    public Guid Id { get; set; }

    public Guid? LotId { get; set; }

    public Guid? ToolId { get; set; }

    public Guid? PanelId { get; set; }

    /// <summary>冗餘：建立 NCR 當下的 lot_no 字串，免 JOIN。</summary>
    public string? LotNo { get; set; }

    /// <summary>冗餘：板號，免 JOIN。</summary>
    public string? PanelNo { get; set; }

    /// <summary>冗餘：機台代碼，免 JOIN。</summary>
    public string? ToolCode { get; set; }

    /// <summary>冗餘：產線代碼。</summary>
    public string? LineCode { get; set; }

    /// <summary>冗餘：站別代碼。</summary>
    public string? StationCode { get; set; }

    /// <summary>冗餘：開單人/責任人 OperatorCode。</summary>
    public string? OperatorCode { get; set; }

    /// <summary>冗餘：開單人顯示名稱。</summary>
    public string? OperatorName { get; set; }

    /// <summary>冗餘：嚴重度（high/medium/low），與優先級對應。</summary>
    public string? Severity { get; set; }

    /// <summary>冗餘：缺陷代碼（觸發此 NCR 的 defect_code）。</summary>
    public string? DefectCode { get; set; }

    /// <summary>NCR 編號（可讀識別碼）。</summary>
    public string NcrNo { get; set; } = null!;

    public string? Priority { get; set; }

    public string? Status { get; set; }

    /// <summary>
    /// 固定記錄這張 NCR 是從哪個 queue 來的（例如：ncr）。
    /// 為什麼要留：新手在 debug 事件流時，最需要知道「資料從哪裡進來」。
    /// </summary>
    public string SourceQueue { get; set; } = "ncr";

    public DateTimeOffset CreatedAt { get; set; }

    public Lot? Lot { get; set; }
    public Tool? Tool { get; set; }
    public Panel? Panel { get; set; }
}

