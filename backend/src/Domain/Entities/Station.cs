namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Station（站別）主檔：PCB 高階製程 SPI / SMT / REFLOW / AOI / ICT / FQC。
/// </summary>
/// <remarks>
/// 為什麼站別也落 DB 一張表：
/// - panel_station_log.station_code 之前是純字串，DB 無法擋拼錯（例：REFL0W）。
/// - 改成 FK→stations.station_code，可從 DB 層保證一致；報表也能 join 取中文 label/seq。
/// - profile JSON 仍是「設定來源」，seed 啟動時把 profile.stations 寫入 stations 表，達到單一真相。
///
/// 為什麼用 station_code 作為 FK key（而不是 Guid id）：
/// - station_code 是穩定的業務鍵（SPI 永遠是 SPI），子表直接吃字串可讀性最高、不需 join；
/// - 我們仍保留 Guid id 做為 PK，給未來如「站別重命名」之類的特殊情境留彈性。
/// </remarks>
public sealed class Station
{
    public Guid Id { get; set; }

    /// <summary>站別代碼：SPI / SMT / REFLOW / AOI / ICT / FQC。</summary>
    public string StationCode { get; set; } = null!;

    /// <summary>站別顯示名稱（中文），例如「錫膏印刷」。</summary>
    public string StationName { get; set; } = null!;

    /// <summary>站別在線體中的序號（用於排序）。</summary>
    public int Seq { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>navigation：所有對應的歷程記錄。</summary>
    public ICollection<PanelStationLog> StationLogs { get; set; } = new List<PanelStationLog>();
}
