namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Alarm（告警）事件。
/// 目的：對應 process_run 的異常訊號，支援 alarm list 與後續的 context copilot 查詢。
/// </summary>
public sealed class Alarm
{
    public Guid Id { get; set; }

    public Guid ToolId { get; set; }

    public Guid? ProcessRunId { get; set; }

    public string AlarmCode { get; set; } = null!;

    public string? AlarmLevel { get; set; }

    public string? Message { get; set; }

    public DateTimeOffset TriggeredAt { get; set; }

    public DateTimeOffset? ClearedAt { get; set; }

    public string? Status { get; set; }

    /// <summary>
    /// 告警來源（kafka / rabbitmq / manual）。
    /// 為什麼要留這個欄位：當你後面加入 Kafka/RabbitMQ 後，告警可能來自不同通道；
    /// 留來源可以幫你 debug「是哪條資料管線出問題」。
    /// </summary>
    public string? Source { get; set; }
}

