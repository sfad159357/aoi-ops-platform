namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// 板與物料批號的多對多關聯表。
/// </summary>
/// <remarks>
/// 為什麼要這張中介表：
/// - 一張板會用到多種物料；同一個物料批號也會被多張板使用；
///   多對多關係必須用中介表才能查兩個方向：
///     1. 給定 panel_no → 列出所有用到的物料批號（追溯）
///     2. 給定 material_lot_no → 列出所有受影響的板（召回）
///
/// 為什麼用 (panel_id, material_lot_id) 當複合主鍵：
/// - 同一張板用同一批物料只該記一次；主鍵防呆比 unique index 更直接。
///
/// 為什麼新增 PanelNo / MaterialLotNo 冗餘：
/// - Trace API 與召回查詢頻繁需要可讀字串，避免每次都 JOIN panels / material_lots。
/// </remarks>
public sealed class PanelMaterialUsage
{
    public Guid PanelId { get; set; }
    public Guid MaterialLotId { get; set; }

    /// <summary>冗餘：對應板的 panel_no。</summary>
    public string PanelNo { get; set; } = null!;

    /// <summary>冗餘：對應物料批號 material_lot_no。</summary>
    public string MaterialLotNo { get; set; } = null!;

    /// <summary>用了多少（顆 / g / mL，視物料而定）。</summary>
    public decimal? Quantity { get; set; }

    public DateTimeOffset UsedAt { get; set; }

    public Panel? Panel { get; set; }
    public MaterialLot? MaterialLot { get; set; }
}
