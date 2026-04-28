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
    double? YieldRate,
    /// <summary>本則檢驗事件代表的件數／批量（預設 1）。供前端累積產出與速率與觀測點數對帳。</summary>
    int InspectedQty = 1,
    /// <summary>產線代碼（ingestion 端從機台對照表帶入），可避免 Worker 再去推測。</summary>
    string? LineCode = null,
    /// <summary>站別代碼（ingestion 端從機台對照表帶入）。</summary>
    string? StationCode = null,
    /// <summary>板號（ingestion 端組好），SPC 落地與前端追溯都會用。</summary>
    string? PanelNo = null,
    /// <summary>當下作業員代碼。</summary>
    string? OperatorCode = null,
    /// <summary>當下作業員顯示名稱。</summary>
    string? OperatorName = null);

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
    IReadOnlyList<SpcRuleViolation> Violations,
    /// <summary>與來源事件相同；累積產出 = 視窗內各點 InspectedQty 之和。</summary>
    int InspectedQty = 1,
    /// <summary>工單／批次號（Kafka lot_no），供前端與產線、機台並列追溯。</summary>
    string? LotNo = null,
    /// <summary>板／晶圓序號（Kafka wafer_no），與批次關聯。</summary>
    int? WaferNo = null,
    /// <summary>站別代碼（SPI/SMT/REFLOW/AOI/ICT/FQC），讓 SPC 圖也能標出當下站別。</summary>
    string? StationCode = null,
    /// <summary>板號字串，與 ingestion 寫 panels 對齊（lot_no-wafer_no）。</summary>
    string? PanelNo = null,
    /// <summary>當下作業員代碼。</summary>
    string? OperatorCode = null,
    /// <summary>當下作業員顯示名稱。</summary>
    string? OperatorName = null);

/// <summary>
/// 八大規則違規結果。
/// </summary>
public sealed record SpcRuleViolation(
    int RuleId,
    string RuleName,
    string Severity, // red / yellow / green
    string Description);
