namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Lot（批次 / 工單）資料。
/// 目的：讓使用者能以 lot_no 作為主要查詢入口，
/// 並串接 panels / process_runs / defects / workorders 形成可追溯的資料鏈。
/// </summary>
/// <remarks>
/// 為什麼加 navigation collections：
/// - controllers 之前要手動 LEFT JOIN 才能拿到對應 panels 與 workorders；
///   有 navigation 之後可以 .Include 一次取出，或讓子表透過 LotNo 冗餘欄位直接顯示，零 mapping。
/// </remarks>
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

    public ICollection<Panel> Panels { get; set; } = new List<Panel>();
    public ICollection<ProcessRun> ProcessRuns { get; set; } = new List<ProcessRun>();
    public ICollection<Defect> Defects { get; set; } = new List<Defect>();
    public ICollection<Workorder> Workorders { get; set; } = new List<Workorder>();
}
