// RabbitMqConsumerHostedService：把 RabbitMQ 消費包進 .NET Hosted Service。
//
// 為什麼跟 Kafka 分開：
// - 兩種 broker 的 client SDK API 完全不一樣，一個 hosted service 同時管會非常難讀；
//   分開後各自有重連 / ack / qos 設定，maintainer 不會混淆。
// - 業務語意也不同：Kafka 失敗就 skip 沒關係，RabbitMQ 異常 / 工單失敗要 nack 重排。
//
// 解決什麼問題：
// - 提供 RabbitMQ 通用的 consumer 樣板，後續 alert / workorder handler 只要實作介面即可。

using System.Text;
using AOIOpsPlatform.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace AOIOpsPlatform.Infrastructure.Messaging;

/// <summary>
/// 訂閱多個 RabbitMQ queue 並把 message 派發給對應 handler 的 hosted service。
/// </summary>
/// <remarks>
/// 為什麼用 manual ack：
/// - 異常 / 工單事件處理失敗一定要重排或進 dead letter，
///   AutoAck = true 會在 broker 端就刪掉訊息，handler 再爛我們也救不回來。
/// </remarks>
public sealed class RabbitMqConsumerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConsumerHostedService> _logger;

    public RabbitMqConsumerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConsumerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 為什麼一開始開個臨時 scope 取 queue 列表：
        // - handler 是 scoped 服務，不能在 hosted service 構造階段直接 inject；
        //   先用 scope 拿一份 queue 名單做 subscribe，後續每筆訊息再開新 scope 處理。
        IReadOnlyList<string> queues;
        using (var scope = _scopeFactory.CreateScope())
        {
            queues = scope.ServiceProvider
                .GetServices<IRabbitMessageHandler>()
                .Select(h => h.Queue)
                .Distinct()
                .ToList();
        }

        if (queues.Count == 0)
        {
            _logger.LogInformation("RabbitMqConsumerHostedService 沒有 handler，跳過啟動。");
            return;
        }

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            // 為什麼啟用自動恢復：
            // - RabbitMQ 重啟或網路抖動時，client 會自動重建 channel + 重新訂閱，
            //   不需要我們在 service 內自己 retry。
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
        };

        IConnection? connection = null;
        IModel? channel = null;
        while (!stoppingToken.IsCancellationRequested && connection is null)
        {
            try
            {
                connection = factory.CreateConnection("aoiops-backend");
                channel = connection.CreateModel();
                channel.BasicQos(prefetchSize: 0, prefetchCount: 16, global: false);

                foreach (var queueName in queues)
                {
                    // 為什麼 declare queue（durable, non-exclusive）：
                    // - Python publisher 端建立的 queue 也要 durable，雙方一致才能 bind 成功；
                    //   queue 已存在時 declare 是 idempotent，不會出錯。
                    channel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false);

                    var localQueue = queueName;
                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (_, ea) =>
                    {
                        // 為什麼把整段邏輯丟到 background task：
                        // - EventingBasicConsumer.Received 是同步事件，
                        //   把 await 結果同步等就會卡 RabbitMQ I/O thread。
                        var deliveryTag = ea.DeliveryTag;
                        var body = Encoding.UTF8.GetString(ea.Body.ToArray());
                        _ = Task.Run(async () =>
                        {
                            bool ok;
                            try
                            {
                                using var msgScope = _scopeFactory.CreateScope();
                                var handler = msgScope.ServiceProvider
                                    .GetServices<IRabbitMessageHandler>()
                                    .FirstOrDefault(h => h.Queue == localQueue);

                                if (handler is null)
                                {
                                    _logger.LogWarning("找不到 handler for queue {Queue}", localQueue);
                                    ok = true; // ack 掉避免 redelivery 風暴
                                }
                                else
                                {
                                    ok = await handler.HandleAsync(body, stoppingToken);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "RabbitMQ handler 處理 queue {Queue} 失敗", localQueue);
                                ok = false;
                            }

                            try
                            {
                                if (ok)
                                {
                                    channel!.BasicAck(deliveryTag, multiple: false);
                                }
                                else
                                {
                                    // 為什麼 requeue = false：
                                    // - 處理失敗的訊息直接重排會無限循環；先 drop（W11 加 DLX）。
                                    channel!.BasicNack(deliveryTag, multiple: false, requeue: false);
                                }
                            }
                            catch (Exception ackEx)
                            {
                                _logger.LogWarning(ackEx, "RabbitMQ ack/nack 失敗（可能 channel 已關閉）");
                            }
                        }, stoppingToken);
                    };

                    channel.BasicConsume(queueName, autoAck: false, consumer);
                    _logger.LogInformation("RabbitMQ 已訂閱 queue {Queue}", queueName);
                }
            }
            catch (BrokerUnreachableException ex)
            {
                _logger.LogWarning(ex, "RabbitMQ broker 連線失敗，5 秒後重試…");
                channel?.Dispose();
                connection?.Dispose();
                channel = null;
                connection = null;
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            // 為什麼吞掉：cancellation 是預期的優雅關閉訊號，不該變成錯誤。
        }
        finally
        {
            try { channel?.Close(); } catch { }
            try { connection?.Close(); } catch { }
            channel?.Dispose();
            connection?.Dispose();
        }
    }
}
