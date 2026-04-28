namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// 物料批號（錫膏 / 電容 / FR4 / 助焊劑 / 主晶片…）。
/// </summary>
/// <remarks>
/// 為什麼用「物料批號」而不是「物料品項」：
/// - PCB 真實追溯時，懷疑同一批物料造成不良；
///   所以需要儲存「整批的識別碼」與「來源廠商 / 收貨日」，
///   發生問題能一鍵 query 同批物料用在哪些板上。
///
/// 為什麼新增 navigation：
/// - 與 PanelMaterialUsage 形成正反兩向 navigation，方便 EF 查詢與 Cascade 設定。
/// </remarks>
public sealed class MaterialLot
{
    public Guid Id { get; set; }

    /// <summary>物料批號，例如 SOLDER-2024-0419-001。</summary>
    public string MaterialLotNo { get; set; } = null!;

    /// <summary>物料類型，例如 solder_paste / capacitor / fr4 / flux / chip。</summary>
    public string MaterialType { get; set; } = null!;

    /// <summary>顯示用名稱，例如「錫膏 SAC305 0.5kg」。</summary>
    public string? MaterialName { get; set; }

    /// <summary>供應商。</summary>
    public string? Supplier { get; set; }

    /// <summary>批次到貨日 / 上線日。</summary>
    public DateTimeOffset? ReceivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<PanelMaterialUsage> Usages { get; set; } = new List<PanelMaterialUsage>();
}
