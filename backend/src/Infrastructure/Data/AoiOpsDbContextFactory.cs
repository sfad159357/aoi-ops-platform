using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AOIOpsPlatform.Infrastructure.Data;

/// <summary>
/// Design-time DbContext factory：給 dotnet ef 工具使用。
/// </summary>
/// <remarks>
/// 為什麼要有這個 factory：
/// - dotnet ef migrations add / database update 在執行時需要實例化 DbContext，
///   它會嘗試啟動 Program 取設定；若 host 在 design-time 啟動失敗會卡住。
/// - 我們把 design-time 的 provider 鎖定為 SQL Server，因為本專案的目標部署是 SQL Server / Azure SQL Edge。
///
/// 為什麼用固定假連線字串：
/// - 產生 migration 時 EF 不會真的連 DB（只需 schema 模型 + provider）；
///   給一個語法合法的 placeholder 即可，不需要 .env 也能跑 ef migrations add。
/// </remarks>
public sealed class AoiOpsDbContextFactory : IDesignTimeDbContextFactory<AoiOpsDbContext>
{
    public AoiOpsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AoiOpsDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=localhost,1433;Database=AOIOpsPlatform_MSSQL;User Id=sa;Password=design-time-only;TrustServerCertificate=True;");
        return new AoiOpsDbContext(optionsBuilder.Options);
    }
}
