namespace AOIOpsPlatform.Domain;

/// <summary>
/// 這個檔案是 .NET 範本產生的預設類別。
/// 為了避免「空專案看起來有東西但其實沒有用」造成後續維護困擾，
/// 我們先把它改成簡單的 marker 型別，並保留存在的原因。
/// </summary>
public static class DomainAssemblyMarker
{
    /// <summary>
    /// 之後用於掃描 Domain layer（例如組態、映射、驗證規則）時，
    /// 可以用此型別作為 anchor，避免硬編字串或路徑。
    /// </summary>
    public static readonly string Name = typeof(DomainAssemblyMarker).Assembly.GetName().Name ?? "AOIOpsPlatform.Domain";
}
