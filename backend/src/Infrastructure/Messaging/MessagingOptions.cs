// MessagingOptions：Kafka 與 RabbitMQ 連線設定的 strongly-typed options。
//
// 為什麼用 IOptions<T> 模式：
// - 連線字串、topic、group id 等設定要從 appsettings.json 或環境變數來，
//   IOptions<T> 是 .NET 官方建議做法，相比直接讀 IConfiguration["..."] 更可測試、更不會打錯字。
// - docker-compose 可以靠 ASPNETCORE 的 environment variable binding（雙底線取代冒號）
//   把 Messaging__Kafka__BootstrapServers 帶入容器，部署時不用改 code。
//
// 解決什麼問題：
// - 集中所有訊息中介軟體設定，避免散落在各 worker 的建構子裡寫死字串。

namespace AOIOpsPlatform.Infrastructure.Messaging;

/// <summary>
/// Kafka 連線與消費群設定。
/// </summary>
/// <remarks>
/// 為什麼提供 GroupIdSpcRealtime：
/// - 同一條 topic 會被多種 worker 消費（influx-writer、db-writer、SpcRealtimeWorker）。
///   每個 worker 都需要獨立的 consumer group，才能拿到「全部訊息」而不是被搶。
/// - 把每個 group 的 id 集中在 options 中，後續 demo 想重置 offset 也好處理。
/// </remarks>
public sealed class KafkaOptions
{
    public const string SectionName = "Messaging:Kafka";

    public string BootstrapServers { get; set; } = "kafka:9092";
    public string TopicInspectionRaw { get; set; } = "aoi.inspection.raw";
    public string TopicDefectEvent { get; set; } = "aoi.defect.event";
    public string GroupIdSpcRealtime { get; set; } = "aoiops-spc-realtime";
    public string GroupIdDefectRealtime { get; set; } = "aoiops-defect-realtime";

    /// <summary>
    /// 開發階段預設從最新 offset 讀，避免重新啟動就回放幾千筆 demo 點。
    /// </summary>
    public string AutoOffsetReset { get; set; } = "latest";
}

/// <summary>
/// RabbitMQ 連線與佇列設定。
/// </summary>
/// <remarks>
/// 為什麼把 queue 名稱直接寫在 options：
/// - alert / workorder queue 的名稱是和 Python publisher 共同約定，
///   寫在 config 比寫在程式碼裡更不容易因 refactor 而走鐘。
/// </remarks>
public sealed class RabbitMqOptions
{
    public const string SectionName = "Messaging:RabbitMq";

    public string HostName { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// 為什麼預設改成 "alert" / "workorder"：
    /// - 與 services/kafka-consumers/rabbitmq-publisher 端 Python 寫入的 queue 名稱一致；
    ///   保持單一事實來源，避免兩邊命名不對。
    /// </summary>
    public string QueueAlert { get; set; } = "alert";
    public string QueueWorkorder { get; set; } = "workorder";
}
