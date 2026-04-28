namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// CopilotQuery（Copilot 問答記錄）。
/// 目的：保留使用者問句、回覆、與引用來源，並可選擇關聯到 alarm/defect 當作 context。
/// </summary>
/// <remarks>
/// 為什麼這次仍保持 nullable FK：
/// - 並非所有問題都要綁定 alarm 或 defect；保留可選關聯避免硬拼，FK 約束在 DbContext 設成 SetNull on delete。
/// </remarks>
public sealed class CopilotQuery
{
    public Guid Id { get; set; }

    public string QueryText { get; set; } = null!;

    public Guid? RelatedAlarmId { get; set; }

    public Guid? RelatedDefectId { get; set; }

    public string? AnswerText { get; set; }

    public string? SourceRefs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Alarm? RelatedAlarm { get; set; }
    public Defect? RelatedDefect { get; set; }
}
