using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Controllers;

/// <summary>
/// Defects API（W02 第三支 list API）。
/// 為什麼要做 defects list：
/// - AOI 平台的核心就是「缺陷清單 → 缺陷詳情 → review」。
/// - 先把 list 做起來，W05 的 defect detail、W06 的 review flow 才有入口。
///
/// 解決什麼問題：
/// - 新手常卡在「資料有了，但不知道 UI 要從哪裡開始」；
///   defects list 就是最直覺的第一個頁面雛形。
/// </summary>
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
        string? ToolCode,
        Guid LotId,
        string? LotNo,
        Guid WaferId,
        string? WaferNo
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
        string? ToolCode,
        string? ToolName,
        Guid LotId,
        string? LotNo,
        Guid WaferId,
        string? WaferNo,
        Guid? ProcessRunId,
        IReadOnlyList<object> Images,
        IReadOnlyList<object> Reviews
    );

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DefectListItemDto>>> List(CancellationToken cancellationToken)
    {
        // 為什麼用 LEFT JOIN（DefaultIfEmpty）：
        // - MVP 階段資料可能不完整（例如 defect 先進來，但 lot/tool 還沒建好）。
        // - list API 不應因為關聯缺資料就整支爆掉，所以這裡允許 NULL，讓 UI 至少能顯示 defect 本體。
        var query =
            from d in _db.Defects.AsNoTracking()
            join t in _db.Tools.AsNoTracking() on d.ToolId equals t.Id into dt
            from t in dt.DefaultIfEmpty()
            join l in _db.Lots.AsNoTracking() on d.LotId equals l.Id into dl
            from l in dl.DefaultIfEmpty()
            join w in _db.Wafers.AsNoTracking() on d.WaferId equals w.Id into dw
            from w in dw.DefaultIfEmpty()
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
                t != null ? t.ToolCode : null,
                d.LotId,
                l != null ? l.LotNo : null,
                d.WaferId,
                w != null ? w.WaferNo : null
            );

        var items = await query
            .Take(200)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    /// <summary>
    /// 取得 defect 詳情（W05 起點）。
    /// 為什麼要先做 detail API：
    /// - UI 從 list 點進去 detail，才像真的 AOI review 系統。
    /// - 後面要做 image upload / review history，都會掛在 detail 頁。
    ///
    /// 解決什麼問題：
    /// - 新手如果直接做 review flow，會卡在「沒有 detail 頁可以放資訊」；
    ///   先把 detail 的資料形狀定下來，後面只是在這個 shape 上擴充欄位。
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DefectDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        // 為什麼這裡也用 LEFT JOIN：
        // - 允許資料不完整時仍能回傳 defect 本體（MVP/測試資料常見情況）。
        var query =
            from d in _db.Defects.AsNoTracking()
            where d.Id == id
            join t in _db.Tools.AsNoTracking() on d.ToolId equals t.Id into dt
            from t in dt.DefaultIfEmpty()
            join l in _db.Lots.AsNoTracking() on d.LotId equals l.Id into dl
            from l in dl.DefaultIfEmpty()
            join w in _db.Wafers.AsNoTracking() on d.WaferId equals w.Id into dw
            from w in dw.DefaultIfEmpty()
            select new DefectDetailDto(
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
                t != null ? t.ToolCode : null,
                t != null ? t.ToolName : null,
                d.LotId,
                l != null ? l.LotNo : null,
                d.WaferId,
                w != null ? w.WaferNo : null,
                d.ProcessRunId,
                Array.Empty<object>(),
                Array.Empty<object>()
            );

        var item = await query.FirstOrDefaultAsync(cancellationToken);
        if (item is null)
        {
            return NotFound(new { message = "defect not found", id });
        }

        return Ok(item);
    }
}

