// IKafkaMessageHandler / IRabbitMessageHandler：訊息處理介面。
//
// 為什麼放在 Application 層：
// - 依 Clean Architecture，Application 對外定義「我需要什麼」，Infrastructure 負責「怎麼做」。
//   把介面放在 Application，Infrastructure 的 hosted service 才能依賴它，
//   Application 的 worker 也可以實作它，避免反向相依。
// - 介面本身與 Confluent.Kafka / RabbitMQ.Client 完全無關，純粹是 string in / Task out，
//   非常適合留在最內層。

namespace AOIOpsPlatform.Application.Messaging;

/// <summary>
/// 處理單一筆 Kafka 訊息的 handler 介面。
/// </summary>
/// <remarks>
/// 為什麼回傳 Task：
/// - 大部分 handler 會非同步寫 DB、推 SignalR；同步版本反而會阻塞 consumer 迴圈。
/// </remarks>
public interface IKafkaMessageHandler
{
    /// <summary>
    /// 對應的 Kafka topic；hosted service 啟動時用來建立 subscription 清單。
    /// </summary>
    string Topic { get; }

    /// <summary>
    /// 拿到一筆訊息後的處理邏輯。
    /// </summary>
    /// <remarks>
    /// 為什麼把 key / value 都丟進來（而不是只給 value）：
    /// - SPC 點要用 toolCode 當 partition key，handler 偶爾需要從 key 還原，
    ///   留著比較有彈性。
    /// </remarks>
    Task HandleAsync(string? key, string value, CancellationToken cancellationToken);
}

/// <summary>
/// 處理 RabbitMQ 訊息的 handler。
/// </summary>
public interface IRabbitMessageHandler
{
    /// <summary>
    /// 對應的 queue 名稱。
    /// </summary>
    string Queue { get; }

    /// <summary>
    /// 處理 message body，回傳 true=ack / false=nack 不重排。
    /// </summary>
    /// <remarks>
    /// 為什麼用 bool 表示 ack/nack：
    /// - 異常 / 工單失敗一定要明確訊號，AutoAck 會在 broker 端就刪掉訊息，無法救回。
    /// </remarks>
    Task<bool> HandleAsync(string body, CancellationToken cancellationToken);
}
