using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Controllers;

/// <summary>
/// Defects API：列表 + 詳情。
/// </summary>
/// <remarks>
/// 為什麼這次砍掉所有 LEFT JOIN：
/// - schema 重建後，<c>defects</c> 表已直接帶 <c>tool_code / lot_no / panel_no</c> 冗餘欄位；
///   DTO 投影只需要 entity 屬性，零 JOIN、零手動 mapping，前端 JSON 也直接對齊。
/// - 這比起 controller 寫 join 既快（DB 不必跑多表 hash join），也比較不容易在改欄位時破功。
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public sealed class DefectsController : ControllerBase
{
    private readonly AoiOpsDbContext _db;

    public DefectsController(AoiOpsDbContext db)
    {
        _db = db;
    }

    public sealed record DefectListItemDto(
        Guid Id,
        string DefectCode,
        string? DefectType,
        string? Severity,
        DateTimeOffset DetectedAt,
        bool IsFalseAlarm,
        string? KafkaEventId,
        Guid ToolId,
        string ToolCode,
        Guid LotId,
        string LotNo,
        Guid? ProductionWorkOrderId,
        string? WorkOrderNo,
        Guid PanelId,
        string PanelNo,
        // 為什麼補 line/station/operator：
        // - 物料追溯查詢、QC review 都需要看「在哪條線哪個站、誰操作的」，避免再回頭 JOIN tools/operators。
        string? LineCode,
        string? StationCode,
        string? OperatorCode,
        string? OperatorName,
        decimal? XCoord,
        decimal? YCoord
    );

    public sealed record DefectDetailDto(
        Guid Id,
        string DefectCode,
        string? DefectType,
        string? Severity,
        decimal? XCoord,
        decimal? YCoord,
        DateTimeOffset DetectedAt,
        bool IsFalseAlarm,
        string? KafkaEventId,
        Guid ToolId,
        string ToolCode,
        string? ToolName,
        Guid LotId,
        string LotNo,
        Guid? ProductionWorkOrderId,
        string? WorkOrderNo,
        Guid PanelId,
        string PanelNo,
        Guid? ProcessRunId,
        IReadOnlyList<object> Images,
        IReadOnlyList<object> Reviews
    );

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DefectListItemDto>>> List(CancellationToken cancellationToken)
    {
        // 為什麼直接 .Select：
        // - defect 已自帶 tool_code/lot_no/panel_no，省掉 JOIN 與多表 round-trip；
        // - 排序仍照 detected_at desc 給前端最新優先。
        // 為什麼 defects 也要關聯回 production_work_order：
        // - 缺陷追溯常從 defect_code 或 panel_no 進來，但管理層需要快速彙整到工單層級；
        // - lot_id 是穩定 FK，可直接 join lots → production_work_order，避免靠字串推測。
        var items = await (
            from d in _db.Defects.AsNoTracking()
            join l in _db.Lots.AsNoTracking() on d.LotId equals l.Id
            join pwo in _db.ProductionWorkOrders.AsNoTracking() on l.ProductionWorkOrderId equals pwo.Id into pj
            from wo in pj.DefaultIfEmpty()
            orderby d.DetectedAt descending
            select new DefectListItemDto(
                d.Id,
                d.DefectCode,
                d.DefectType,
                d.Severity,
                d.DetectedAt,
                d.IsFalseAlarm,
                d.KafkaEventId,
                d.ToolId,
                d.ToolCode,
                d.LotId,
                d.LotNo,
                l.ProductionWorkOrderId,
                wo != null ? wo.WorkOrderNo : null,
                d.PanelId,
                d.PanelNo,
                d.LineCode,
                d.StationCode,
                d.OperatorCode,
                d.OperatorName,
                d.XCoord,
                d.YCoord
            ))
            .Take(200)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    /// <summary>
    /// 取得 defect 詳情。
    /// </summary>
    /// <remarks>
    /// 為什麼仍 .Include(Tool)：
    /// - detail 需要 ToolName（不像 list 只要 ToolCode），這個欄位沒冗餘到 defects；
    ///   .Include 走 navigation 比再寫 join 直觀，EF 會自動產生 LEFT JOIN。
    /// </remarks>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DefectDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var item = await (
            from d in _db.Defects.AsNoTracking()
            where d.Id == id
            join l in _db.Lots.AsNoTracking() on d.LotId equals l.Id
            join pwo in _db.ProductionWorkOrders.AsNoTracking() on l.ProductionWorkOrderId equals pwo.Id into pj
            from wo in pj.DefaultIfEmpty()
            select new
            {
                Defect = d,
                Lot = l,
                WorkOrderNo = wo != null ? wo.WorkOrderNo : null,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return NotFound(new { message = "defect not found", id });
        }

        // 為什麼 detail 要額外查 toolName：
        // - ToolName 沒冗餘在 defects；這裡保持最小變更，僅在 detail 再查一次 tools。
        var toolName = await _db.Tools
            .AsNoTracking()
            .Where(t => t.Id == item.Defect.ToolId)
            .Select(t => t.ToolName)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new DefectDetailDto(
            item.Defect.Id,
            item.Defect.DefectCode,
            item.Defect.DefectType,
            item.Defect.Severity,
            item.Defect.XCoord,
            item.Defect.YCoord,
            item.Defect.DetectedAt,
            item.Defect.IsFalseAlarm,
            item.Defect.KafkaEventId,
            item.Defect.ToolId,
            item.Defect.ToolCode,
            toolName,
            item.Defect.LotId,
            item.Defect.LotNo,
            item.Lot.ProductionWorkOrderId,
            item.WorkOrderNo,
            item.Defect.PanelId,
            item.Defect.PanelNo,
            item.Defect.ProcessRunId,
            Array.Empty<object>(),
            Array.Empty<object>()
        ));
    }
}
