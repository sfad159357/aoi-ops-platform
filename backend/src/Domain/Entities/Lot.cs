namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Lot（批次）資料。
/// 目的：讓使用者能以 lot_no 作為主要查詢入口，
/// 並串接 wafers/process_runs/defects 形成可追溯的資料鏈。
/// </summary>
public sealed class Lot
{
    public Guid Id { get; set; }

    public string LotNo { get; set; } = null!;

    public string? ProductCode { get; set; }

    public int? Quantity { get; set; }

    public DateTimeOffset? StartTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public string? Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

