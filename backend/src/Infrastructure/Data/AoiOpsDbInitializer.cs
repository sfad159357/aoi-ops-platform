using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AOIOpsPlatform.Infrastructure.Data;

/// <summary>
/// DB 初始化器：在開發環境把「連線 + schema」一次打通。
/// 為什麼這樣寫：
/// - MVP 階段我們還沒引入 migrations 管線時，先用 EnsureCreated 讓 docker-compose / 本機開發能立刻驗證 end-to-end。
/// - Production 不應使用 EnsureCreated（難以演進、也不適合團隊協作），因此這裡嚴格限制只在 Development 執行。
/// 解決什麼問題：
/// - 避免「API 能起來但 DB 沒表」造成 Swagger/前端查詢全失敗，卻難以定位是連線還是 schema 問題。
/// </summary>
public static class AoiOpsDbInitializer
{
    /// <summary>
    /// 在應用程式啟動時初始化資料庫。
    /// 為什麼用 IServiceProvider：
    /// - 讓我們在最小改動下取得 scoped DbContext，符合 EF Core 生命週期。
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (!env.IsDevelopment())
        {
            // Production 走 migrations / DBA 流程；這裡刻意不做任何事，避免誤把開發捷徑帶上線。
            return;
        }

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AOIOpsPlatform.Infrastructure.Data.AoiOpsDbInitializer");

        var db = scope.ServiceProvider.GetRequiredService<AoiOpsDbContext>();

        // EnsureCreated：依目前 model 直接建立資料庫與資料表（僅適合早期 MVP）。
        // 後續若導入 migrations，應移除此段並改為自動/手動執行 dotnet ef database update。
        var created = await db.Database.EnsureCreatedAsync(cancellationToken);
        logger.LogInformation("Database EnsureCreated completed. Created={Created}", created);
    }
}
