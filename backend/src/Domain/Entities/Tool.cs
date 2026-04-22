namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Tool（機台/設備）主檔。
/// 為了讓 Dashboard 與製程/告警/缺陷資料能夠依機台聚合查詢，
/// 我們將 tool_code 作為業務上的可讀識別碼，但 DB 仍以 Id 當主鍵。
/// </summary>
public sealed class Tool
{
    public long Id { get; set; }

    public string ToolCode { get; set; } = null!;

    public string ToolName { get; set; } = null!;

    public string? ToolType { get; set; }

    public string? Status { get; set; }

    public string? Location { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
