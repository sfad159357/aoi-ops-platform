namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Tool（機台/設備）主檔。
/// 為了讓 Dashboard 與製程/告警/缺陷資料能夠依機台聚合查詢，
/// 我們將 tool_code 作為業務上的可讀識別碼，但 DB 仍以 Id 當主鍵。
/// </summary>
public sealed class Tool
{
    // 為什麼用 Guid：
    // - OT/IT 整合時，事件可能先在 Kafka/RabbitMQ 流動再落 DB；Guid 更容易在「分散式系統」裡提前產生並追蹤。
    // - 也能避免不同資料來源合併時的自增主鍵撞號問題。
    public Guid Id { get; set; }

    public string ToolCode { get; set; } = null!;

    public string ToolName { get; set; } = null!;

    public string? ToolType { get; set; }

    public string? Status { get; set; }

    public string? Location { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
