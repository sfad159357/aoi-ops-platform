namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// ProductionWorkOrder（生產工單／製令／MO/WO）。
/// </summary>
/// <remarks>
/// 為什麼要新增這個主檔：
/// - 先前系統把 lot_no（WO-yyyymmdd-xxx）當作「工單」在用，容易與實際 MES 語意混淆；
///   真實現場通常是「生產工單（製令）→ 多個批次/lot → 多片 panel → 過站/量測/追溯」。
/// - 因此這個 entity 用來承接「生產指令」層級的欄位（料號、計畫數量、狀態、起訖），
///   並讓 Lot 只代表「批次/流轉單位」，避免用詞歧義。
/// </remarks>
public sealed class ProductionWorkOrder
{
    public Guid Id { get; set; }

    /// <summary>生產工單號（例如：MO-20260505-0001）。</summary>
    public string WorkOrderNo { get; set; } = null!;

    /// <summary>成品料號/產品料號。</summary>
    public string? ProductCode { get; set; }

    /// <summary>計畫生產數量（以 panel 為單位；實務可依工廠定義）。</summary>
    public int? PlannedQuantity { get; set; }

    /// <summary>狀態：planned / released / in_progress / completed / cancelled。</summary>
    public string? Status { get; set; }

    public DateTimeOffset? PlannedStartAt { get; set; }

    public DateTimeOffset? PlannedEndAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>navigation：此生產工單拆分出的批次（lots）。</summary>
    public ICollection<Lot> Lots { get; set; } = new List<Lot>();
}

