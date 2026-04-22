namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Recipe（配方/參數版本）主檔。
/// 設計目的：讓 process_run 能回溯「當次跑的參數版本」，
/// 以支援後續的 yield/defect/alarm 追因與趨勢分析。
/// </summary>
public sealed class Recipe
{
    public long Id { get; set; }

    public string RecipeCode { get; set; } = null!;

    public string RecipeName { get; set; } = null!;

    public string? Version { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

