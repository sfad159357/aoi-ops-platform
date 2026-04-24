namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Workorder（工單）。
/// 為什麼需要它：
/// - 架構加入 RabbitMQ 的 `workorder` queue 後，代表系統會收到「需要開工單」的業務事件。
/// - 我們用一張表把工單落地，才能在 Defect Review 流程中追蹤「這個 defect 是否已經有對應工單」。
///
/// 解決什麼問題：
/// - 若只用訊息不落 DB，你很難做查詢、稽核、或在 UI 顯示「工單狀態」。
/// </summary>
public sealed class Workorder
{
    public Guid Id { get; set; }

    public Guid? LotId { get; set; }

    public string WorkorderNo { get; set; } = null!;

    public string? Priority { get; set; }

    public string? Status { get; set; }

    /// <summary>
    /// 固定記錄這張工單是從哪個 queue 來的（例如：workorder）。
    /// 為什麼要留：新手在 debug 事件流時，最需要知道「資料從哪裡進來」。
    /// </summary>
    public string SourceQueue { get; set; } = "workorder";

    public DateTimeOffset CreatedAt { get; set; }
}

