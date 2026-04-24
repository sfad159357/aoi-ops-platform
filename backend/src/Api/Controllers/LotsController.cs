using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Controllers;

/// <summary>
/// Lots API（W02 第一個 list API）。
/// 為什麼從 lots 開始：
/// - LotNo 是製造場景最常見的查詢入口（工程師第一個問的通常是「這個 lot 發生什麼事」）。
/// - 做好 lots list，就能很快讓前端有資料可顯示，建立「DB → API → UI」的正向循環。
///
/// 解決什麼問題：
/// - 新手在 W02 最容易卡在「我有建表，但不知道 API 要回什麼」；
///   這支 API 只回最小必要欄位，先讓流程跑通，再逐步擴充。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class LotsController : ControllerBase
{
    private readonly AoiOpsDbContext _db;

    public LotsController(AoiOpsDbContext db)
    {
        _db = db;
    }

    public sealed record LotListItemDto(
        Guid Id,
        string LotNo,
        string? ProductCode,
        int? Quantity,
        DateTimeOffset? StartTime,
        DateTimeOffset? EndTime,
        string? Status,
        DateTimeOffset CreatedAt
    );

    /// <summary>
    /// 取得 lot 清單（MVP 版）。
    /// 為什麼先不做分頁：
    /// - W02 目標是打通端到端；分頁/排序屬於「之後優化」。
    /// - 當資料量變大（上千筆）才需要分頁，先別把新手卡在複雜度上。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LotListItemDto>>> List(CancellationToken cancellationToken)
    {
        var items = await _db.Lots
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new LotListItemDto(
                x.Id,
                x.LotNo,
                x.ProductCode,
                x.Quantity,
                x.StartTime,
                x.EndTime,
                x.Status,
                x.CreatedAt
            ))
            .Take(200)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}

