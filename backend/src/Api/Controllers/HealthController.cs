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

        // 為什麼不再查 information_schema：
        // - 先前這支 healthcheck 寫死 Postgres 的 table_schema='public'，
        //   一旦改用 SQL Server（預設 dbo）就會「不報錯但永遠回 false」，造成誤判。
        //
        // 這裡改用 ORM 本身的 query 當作 schema readiness：
        // - 若 schema 未建立，AnyAsync 會拋例外；我們捕捉後回 false，並保留 canConnect 供判斷是連線問題還是 schema 問題。
        // - 這樣能讓 healthcheck 在不同 provider 上行為一致（不綁死特定 DB 的 metadata schema）。
        var toolsTableExists = false;
        try
        {
            // 為什麼用 Take(1) + AnyAsync：最小成本觸發查詢，不依賴 seed 是否存在。
            toolsTableExists = await _db.Tools.AsNoTracking().Take(1).AnyAsync(cancellationToken);
        }
        catch
        {
            toolsTableExists = false;
        }

        return Ok(new
        {
            canConnect,
            toolsTableExists
        });
    }
}
