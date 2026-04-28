namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Line（產線）主檔，例如 SMT-A / SMT-B / AOI-A。
/// </summary>
/// <remarks>
/// 為什麼把 line 從 domain profile 拉出來變成資料表：
/// - 原本 line 只在 profile JSON 裡當設定，tools 表只有 location 字串卻沒有 FK；
///   ERD 上看不出 tool 屬於哪條線，工程師無法用 SQL JOIN 追溯。
/// - 改成獨立資料表後，tools.line_id 可建 FK，未來 SPC / Yield 報表能依產線聚合。
///
/// 為什麼保留 line_code 當業務唯一鍵：
/// - 與 profile JSON 的 lines[].code 一致，seed 可直接從 profile 載入並對齊。
/// </remarks>
public sealed class Line
{
    public Guid Id { get; set; }

    /// <summary>產線業務代碼，例如 SMT-A，全廠唯一。</summary>
    public string LineCode { get; set; } = null!;

    /// <summary>產線顯示名稱（中文），例如「SMT 線 A」。</summary>
    public string LineName { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>navigation：屬於這條線的所有機台。</summary>
    public ICollection<Tool> Tools { get; set; } = new List<Tool>();
}
