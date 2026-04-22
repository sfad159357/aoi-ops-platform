using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 之所以在 Program.cs 集中註冊，是為了讓 docker-compose 啟動時只要提供 connection string，
// 就能讓 API 與 migrations/DbContext 使用同一份設定，避免環境不一致。
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    // MVP 階段：用明確錯誤快速指出環境變數/設定缺失，避免默默連到錯誤的 DB。
    throw new InvalidOperationException("Missing connection string 'Default'.");
}

builder.Services.AddDbContext<AoiOpsDbContext>(options =>
{
    // 改用 PostgreSQL：讓開發環境可用 docker-compose 快速啟動，並用開源 DB 方便跨平台協作。
    options.UseNpgsql(connectionString);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
    {
        // MVP 先允許前端本機 dev server 呼叫，後續再收斂到更嚴格的 origin 清單。
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowAnyOrigin();
    });
});

var app = builder.Build();

// 之所以在 middleware pipeline 前先做 DB 初始化：
// - 讓開發者打第一支 API 前，資料表就已經存在，降低「以為連線壞了其實只是沒建表」的誤判成本。
// - 仍限制只在 Development 執行（細節見 AoiOpsDbInitializer），避免 production 误用 EnsureCreated。
await AoiOpsDbInitializer.InitializeAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("DevCors");

app.MapControllers();

app.Run();
