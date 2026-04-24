namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// CopilotQuery（Copilot 問答記錄）。
/// 目的：保留使用者問句、回覆、與引用來源，並可選擇關聯到 alarm/defect 當作 context。
/// </summary>
public sealed class CopilotQuery
{
    public Guid Id { get; set; }

    public string QueryText { get; set; } = null!;

    public Guid? RelatedAlarmId { get; set; }

    public Guid? RelatedDefectId { get; set; }

    public string? AnswerText { get; set; }

    public string? SourceRefs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

