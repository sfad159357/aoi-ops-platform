using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Controllers;

/// <summary>
/// Tools API（W02/W03 常用的第二個 list API）。
/// 為什麼要做 tools list：
/// - Dashboard 常見的第一個 filter 就是「選機台（tool）」。
/// - 有 tools 清單後，前端才能做下拉選單或篩選條件，讓查詢更像真實 MES。
///
/// 解決什麼問題：
/// - 新手常不知道下一步要做什麼 API；tools list 是最通用、後面到處用得到的一支。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ToolsController : ControllerBase
{
    private readonly AoiOpsDbContext _db;

    public ToolsController(AoiOpsDbContext db)
    {
        _db = db;
    }

    public sealed record ToolListItemDto(
        Guid Id,
        string ToolCode,
        string ToolName,
        string? ToolType,
        string? Status,
        string? Location,
        DateTimeOffset CreatedAt
    );

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ToolListItemDto>>> List(CancellationToken cancellationToken)
    {
        // 為什麼 AsNoTracking：
        // - list API 只讀資料，不需要 EF 幫你追蹤變更；關掉 tracking 會更快、也省記憶體。
        var items = await _db.Tools
            .AsNoTracking()
            .OrderBy(x => x.ToolCode)
            .Select(x => new ToolListItemDto(
                x.Id,
                x.ToolCode,
                x.ToolName,
                x.ToolType,
                x.Status,
                x.Location,
                x.CreatedAt
            ))
            .Take(200)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}

