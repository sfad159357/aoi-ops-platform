using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Controllers;

/// <summary>
/// Workorders API。
/// </summary>
/// <remarks>
/// 為什麼這次刪掉 GroupJoin：
/// - workorders 表已自帶 lot_no 冗餘欄位，建立時由 Worker 寫入；
///   list query 直接 .Select 即可，避免 SelectMany / DefaultIfEmpty 帶來的 query plan 複雜度。
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public sealed class WorkordersController : ControllerBase
{
    private readonly AoiOpsDbContext _db;

    public WorkordersController(AoiOpsDbContext db)
    {
        _db = db;
    }

    // 為什麼 DTO 把 panel/tool/line/station/operator/severity 都拋出去：
    // - 前端工單管理頁要顯示「板、機台、產線、站、開單人、嚴重度」六個關聯欄位；
    // - workorders 表已自帶這些冗餘欄位，DTO 直接拋出，無 JOIN 成本。
    public sealed record WorkorderListItemDto(
        Guid Id,
        string WorkorderNo,
        string? Priority,
        string? Status,
        string? SourceQueue,
        DateTimeOffset CreatedAt,
        string? LotNo,
        string? PanelNo,
        string? ToolCode,
        string? LineCode,
        string? StationCode,
        string? OperatorCode,
        string? OperatorName,
        string? Severity,
        string? DefectCode);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkorderListItemDto>>> List(
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);

        var items = await _db.Workorders
            .AsNoTracking()
            .OrderByDescending(w => w.CreatedAt)
            .Take(take)
            .Select(w => new WorkorderListItemDto(
                w.Id,
                w.WorkorderNo,
                w.Priority,
                w.Status,
                w.SourceQueue,
                w.CreatedAt,
                w.LotNo,
                w.PanelNo,
                w.ToolCode,
                w.LineCode,
                w.StationCode,
                w.OperatorCode,
                w.OperatorName,
                w.Severity,
                w.DefectCode))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}
