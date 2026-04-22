using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Controllers;

/// <summary>
/// Health API：用最小成本驗證「後端 ↔ PostgreSQL」是否真的能通。
/// 為什麼要獨立一個 controller：
/// - 連線問題最常發生在 compose 網路、連線字串、帳密、DB 尚未初始化等；用一個固定 endpoint 能快速定位。
/// 解決什麼問題：
/// - 避免只能靠「打業務 API 才爆錯」來猜測是 DB 還是程式邏輯問題。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class HealthController : ControllerBase
{
    private readonly AoiOpsDbContext _db;

    public HealthController(AoiOpsDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// 檢查 DB 連線是否可用，並回傳 tools 資料表是否存在（用於確認 schema 是否已建立）。
    /// 為什麼查 information_schema：
    /// - 比直接 query 業務資料更穩定（即使還沒 seed，也不會因為空表而誤判）。
    /// </summary>
    [HttpGet("db")]
    public async Task<ActionResult<object>> Db(CancellationToken cancellationToken)
    {
        var canConnect = await _db.Database.CanConnectAsync(cancellationToken);

        // EF Core 8 的 SqlQuery<T> 在「非 scalar 投影」時對欄位命名較敏感；
        // 這裡直接查 bool scalar，並把輸出欄位命名為 Value（對齊官方文件對 scalar composition 的慣例）。
        FormattableString sql = $"""
                                 select exists (
                                   select 1
                                   from information_schema.tables
                                   where table_schema = 'public'
                                     and table_name = {"tools"}
                                 ) as "Value";
                                 """;

        var toolsTableExists = await _db.Database
            .SqlQuery<bool>(sql)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new
        {
            canConnect,
            toolsTableExists
        });
    }
}
