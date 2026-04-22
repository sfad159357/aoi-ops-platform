namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// ProcessRun（一次製程/一次跑片的記錄）。
/// 目的：承接 tool/recipe/lot/wafer，並保留環境數據與結果，
/// 讓 dashboard 能做 yield trend 與異常追溯。
/// </summary>
public sealed class ProcessRun
{
    public long Id { get; set; }

    public long ToolId { get; set; }

    public long RecipeId { get; set; }

    public long LotId { get; set; }

    public long WaferId { get; set; }

    public DateTimeOffset RunStartAt { get; set; }

    public DateTimeOffset? RunEndAt { get; set; }

    public decimal? Temperature { get; set; }

    public decimal? Pressure { get; set; }

    public decimal? YieldRate { get; set; }

    public string? ResultStatus { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

