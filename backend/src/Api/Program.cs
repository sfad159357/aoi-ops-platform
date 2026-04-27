using AOIOpsPlatform.Api.Hubs;
using AOIOpsPlatform.Api.Observability;
using AOIOpsPlatform.Api.Realtime;
using AOIOpsPlatform.Application.Domain;
using AOIOpsPlatform.Application.Hubs;
using AOIOpsPlatform.Application.Messaging;
using AOIOpsPlatform.Application.Observability;
using AOIOpsPlatform.Application.Workers;
using AOIOpsPlatform.Infrastructure.Data;
using AOIOpsPlatform.Infrastructure.Messaging;
using AOIOpsPlatform.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

// W11：Serilog bootstrap logger。
// 為什麼用 bootstrap pattern：
// - DI 還沒建立前發生的錯誤（appsettings 缺欄位、DB 連線失敗）也能寫到 stdout；
//   這對 docker compose 啟動失敗時除錯特別關鍵。
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    Log.Information("AOI Ops Platform backend starting up");

    var builder = WebApplication.CreateBuilder(args);

    // 為什麼在程式內再強制指定 URLs：
    // - 目前容器現象是「ASPNETCORE_URLS 已設定但 Kestrel 完全沒有 listen」，導致所有 API 連不上也不會回 500；
    // - 在 docker-compose 內同時存在 ASPNETCORE_URLS / ASPNETCORE_HTTP_PORTS（或被 runtime 注入）時，
    //   某些環境會造成 host 無法決定要綁哪個位址而直接不啟動 listener。
    // - 這裡用 `UseKestrel + UseUrls` 讓綁定位址「只由程式決定」，避免環境差異造成假死。
    builder.WebHost
        .UseKestrel()
        // 為什麼用 http://+:8080：
        // - `0.0.0.0` 在少數容器/網路堆疊下可能產生 bind 行為異常；
        // - `+` 是 ASP.NET Core 官方的 wildcard 語法，等同於「所有介面」，較能跨環境一致運作。
        .UseUrls("http://+:8080");

    // W11：把實際的 Serilog 接到 host，並讓它從 appsettings.json 讀取覆蓋設定。
    // 為什麼選 ReadFrom.Configuration：上線時可在 appsettings.Production.json 改 LogLevel 而不必改程式。
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("service", "aoiops-backend")
        .WriteTo.Console(new CompactJsonFormatter()));

    // 之所以在 Program.cs 集中註冊，是為了讓 docker-compose 啟動時只要提供 connection string，
    // 就能讓 API 與 migrations/DbContext 使用同一份設定，避免環境不一致。
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // 為什麼啟用 SignalR：
    // - W04 的核心是把 Kafka 訊息透過 WebSocket 即時推給前端；SignalR 是 .NET 棧最直接的解。
    // - AddSignalR 預設啟 WebSocket，傳輸不到時會自動降級 SSE / Long Polling。
    builder.Services.AddSignalR(options =>
    {
        // 為什麼放寬 MaximumReceiveMessageSize：
        // - 預設 32KB 足夠 SPC 單點，但若未來推「批次補歷史 100 點」會超過上限。
        options.MaximumReceiveMessageSize = 256 * 1024;
    });

    var connectionString = builder.Configuration.GetConnectionString("Default");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        // MVP 階段：用明確錯誤快速指出環境變數/設定缺失，避免默默連到錯誤的 DB。
        throw new InvalidOperationException("Missing connection string 'Default'.");
    }

    builder.Services.AddDbContext<AoiOpsDbContext>(options =>
    {
        // 為什麼在這裡做 DB provider 分流（而不是散落在各處）：
        // - DbContext 是所有 Controller / Worker 共用的基礎設施；集中在 Program.cs 做選擇，
        //   才能符合 DIP（高層不依賴特定 DB），換 DB 時只要改設定與 compose env。
        //
        // 解決什麼問題：
        // - 先前專案因為 initializer/healthcheck 有 Postgres DDL，導致「換 DB 需要大改」；
        //   這裡先把「provider 選擇」抽象成設定，降低之後切換成本。
        //
        // 設定來源：
        // - docker-compose 可以提供 Database__Provider=postgres|sqlserver
        // - 若未提供，預設沿用既有 PostgreSQL 行為，避免影響現有環境。
        var provider = builder.Configuration["Database:Provider"]?.Trim().ToLowerInvariant() ?? "postgres";

        if (provider is "sqlserver" or "mssql")
        {
            // 為什麼用 UseSqlServer：EF Core 官方 provider，對 Azure SQL Edge / SQL Server 相容。
            options.UseSqlServer(connectionString);
        }
        else
        {
            // 既有預設：PostgreSQL（Npgsql provider）
            options.UseNpgsql(connectionString);
        }
    });

    // 為什麼用 Configure<T> 模式：
    // - 從 appsettings.json / docker env 讀 KafkaOptions / RabbitMqOptions，集中管理連線資訊。
    // - docker-compose 環境變數的命名規則為 Messaging__Kafka__BootstrapServers（雙底線取代冒號）。
    builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.SectionName));
    builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));

    // 為什麼把 broker 註冊成 singleton：
    // - IHubContext 本身是 singleton；包裝它的 broker 沒狀態，singleton 最省資源。
    builder.Services.AddSingleton<ISpcHubBroker, SpcHubBroker>();
    builder.Services.AddSingleton<IAlarmHubBroker, AlarmHubBroker>();
    builder.Services.AddSingleton<IWorkorderHubBroker, WorkorderHubBroker>();

    // W11：RealtimeMetrics 也是 singleton，跟 broker 同一個生命週期。
    // 為什麼同時用兩個 service descriptor：MetricsController 直接需要實作型別來取 Snapshot()，
    // 但 workers（含 scoped）只透過 IRealtimeMetrics 抽象操作；我們把同一份 instance 同時註冊到兩個 token。
    builder.Services.AddSingleton<RealtimeMetricsService>();
    builder.Services.AddSingleton<IRealtimeMetrics>(sp => sp.GetRequiredService<RealtimeMetricsService>());

    // 為什麼 DomainProfileService 也是 singleton：
    // - profile JSON 啟動時讀一次，之後 read-only；singleton 對 IO 友好。
    builder.Services.AddSingleton<DomainProfileService>();

    // 為什麼 SpcRealtimeWorker 註冊成 singleton：
    // - Worker 內部維護 ConcurrentDictionary<string, SpcWindowState>，
    //   scoped 會每筆訊息重建一次、視窗永遠是 1 點，規則完全無法觸發。
    // - upstream 依賴都是 singleton（broker / profile / config / logger），同層註冊不會踩 scope 規則。
    builder.Services.AddSingleton<IKafkaMessageHandler, SpcRealtimeWorker>();

    // 為什麼 RabbitMQ workers 用 scoped：
    // - 它們依賴 AoiOpsDbContext（DbContext 必須 scoped）；
    // - RabbitMqConsumerHostedService 每筆訊息會 CreateScope() 解析 handler，
    //   保證 DbContext 在訊息生命週期內獨立、用完即釋放。
    builder.Services.AddScoped<IRabbitMessageHandler, AlarmRabbitWorker>();
    builder.Services.AddScoped<IRabbitMessageHandler, WorkorderRabbitWorker>();

    // 為什麼 hosted service 用 AddHostedService：
    // - .NET 會自動在啟動時 StartAsync、停止時 StopAsync，不用我們手動管 thread。
    builder.Services.AddHostedService<KafkaConsumerHostedService>();
    builder.Services.AddHostedService<RabbitMqConsumerHostedService>();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("DevCors", policy =>
        {
            // 為什麼 SignalR 同時要 AllowCredentials + 明確 origin：
            // - WebSocket 升級需要帶 cookie / token；AllowAnyOrigin + AllowCredentials 是禁止組合，
            //   開發階段固定列前端 dev server 與 docker 對外 port，未來部署再收緊。
            var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                          ?? new[] { "http://localhost:5173", "http://localhost:4173" };
            policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    var app = builder.Build();

    // W11：把進來的 HTTP request 加結構化欄位（method、path、status、elapsed）。
    // 為什麼加：CompactJsonFormatter + RequestLogging 可以一行 JSON 看完整一筆請求，方便 grep。
    app.UseSerilogRequestLogging();

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

    // 為什麼 hub 路徑放 /hubs 開頭：
    // - 與 /api/* 路徑明顯區分，nginx / gateway 之後可以針對 WebSocket 設不同 timeout / sticky。
    app.MapHub<SpcHub>("/hubs/spc");
    app.MapHub<AlarmHub>("/hubs/alarm");
    app.MapHub<WorkorderHub>("/hubs/workorder");

    // 為什麼加 lifecycle log：
    // - 目前症狀是「程式有跑、DB/Kafka worker 有 log，但 HTTP 沒有 listen」；
    // - 透過 ApplicationStarted/Stopping/Stopped 可以判斷 host 是否真的進入 run loop，
    //   或是在啟動階段就被某個 hosted service / server startup 卡住。
    app.Lifetime.ApplicationStarted.Register(() =>
        Log.Information("IHostApplicationLifetime.ApplicationStarted fired"));
    app.Lifetime.ApplicationStopping.Register(() =>
        Log.Information("IHostApplicationLifetime.ApplicationStopping fired"));
    app.Lifetime.ApplicationStopped.Register(() =>
        Log.Information("IHostApplicationLifetime.ApplicationStopped fired"));

    // 為什麼加這段啟動前 log：
    // - 目前現象是「container 內沒有任何 port 在 listen，但 background worker 似乎有在跑」；
    //   這代表 host startup 的某一段可能卡住或 Kestrel 沒有真正啟動。
    // - 用 app.Urls 把實際綁定的位址印出來，能快速判斷是「根本沒跑到 app.Run」還是「綁錯 port」。
    Log.Information("HTTP endpoints mapped. About to start Kestrel. Urls={Urls}", string.Join(",", app.Urls));

    // 為什麼改用 RunAsync：
    // - Program.cs 前面已使用 await（InitializeAsync），整個 entrypoint 是 async；
    // - 某些環境下同步的 app.Run()（內部用 GetResult 阻塞）可能導致 host 啟動行為異常，
    //   表現為「程式在跑但沒有任何 HTTP listen」；改用 await RunAsync 讓生命週期一致。
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "AOI Ops Platform backend terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
