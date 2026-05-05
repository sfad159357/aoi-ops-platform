using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Controllers;

/// <summary>
/// NCR（不良單）API。
/// </summary>
/// <remarks>
/// 為什麼這次刪掉 GroupJoin：
/// - workorders 表已自帶 lot_no 冗餘欄位，建立時由 Worker 寫入；
///   list query 直接 .Select 即可，避免 SelectMany / DefaultIfEmpty 帶來的 query plan 複雜度。
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public sealed class NcrsController : ControllerBase
{
    private readonly AoiOpsDbContext _db;

    public NcrsController(AoiOpsDbContext db)
    {
        _db = db;
    }

    // 為什麼 DTO 把 panel/tool/line/station/operator/severity 都拋出去：
    // - 前端工單管理頁要顯示「板、機台、產線、站、開單人、嚴重度」六個關聯欄位；
    // - workorders 表已自帶這些冗餘欄位，DTO 直接拋出，無 JOIN 成本。
    public sealed record NcrListItemDto(
        Guid Id,
        string NcrNo,
        string? Priority,
        string? Status,
        string? SourceQueue,
        DateTimeOffset CreatedAt,
        string? LotNo,
        Guid? ProductionWorkOrderId,
        string? WorkOrderNo,
        string? PanelNo,
        string? ToolCode,
        string? LineCode,
        string? StationCode,
        string? OperatorCode,
        string? OperatorName,
        string? Severity,
        string? DefectCode);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NcrListItemDto>>> List(
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);

        // 為什麼這裡要把 lot_no 關聯回 production_work_order：
        // - 4 大頁（NCR/異常/追溯/品質）都有 lot_no；使用者需要知道「這筆事件屬於哪張製令/工單」；
        // - 單一真相在 lots.production_work_order_id → production_work_order.work_order_no，
        //   controller 在這裡一次性 enrich，避免前端再多打一支 API。
        var items = await (
            from n in _db.Ncrs.AsNoTracking()
            join l in _db.Lots.AsNoTracking() on n.LotNo equals l.LotNo into lj
            from lot in lj.DefaultIfEmpty()
            join pwo in _db.ProductionWorkOrders.AsNoTracking() on lot.ProductionWorkOrderId equals pwo.Id into pj
            from wo in pj.DefaultIfEmpty()
            orderby n.CreatedAt descending
            select new NcrListItemDto(
                n.Id,
                n.NcrNo,
                n.Priority,
                n.Status,
                n.SourceQueue,
                n.CreatedAt,
                n.LotNo,
                lot != null ? lot.ProductionWorkOrderId : null,
                wo != null ? wo.WorkOrderNo : null,
                n.PanelNo,
                n.ToolCode,
                n.LineCode,
                n.StationCode,
                n.OperatorCode,
                n.OperatorName,
                n.Severity,
                n.DefectCode))
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}
