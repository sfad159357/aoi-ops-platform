using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Controllers;

/// <summary>
/// Alarms API。
/// </summary>
/// <remarks>
/// 為什麼這次拿掉 INNER JOIN tools：
/// - alarms 表已自帶 tool_code 冗餘欄位，新增告警時由 Worker 主動寫入；
///   list query 直接 .Select 即可，省下一次 JOIN，也避免遇到「沒 tool 對應就漏掉 alarm」的問題。
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public sealed class AlarmsController : ControllerBase
{
    private readonly AoiOpsDbContext _db;

    public AlarmsController(AoiOpsDbContext db)
    {
        _db = db;
    }

    // 為什麼 DTO 加 line/station/lot/panel/operator：
    // - 前端「異常記錄」頁要展示完整關聯欄位（誰、在哪條線哪個站、操作哪張板、哪個批次出包）；
    // - alarms 表已自帶這些冗餘欄位，DTO 直接拋出即可，沒有 JOIN 成本。
    public sealed record AlarmListItemDto(
        Guid Id,
        string AlarmCode,
        string? AlarmLevel,
        string? Message,
        DateTimeOffset TriggeredAt,
        DateTimeOffset? ClearedAt,
        string? Status,
        string? Source,
        string ToolCode,
        string? LineCode,
        string? StationCode,
        string? LotNo,
        Guid? ProductionWorkOrderId,
        string? WorkOrderNo,
        string? PanelNo,
        string? OperatorCode,
        string? OperatorName);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AlarmListItemDto>>> List(
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        // 為什麼上限固定 500：避免 client 傳超大數字打爆 DB / 前端；後續分頁可在 W11+ 補。
        take = Math.Clamp(take, 1, 500);

        // 為什麼要把 lot_no 關聯回 production_work_order：
        // - 告警列表常用 lot_no 當入口，但現場管理要看的是「這是哪張製令/工單」；
        // - 單一真相在 lots.production_work_order_id → production_work_order.work_order_no。
        var items = await (
            from a in _db.Alarms.AsNoTracking()
            join l in _db.Lots.AsNoTracking() on a.LotNo equals l.LotNo into lj
            from lot in lj.DefaultIfEmpty()
            join pwo in _db.ProductionWorkOrders.AsNoTracking() on lot.ProductionWorkOrderId equals pwo.Id into pj
            from wo in pj.DefaultIfEmpty()
            orderby a.TriggeredAt descending
            select new AlarmListItemDto(
                a.Id,
                a.AlarmCode,
                a.AlarmLevel,
                a.Message,
                a.TriggeredAt,
                a.ClearedAt,
                a.Status,
                a.Source,
                a.ToolCode,
                a.LineCode,
                a.StationCode,
                a.LotNo,
                lot != null ? lot.ProductionWorkOrderId : null,
                wo != null ? wo.WorkOrderNo : null,
                a.PanelNo,
                a.OperatorCode,
                a.OperatorName))
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}
