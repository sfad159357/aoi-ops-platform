// DomainProfile：對應 shared/domain-profiles/*.json 的後端模型。
//
// 為什麼用 record + nullable list：
// - record 的 deserialize 由 System.Text.Json 預設支援，且天生不可變（profile 啟動後不該被人改）。
// - List 用 IReadOnlyList 暴露給呼叫端，避免 controller / worker 不小心改到單例。
//
// 解決什麼問題：
// - 把「同一份 codebase 跑 PCB 或半導體」的差異收斂到 JSON，
//   程式碼從「寫死字串」變成「從 profile 拿欄位」，未來新增第三產業只要加 JSON。

using System.Text.Json.Serialization;

namespace AOIOpsPlatform.Application.Domain;

/// <summary>
/// 域 (Domain) profile 主結構，對應 pcb.json / semiconductor.json 全文。
/// </summary>
/// <remarks>
/// 為什麼欄位用 snake_case：
/// - JSON 是工程師 / PM 共筆，snake_case 對非工程師更友善；
/// - .NET 用 [JsonPropertyName] 對應 snake_case，讀寫不會出錯。
/// </remarks>
public sealed class DomainProfile
{
    [JsonPropertyName("profile_id")]
    public string ProfileId { get; init; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("factory")]
    public DomainFactory Factory { get; init; } = new();

    [JsonPropertyName("entities")]
    public DomainEntities Entities { get; init; } = new();

    [JsonPropertyName("stations")]
    public IReadOnlyList<DomainStation> Stations { get; init; } = new List<DomainStation>();

    [JsonPropertyName("lines")]
    public IReadOnlyList<DomainLine> Lines { get; init; } = new List<DomainLine>();

    [JsonPropertyName("parameters")]
    public IReadOnlyList<DomainParameter> Parameters { get; init; } = new List<DomainParameter>();

    [JsonPropertyName("menus")]
    public IReadOnlyList<DomainMenu> Menus { get; init; } = new List<DomainMenu>();

    [JsonPropertyName("kpi")]
    public IReadOnlyDictionary<string, DomainKpi> Kpi { get; init; } = new Dictionary<string, DomainKpi>();

    [JsonPropertyName("wording")]
    public IReadOnlyDictionary<string, string> Wording { get; init; } = new Dictionary<string, string>();
}

public sealed class DomainFactory
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("site")] public string Site { get; init; } = string.Empty;
}

public sealed class DomainEntities
{
    [JsonPropertyName("panel")] public DomainEntity Panel { get; init; } = new();
    [JsonPropertyName("lot")]   public DomainEntity Lot   { get; init; } = new();
    [JsonPropertyName("tool")]  public DomainEntity Tool  { get; init; } = new();
}

public sealed class DomainEntity
{
    [JsonPropertyName("table")]    public string Table    { get; init; } = string.Empty;
    [JsonPropertyName("label_zh")] public string LabelZh  { get; init; } = string.Empty;
    [JsonPropertyName("id_prefix")] public string? IdPrefix { get; init; }
}

public sealed class DomainStation
{
    [JsonPropertyName("code")]    public string Code    { get; init; } = string.Empty;
    [JsonPropertyName("label_zh")] public string LabelZh { get; init; } = string.Empty;
    [JsonPropertyName("seq")]      public int Seq        { get; init; }
}

public sealed class DomainLine
{
    [JsonPropertyName("code")]    public string Code    { get; init; } = string.Empty;
    [JsonPropertyName("label_zh")] public string LabelZh { get; init; } = string.Empty;
}

public sealed class DomainParameter
{
    [JsonPropertyName("code")]    public string Code    { get; init; } = string.Empty;
    [JsonPropertyName("label_zh")] public string LabelZh { get; init; } = string.Empty;
    [JsonPropertyName("unit")]    public string Unit    { get; init; } = string.Empty;

    /// <summary>USL：規格上限。</summary>
    [JsonPropertyName("usl")] public double Usl { get; init; }
    /// <summary>LSL：規格下限。</summary>
    [JsonPropertyName("lsl")] public double Lsl { get; init; }
    /// <summary>Target：目標值，Cpk 計算用。</summary>
    [JsonPropertyName("target")] public double Target { get; init; }
}

public sealed class DomainMenu
{
    [JsonPropertyName("id")]      public string Id      { get; init; } = string.Empty;
    [JsonPropertyName("label_zh")] public string LabelZh { get; init; } = string.Empty;
}

public sealed class DomainKpi
{
    [JsonPropertyName("label_zh")] public string LabelZh { get; init; } = string.Empty;

    /// <summary>「越大越好」的閾值；超過 == 達標。</summary>
    [JsonPropertyName("good_threshold")] public double? GoodThreshold { get; init; }

    /// <summary>「越小越好」的閾值；低於 == 達標。</summary>
    [JsonPropertyName("good_threshold_lt")] public double? GoodThresholdLt { get; init; }
}
