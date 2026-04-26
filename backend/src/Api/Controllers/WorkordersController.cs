using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Controllers;

/// <summary>
/// Workorders API。
/// </summary>
/// <remarks>
/// 為什麼跟 Alarms 拆兩支 controller：
/// - 雖然兩者都是「事件驅動表格」，但工單有 priority / status 篩選需求，
///   未來會逐步擴張查詢條件；分開後不會互相干擾。
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

    /// <summary>
    /// 工單列表 DTO。
    /// </summary>
    /// <remarks>
    /// 為什麼不直接回 Lot：
    /// - 前端清單只需要 lot_no 字串，把 join 結果拍平成一個欄位最省心力。
    /// </remarks>
    public sealed record WorkorderListItemDto(
        Guid Id,
        string WorkorderNo,
        string? Priority,
        string? Status,
        string? SourceQueue,
        DateTimeOffset CreatedAt,
        string? LotNo);

    /// <summary>
    /// 取得近期工單清單，預設回最近 100 筆。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkorderListItemDto>>> List(
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);

        var items = await _db.Workorders
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .GroupJoin(
                _db.Lots.AsNoTracking(),
                w => w.LotId,
                l => l.Id,
                (w, lots) => new { w, lots })
            .SelectMany(
                x => x.lots.DefaultIfEmpty(),
                (x, l) => new WorkorderListItemDto(
                    x.w.Id,
                    x.w.WorkorderNo,
                    x.w.Priority,
                    x.w.Status,
                    x.w.SourceQueue,
                    x.w.CreatedAt,
                    l != null ? l.LotNo : null))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}
