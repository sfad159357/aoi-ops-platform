namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Document（文件）主檔。
/// 目的：讓 Knowledge Copilot 能上傳 SOP/手冊並追蹤版本，
/// 後續將文件切塊後寫入 document_chunks 供檢索使用。
/// </summary>
public sealed class Document
{
    public Guid Id { get; set; }

    public string Title { get; set; } = null!;

    public string? DocType { get; set; }

    public string? Version { get; set; }

    public string? SourcePath { get; set; }

    public DateTimeOffset UploadedAt { get; set; }
}

