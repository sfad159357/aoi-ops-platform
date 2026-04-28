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
        var items = await _db.Defects
            .AsNoTracking()
            .OrderByDescending(d => d.DetectedAt)
            .Take(200)
            .Select(d => new DefectListItemDto(
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
                d.PanelId,
                d.PanelNo,
                d.LineCode,
                d.StationCode,
                d.OperatorCode,
                d.OperatorName,
                d.XCoord,
                d.YCoord
            ))
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
        var item = await _db.Defects
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Include(d => d.Tool)
            .Select(d => new DefectDetailDto(
                d.Id,
                d.DefectCode,
                d.DefectType,
                d.Severity,
                d.XCoord,
                d.YCoord,
                d.DetectedAt,
                d.IsFalseAlarm,
                d.KafkaEventId,
                d.ToolId,
                d.ToolCode,
                d.Tool != null ? d.Tool.ToolName : null,
                d.LotId,
                d.LotNo,
                d.PanelId,
                d.PanelNo,
                d.ProcessRunId,
                Array.Empty<object>(),
                Array.Empty<object>()
            ))
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return NotFound(new { message = "defect not found", id });
        }

        return Ok(item);
    }
}
