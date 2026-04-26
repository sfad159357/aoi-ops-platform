// KafkaConsumerHostedService：把 Kafka 消費迴圈包進 .NET Hosted Service。
//
// 為什麼用 BackgroundService：
// - 這是 .NET 官方推薦做法，跟著 ASP.NET Core 一起啟動 / 優雅關閉，不需要額外進程管理；
//   docker-compose 起 backend container 同時就把 consumer 帶起來，運維簡單。
// - 可以注入 IServiceProvider，每筆訊息開一個 scope（避免 DbContext 跨訊息殘留狀態）。
//
// 解決什麼問題：
// - 把 consumer 生命週期、handler 路由、錯誤回滾、優雅停機集中在一個地方，
//   不同 topic 的處理邏輯放到 IKafkaMessageHandler 各實作中。

using AOIOpsPlatform.Application.Messaging;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AOIOpsPlatform.Infrastructure.Messaging;

/// <summary>
/// 從 Kafka 拉訊息並分派到 IKafkaMessageHandler 的 hosted service。
/// </summary>
/// <remarks>
/// 為什麼一個 hosted service 跑多個 handler：
/// - Confluent.Kafka 的 consumer 不是 thread-safe；同一個 consumer 物件不能被多個 worker 共用。
/// - 但 ASP.NET Core 也不希望我們每個 handler 都宣告一個獨立的 BackgroundService（粒度太細）。
/// - 折衷做法：一個 hosted service 維護一個 consumer，subscribe 多個 topic，
///   收到後依 topic 分派給對應 handler，讓 handler 執行緒短暫並行不影響 consumer 主迴圈。
/// </remarks>
public sealed class KafkaConsumerHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaConsumerHostedService> _logger;
    private readonly IReadOnlyList<IKafkaMessageHandler> _handlers;

    public KafkaConsumerHostedService(
        IServiceProvider serviceProvider,
        IOptions<KafkaOptions> options,
        IEnumerable<IKafkaMessageHandler> handlers,
        ILogger<KafkaConsumerHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
        // 為什麼用 ToList：固定處理器清單後續用 dictionary 路由，避免每次都跑 LINQ 重建。
        _handlers = handlers.ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_handlers.Count == 0)
        {
            // 為什麼直接結束：沒有 handler 註冊代表這個服務暫時用不到 Kafka，
            // 不要白白卡個閒置 consumer 占 group 名額（Kafka rebalance 會變慢）。
            _logger.LogInformation("KafkaConsumerHostedService 沒有任何 handler，跳過啟動。");
            return;
        }

        var topics = _handlers.Select(h => h.Topic).Distinct().ToList();
        var handlerByTopic = _handlers
            .GroupBy(h => h.Topic)
            .ToDictionary(g => g.Key, g => g.ToList());

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            // 為什麼用 SpcRealtime group：W04 主訴求是把 SPC realtime worker 跑通；
            // 之後 W07 加 RabbitMQ defect handler 時會另起 host service，不會用同一個 group。
            GroupId = _options.GroupIdSpcRealtime,
            AutoOffsetReset = ParseOffsetReset(_options.AutoOffsetReset),
            EnableAutoCommit = true,
            // 為什麼 Earliest 改成 latest（透過 options）：
            // - demo 重啟時不要把幾千筆歷史點重播給前端，否則 SignalR 會把 UI 灌爆。
            EnableAutoOffsetStore = true,
            AllowAutoCreateTopics = false,
        };

        // 為什麼包 try / while loop：
        // - Kafka broker 可能比 backend 晚啟動，subscribe 一開始會丟 KafkaException；
        //   用迴圈 + 短暫等待，比 docker-compose depends_on 更可靠。
        IConsumer<string, string>? consumer = null;
        while (!stoppingToken.IsCancellationRequested && consumer is null)
        {
            try
            {
                consumer = new ConsumerBuilder<string, string>(config)
                    .SetErrorHandler((_, e) => _logger.LogWarning("Kafka error: {Reason}", e.Reason))
                    .Build();
                consumer.Subscribe(topics);
                _logger.LogInformation(
                    "Kafka consumer 已訂閱 topics: {Topics}（bootstrap={Bootstrap}, group={Group})",
                    string.Join(",", topics), _options.BootstrapServers, _options.GroupIdSpcRealtime);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kafka consumer 建立失敗，5 秒後重試…");
                consumer?.Dispose();
                consumer = null;
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        if (consumer is null) return;

        try
        {
            // 為什麼要先 Yield 一次再進入 consume loop：
            // - BackgroundService.StartAsync 會在 Host 啟動流程中呼叫 ExecuteAsync；
            // - 如果 ExecuteAsync 在進入 while loop 前沒有任何 await，
            //   那麼它會「同步」占住 Host 啟動執行緒，造成 Kestrel 永遠無法開始 listen，
            //   表現為：DB/Kafka worker 有 log，但 API 連線永遠 reset/refused、healthcheck 一直失敗。
            // - `await Task.Yield()` 可確保啟動流程能先完成（Kestrel 先起來），consume loop 再在 thread pool 上持續跑。
            await Task.Yield();

            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    // 為什麼用 short timeout：
                    // - 讓 stoppingToken 取消時能及時跳出；長 timeout 會讓 graceful shutdown 卡好幾秒。
                    result = consumer.Consume(TimeSpan.FromSeconds(1));
                    if (result is null) continue;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogWarning(ex, "Kafka consume 例外（topic={Topic})", ex.ConsumerRecord?.Topic);
                    continue;
                }

                if (!handlerByTopic.TryGetValue(result.Topic, out var handlers))
                {
                    continue;
                }

                // 為什麼每筆訊息都開 scope：
                // - handler 內可能注入 DbContext / SignalR HubContext 等 scoped 服務，
                //   不開 scope 會踩到「DbContext 跨多執行緒」的雷。
                using var scope = _serviceProvider.CreateScope();
                foreach (var handler in handlers)
                {
                    try
                    {
                        await handler.HandleAsync(result.Message.Key, result.Message.Value, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        // 為什麼吞例外、繼續消費：
                        // - 一筆爛資料不應該讓整條 consumer 停下；handler 內部可自己決定要寫死信佇列還是放棄。
                        _logger.LogError(ex, "Handler {Type} 處理 {Topic} 訊息失敗", handler.GetType().Name, result.Topic);
                    }
                }
            }
        }
        finally
        {
            try { consumer.Close(); } catch { /* ignore on shutdown */ }
            consumer.Dispose();
        }
    }

    private static AutoOffsetReset ParseOffsetReset(string value) => value.ToLowerInvariant() switch
    {
        "earliest" => Confluent.Kafka.AutoOffsetReset.Earliest,
        "latest" => Confluent.Kafka.AutoOffsetReset.Latest,
        _ => Confluent.Kafka.AutoOffsetReset.Latest,
    };
}
