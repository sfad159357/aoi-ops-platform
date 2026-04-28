namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Alarm（告警）事件。
/// 目的：對應 process_run 的異常訊號，支援 alarm list 與後續的 context copilot 查詢。
/// </summary>
/// <remarks>
/// 為什麼新增 ToolCode 冗餘欄位：
/// - 原本 AlarmsController 必須 INNER JOIN tools 才能拿 tool_code；
///   冗餘後直接 Select Alarm entity 一次取出，不需要寫任何 JOIN，前端 JSON 的 toolCode 就是 entity 屬性。
/// </remarks>
public sealed class Alarm
{
    public Guid Id { get; set; }

    public Guid ToolId { get; set; }

    public Guid? ProcessRunId { get; set; }

    /// <summary>冗餘欄位：tool 的業務代碼，避免 list query 還要 JOIN tools。</summary>
    public string ToolCode { get; set; } = null!;

    /// <summary>冗餘：產線代碼，例如 SMT-A。</summary>
    public string? LineCode { get; set; }

    /// <summary>冗餘：站別代碼，例如 SPI / AOI / FQC。</summary>
    public string? StationCode { get; set; }

    /// <summary>冗餘：批次/工單號，例如 WO-20260428-001。</summary>
    public string? LotNo { get; set; }

    /// <summary>冗餘：板號，例如 PCB-20260428-WO-001-3。</summary>
    public string? PanelNo { get; set; }

    /// <summary>冗餘：值班人員（OperatorCode），例如 OP-001。</summary>
    public string? OperatorCode { get; set; }

    /// <summary>冗餘：值班人員顯示名稱，例如「王小明」。</summary>
    public string? OperatorName { get; set; }

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

    public Tool? Tool { get; set; }
    public ProcessRun? ProcessRun { get; set; }
}
