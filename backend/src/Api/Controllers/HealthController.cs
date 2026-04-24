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

        // EF Core 8 的 SqlQuery<T> 會把你的 SQL 包成子查詢（FROM (your_sql) AS t）。
        // 如果 SQL 結尾有分號「;」，PostgreSQL 的語法解析會爆 42601 syntax error。
        // 解決方法：移除結尾分號，讓 EF Core 自行決定 SQL 邊界。
        // 欄位命名為 "Value" 是因為 SqlQuery<bool> scalar projection 需要對應欄位名稱。
        FormattableString sql = $"""
                                 select exists (
                                   select 1
                                   from information_schema.tables
                                   where table_schema = 'public'
                                     and table_name = {"tools"}
                                 ) as "Value"
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
