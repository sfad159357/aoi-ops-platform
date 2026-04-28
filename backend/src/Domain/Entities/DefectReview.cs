namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// DefectReview（缺陷覆判）記錄。
/// 目的：保留每一次 review 的結果與備註，支援 review history 與稽核追溯。
/// </summary>
/// <remarks>
/// 為什麼加 navigation：與 DefectImage 同樣理由，讓 Cascade Delete 在 EF / DB 層一致。
/// </remarks>
public sealed class DefectReview
{
    public Guid Id { get; set; }

    public Guid DefectId { get; set; }

    public string Reviewer { get; set; } = null!;

    public string ReviewResult { get; set; } = null!;

    public string? ReviewComment { get; set; }

    public DateTimeOffset ReviewedAt { get; set; }

    public Defect? Defect { get; set; }
}
