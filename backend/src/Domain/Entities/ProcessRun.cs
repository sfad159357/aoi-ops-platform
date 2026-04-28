namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// ProcessRun（一次製程/一次跑片的記錄）。
/// </summary>
/// <remarks>
/// 為什麼把原本的 WaferId 改成 PanelId：
/// - 系統已聚焦 PCB 製程，wafer 語意只會讓報表與前端欄位對不上。
/// - 同步冗餘 ToolCode / LotNo / PanelNo，讓 dashboard 直接 Select 不必 JOIN 三張母表。
/// </remarks>
public sealed class ProcessRun
{
    public Guid Id { get; set; }

    public Guid ToolId { get; set; }

    public Guid RecipeId { get; set; }

    public Guid LotId { get; set; }

    public Guid PanelId { get; set; }

    public string ToolCode { get; set; } = null!;
    public string LotNo { get; set; } = null!;
    public string PanelNo { get; set; } = null!;

    public DateTimeOffset RunStartAt { get; set; }

    public DateTimeOffset? RunEndAt { get; set; }

    public decimal? Temperature { get; set; }

    public decimal? Pressure { get; set; }

    public decimal? YieldRate { get; set; }

    public string? ResultStatus { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Tool? Tool { get; set; }
    public Recipe? Recipe { get; set; }
    public Lot? Lot { get; set; }
    public Panel? Panel { get; set; }

    public ICollection<Alarm> Alarms { get; set; } = new List<Alarm>();
    public ICollection<Defect> Defects { get; set; } = new List<Defect>();
}
