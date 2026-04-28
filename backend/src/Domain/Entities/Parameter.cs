namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Parameter（量測參數）主檔，對齊 PCB profile 的 parameters[]：
/// solder_thickness / reflow_peak_temp / hole_tolerance 等。
/// </summary>
/// <remarks>
/// 為什麼把 parameter 從 profile 拉到 DB：
/// - SPC 即時點原本只在 Kafka 流動，沒落 DB；
///   一旦你想做「歷史曲線回放」「日報跑批」就會卡住，因為 DB 完全沒這些值。
/// - 改成 spc_measurements.parameter_id FK→parameters.id 後，每筆量測值都能用 join 拿到 USL/LSL/target，
///   報表查詢一次到位。
///
/// 為什麼把 USL/LSL/target 放在 parameters 表而不是只放 profile：
/// - 真實工廠的規格會隨時調整（換料、換配方時 LSL 會升降），
///   留在 DB 才能在歷史記錄上保留「當時的 spec」做後續稽核。
/// </remarks>
public sealed class Parameter
{
    public Guid Id { get; set; }

    /// <summary>參數代碼，例如 solder_thickness，與 profile 一致。</summary>
    public string ParameterCode { get; set; } = null!;

    /// <summary>參數中文名稱，例如「錫膏厚度」。</summary>
    public string ParameterName { get; set; } = null!;

    /// <summary>單位，例如 μm / °C / %。</summary>
    public string? Unit { get; set; }

    /// <summary>規格上限（Upper Spec Limit）。</summary>
    public decimal Usl { get; set; }

    /// <summary>規格下限（Lower Spec Limit）。</summary>
    public decimal Lsl { get; set; }

    /// <summary>目標值，Cpk 計算用。</summary>
    public decimal Target { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>navigation：所有對應的量測值。</summary>
    public ICollection<SpcMeasurement> Measurements { get; set; } = new List<SpcMeasurement>();
}
