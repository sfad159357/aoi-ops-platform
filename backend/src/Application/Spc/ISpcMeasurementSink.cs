namespace AOIOpsPlatform.Application.Spc;

/// <summary>
/// SPC 量測值寫入的抽象。
/// </summary>
/// <remarks>
/// 為什麼把 sink 抽成介面：
/// - 真正寫 DB 的實作放在 Infrastructure（依賴 EF DbContext），
///   但 SpcRealtimeWorker 在 Application 層只需要 enqueue 一筆值。
/// - 透過介面解耦，未來改成 InfluxDB 或 Kafka topic 也只動 Infrastructure。
///
/// 為什麼用 Enqueue 而非 SaveAsync：
/// - 即時 stream 不能等 DB 同步寫完，否則 push SignalR 的延遲會累積。
/// - Enqueue 進 Channel + 背景 batch flush 是 .NET 的標準慣用做法。
/// </remarks>
public interface ISpcMeasurementSink
{
    /// <summary>
    /// 將一筆量測點放進寫入佇列；保證不阻塞 caller。
    /// </summary>
    void Enqueue(SpcMeasurementWriteRequest request);
}

/// <summary>
/// 提供 SPC sink 寫入 DB 所需的所有欄位。
/// </summary>
/// <remarks>
/// 為什麼用 record + 字串業務鍵（panelNo/toolCode/parameterCode）：
/// - SpcRealtimeWorker 不持有 PK Guid，但握有業務鍵；
///   讓 sink 在背景批次解析 code → id（並做 cache），可大幅降低 enqueue 端的負擔。
/// </remarks>
public sealed record SpcMeasurementWriteRequest(
    string LineCode,
    string ToolCode,
    string ParameterCode,
    string? PanelNo,
    string? StationCode,
    decimal Value,
    DateTimeOffset MeasuredAt,
    bool IsViolation,
    string? ViolationCodes,
    string? KafkaEventId,
    /// <summary>批次/工單號，落地後便於 SPC 報表依 lot 聚合。</summary>
    string? LotNo = null,
    /// <summary>當下作業員代碼。</summary>
    string? OperatorCode = null,
    /// <summary>當下作業員顯示名稱。</summary>
    string? OperatorName = null);
