// SpcMeasurementWriterService：背景 batch flush SPC 量測點到 spc_measurements。
//
// 為什麼放 Infrastructure：
// - 直接依賴 EF DbContext（屬於基礎設施），符合 Clean Architecture。
//
// 為什麼一個 class 同時實作 ISpcMeasurementSink + BackgroundService：
// - sink 與 flusher 共用同一個 Channel；一個物件兩個 hat 可避免做生命週期同步。
// - DI 註冊時用「同一個實例」覆蓋兩個 service descriptor 即可（見 Program.cs）。

using System.Threading.Channels;
using AOIOpsPlatform.Application.Spc;
using AOIOpsPlatform.Domain.Entities;
using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AOIOpsPlatform.Infrastructure.Workers;

/// <summary>
/// 把 SPC 即時量測點以 batch 方式寫入 <c>spc_measurements</c>。
/// </summary>
/// <remarks>
/// 為什麼用 Channel.BoundedChannel：
/// - 避免下游 DB 變慢時 Kafka 端無限堆積，造成 OOM；
///   超過上限時用 DropOldest 政策，丟舊保新對 SPC 來說損失可接受（即時性優先）。
///
/// 為什麼 in-memory cache code→id：
/// - 每次 enqueue 都查 DB 會把 SPC stream 速度拖到 DB；
/// - tools/parameters 表變動很慢，cache 一次就能用很久；
/// - panels 較多但仍有限，命中率也高，用 ConcurrentDictionary 分頁 cache 就夠。
/// </remarks>
public sealed class SpcMeasurementWriterService : BackgroundService, ISpcMeasurementSink
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SpcMeasurementWriterService> _logger;

    private readonly Channel<SpcMeasurementWriteRequest> _channel;

    private const int BatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);

    public SpcMeasurementWriterService(
        IServiceProvider services,
        ILogger<SpcMeasurementWriterService> logger)
    {
        _services = services;
        _logger = logger;
        // 為什麼上限 5000：開發機 SPC 訊息量約每秒幾十筆；5000 足夠 1-2 分鐘 buffer，
        // 即使 DB 短暫卡住也不會立刻丟資料。
        _channel = Channel.CreateBounded<SpcMeasurementWriteRequest>(
            new BoundedChannelOptions(5000)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    public void Enqueue(SpcMeasurementWriteRequest request)
    {
        // 為什麼用 TryWrite 不 await：
        // - 對 SPC 即時 stream 而言「丟掉一兩筆」遠勝過「卡住整個 worker」。
        // - 真要嚴格不丟可改 await WriteAsync，但需評估 back-pressure。
        if (!_channel.Writer.TryWrite(request))
        {
            _logger.LogWarning("SPC measurement channel full, dropped 1 record (line={Line} tool={Tool} param={Param})",
                request.LineCode, request.ToolCode, request.ParameterCode);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<SpcMeasurementWriteRequest>(BatchSize);
        var reader = _channel.Reader;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await reader.WaitToReadAsync(stoppingToken)) break;

                while (batch.Count < BatchSize && reader.TryRead(out var req))
                {
                    batch.Add(req);
                }

                if (batch.Count == 0)
                {
                    await Task.Delay(FlushInterval, stoppingToken);
                    continue;
                }

                await FlushBatchAsync(batch, stoppingToken);
                batch.Clear();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SPC measurement batch flush 失敗，丟棄 {Count} 筆繼續", batch.Count);
                batch.Clear();
                // 為什麼這裡不重試：丟棄當前 batch 換取整體穩定，避免單一壞資料卡住流。
                await Task.Delay(FlushInterval, stoppingToken);
            }
        }

        // 為什麼結束時要嘗試最後 flush：
        // - 容器收到 SIGTERM 時還在 channel 裡的訊息要儘量寫出，避免 demo 看不到「最後幾筆」。
        if (batch.Count > 0)
        {
            try
            {
                await FlushBatchAsync(batch, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Shutdown final flush failed for {Count} records", batch.Count);
            }
        }
    }

    /// <summary>
    /// 把一個 batch 寫入 spc_measurements；自動解析 code→id 並 cache。
    /// </summary>
    /// <remarks>
    /// 為什麼每次 flush 都 CreateScope：
    /// - DbContext 是 scoped；BackgroundService 自己是 singleton，不能直接持有 DbContext。
    /// - 每個 batch 一個 scope 是最直觀也最少 surprise 的寫法。
    /// </remarks>
    private async Task FlushBatchAsync(List<SpcMeasurementWriteRequest> batch, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AoiOpsDbContext>();

        // 預先把要查的 code 集合抽出，一次查 DB 即可。
        var toolCodes = batch.Select(x => x.ToolCode).Distinct().ToList();
        var parameterCodes = batch.Select(x => x.ParameterCode).Distinct().ToList();
        var panelNos = batch
            .Where(x => !string.IsNullOrWhiteSpace(x.PanelNo))
            .Select(x => x.PanelNo!)
            .Distinct()
            .ToList();

        var toolMap = await db.Tools.AsNoTracking()
            .Where(t => toolCodes.Contains(t.ToolCode))
            .Select(t => new { t.Id, t.ToolCode, t.LineCode })
            .ToDictionaryAsync(t => t.ToolCode, t => t, cancellationToken);
        var parameterMap = await db.Parameters.AsNoTracking()
            .Where(p => parameterCodes.Contains(p.ParameterCode))
            .Select(p => new { p.Id, p.ParameterCode })
            .ToDictionaryAsync(p => p.ParameterCode, p => p, cancellationToken);
        Dictionary<string, Guid> panelMap = panelNos.Count == 0
            ? new Dictionary<string, Guid>()
            : await db.Panels.AsNoTracking()
                .Where(p => panelNos.Contains(p.PanelNo))
                .Select(p => new { p.Id, p.PanelNo })
                .ToDictionaryAsync(p => p.PanelNo, p => p.Id, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var inserted = 0;

        foreach (var req in batch)
        {
            if (!toolMap.TryGetValue(req.ToolCode, out var toolEntry))
            {
                // 為什麼遇到未知 tool 直接略過：
                // - SPC stream 規格已先註冊機台，未知 tool 多半是 ingestion 早於 seed；
                //   略過比硬塞髒資料安全。
                continue;
            }
            if (!parameterMap.TryGetValue(req.ParameterCode, out var paramEntry))
            {
                continue;
            }
            panelMap.TryGetValue(req.PanelNo ?? string.Empty, out var panelId);

            db.SpcMeasurements.Add(new SpcMeasurement
            {
                Id = Guid.NewGuid(),
                PanelId = panelId == Guid.Empty ? null : panelId,
                ToolId = toolEntry.Id,
                ParameterId = paramEntry.Id,
                PanelNo = req.PanelNo,
                LotNo = req.LotNo,
                ToolCode = req.ToolCode,
                LineCode = string.IsNullOrEmpty(req.LineCode) ? toolEntry.LineCode ?? "UNKNOWN" : req.LineCode,
                StationCode = req.StationCode,
                ParameterCode = req.ParameterCode,
                OperatorCode = req.OperatorCode,
                OperatorName = req.OperatorName,
                Value = req.Value,
                MeasuredAt = req.MeasuredAt,
                IsViolation = req.IsViolation,
                ViolationCodes = req.ViolationCodes,
                KafkaEventId = req.KafkaEventId,
                CreatedAt = now,
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Flushed {Count} spc_measurements", inserted);
        }
    }
}
