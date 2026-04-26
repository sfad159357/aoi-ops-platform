// SpcModels：SPC 即時推播事件的共用資料合約。
//
// 為什麼放在 Application 層：
// - Hub（在 Api 專案）與 Worker / RulesEngine（在 Application 專案）都會用到這幾個 record；
//   把它們集中在最內層、最少依賴的 Application 才不會造成循環引用。
// - record 比 class 適合做訊息合約（值型相等、不可變、ToString 預設友善）。
//
// 解決什麼問題：
// - 統一 Kafka payload → 規則引擎 → SignalR push 中間「同一筆事件」的型別，
//   避免每一層都重新定義一個 SpcPoint，造成 mapping bug。

namespace AOIOpsPlatform.Application.Spc;

/// <summary>
/// 從 Kafka <c>aoi.inspection.raw</c> 解析出來的單一量測事件。
/// </summary>
/// <remarks>
/// 為什麼要有這個型別：
/// - Kafka 訊息原本是 JSON dynamic dictionary，在 worker 內部直接傳 dict 容易拼錯欄位。
///   有了強型別 record，IDE 可幫忙檢查，後續移植 / refactor 更安全。
/// </remarks>
public sealed record SpcInspectionEvent(
    string EventId,
    string ToolCode,
    string LotNo,
    int WaferNo,
    DateTimeOffset Timestamp,
    double? Temperature,
    double? Pressure,
    double? YieldRate);

/// <summary>
/// 經過規則引擎處理後，要透過 SignalR 推給前端的單點。
/// </summary>
/// <remarks>
/// 為什麼把規則違規一起塞進 payload：
/// - 前端管制圖需要「同一個點」上同時知道是不是違規、是哪一條規則違規，
///   以便決定塗紅還是塗黃；分兩個事件推會增加同步問題。
/// </remarks>
public sealed record SpcPointPayload(
    string LineCode,
    string ToolCode,
    string ParameterCode,
    DateTimeOffset Timestamp,
    double Value,
    double Mean,
    double Sigma,
    double Ucl,
    double Cl,
    double Lcl,
    double? Cpk,
    IReadOnlyList<SpcRuleViolation> Violations);

/// <summary>
/// 八大規則違規結果。
/// </summary>
public sealed record SpcRuleViolation(
    int RuleId,
    string RuleName,
    string Severity, // red / yellow / green
    string Description);
