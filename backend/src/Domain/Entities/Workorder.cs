namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Workorder（工單）。
/// </summary>
/// <remarks>
/// 為什麼新增 LotNo 冗餘欄位：
/// - 原本 WorkordersController 用 GroupJoin 才能拿到 lot_no，每次需要做 select-many；
///   冗餘後直接 Select Workorder entity 即可。
/// - 工單建立時就把 lot_no 字串帶入，後續即使 lots 表偶爾被刪也不影響顯示（保留歷史快照）。
/// </remarks>
public sealed class Workorder
{
    public Guid Id { get; set; }

    public Guid? LotId { get; set; }

    public Guid? ToolId { get; set; }

    public Guid? PanelId { get; set; }

    /// <summary>冗餘：建立工單當下的 lot_no 字串，免 JOIN。</summary>
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

    /// <summary>冗餘：缺陷代碼（觸發此工單的 defect_code）。</summary>
    public string? DefectCode { get; set; }

    public string WorkorderNo { get; set; } = null!;

    public string? Priority { get; set; }

    public string? Status { get; set; }

    /// <summary>
    /// 固定記錄這張工單是從哪個 queue 來的（例如：workorder）。
    /// 為什麼要留：新手在 debug 事件流時，最需要知道「資料從哪裡進來」。
    /// </summary>
    public string SourceQueue { get; set; } = "workorder";

    public DateTimeOffset CreatedAt { get; set; }

    public Lot? Lot { get; set; }
    public Tool? Tool { get; set; }
    public Panel? Panel { get; set; }
}
