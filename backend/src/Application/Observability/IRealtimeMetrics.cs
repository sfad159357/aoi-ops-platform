// IRealtimeMetrics：W11 觀測抽象。
//
// 為什麼放 Application 層：
// - workers（SpcRealtimeWorker / AlarmRabbitWorker / WorkorderRabbitWorker）都在 Application；
//   如果 metrics 抽象放 Infrastructure，會讓 Application 反向依賴 Infrastructure，違反 Clean Architecture。
//
// 解決什麼問題：
// - workers 只負責「告訴系統發生了什麼事」（吞吐 + 延遲），不需要知道指標如何被收集 / 暴露。
//   實際的 in-memory snapshot 由 Api 層的 RealtimeMetricsService 實作；未來想換 prometheus-net
//   只需重寫實作，不必改 worker。

namespace AOIOpsPlatform.Application.Observability;

/// <summary>
/// 即時推播相關的 metrics 收集介面（Application 層只會呼叫，不在意實作）。
/// </summary>
public interface IRealtimeMetrics
{
    /// <summary>
    /// 記錄一次 SPC 點推播（含計算時間 / push 延遲，毫秒）。
    /// </summary>
    /// <param name="hadViolation">該點是否同時觸發違規。</param>
    /// <param name="latencyMs">從 Kafka payload 解析完成到 SignalR PushAsync 結束的耗時。</param>
    void RecordSpcPoint(bool hadViolation, double latencyMs);

    /// <summary>
    /// 記錄一次 alarm 寫入 + 推播。
    /// </summary>
    void RecordAlarm(double latencyMs);

    /// <summary>
    /// 記錄一次 workorder 寫入 + 推播。
    /// </summary>
    void RecordWorkorder(double latencyMs);
}
