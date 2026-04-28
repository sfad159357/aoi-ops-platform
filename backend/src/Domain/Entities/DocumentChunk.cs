namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// DocumentChunk（文件切塊）。
/// 目的：將文件拆成可檢索的最小單位，並保留 chunk_index 以便回溯原文段落。
/// embedding_id 先存對外部向量/embedding 的參照，MVP 不強制導入向量資料庫。
/// </summary>
public sealed class DocumentChunk
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public string ChunkText { get; set; } = null!;

    public int ChunkIndex { get; set; }

    public string? EmbeddingId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Document? Document { get; set; }
}
