namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Alarm（告警）事件。
/// 目的：對應 process_run 的異常訊號，支援 alarm list 與後續的 context copilot 查詢。
/// </summary>
public sealed class Alarm
{
    public long Id { get; set; }

    public long ToolId { get; set; }

    public long ProcessRunId { get; set; }

    public string AlarmCode { get; set; } = null!;

    public string? AlarmLevel { get; set; }

    public string? Message { get; set; }

    public DateTimeOffset TriggeredAt { get; set; }

    public DateTimeOffset? ClearedAt { get; set; }

    public string? Status { get; set; }
}

