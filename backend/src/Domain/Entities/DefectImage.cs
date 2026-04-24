namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// DefectImage（缺陷影像）記錄。
/// 目的：把影像檔路徑與解析度等資訊正規化到獨立表，
/// 讓同一個 defect 可掛多張圖片（例如原圖、裁切、縮圖）。
/// </summary>
public sealed class DefectImage
{
    public Guid Id { get; set; }

    public Guid DefectId { get; set; }

    public string ImagePath { get; set; } = null!;

    public string? ThumbnailPath { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

