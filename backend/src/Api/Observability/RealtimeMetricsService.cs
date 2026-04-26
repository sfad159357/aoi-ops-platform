// RealtimeMetricsService：簡易 in-memory metrics 收集器。
//
// 為什麼自己實作而不直接導入 prometheus-net：
// - W11 的目標是「能驗證 SignalR push 延遲、違規事件數」，不是建完整觀測棧；
//   in-memory + 一支 GET /api/metrics 已經能滿足 demo 與 acceptance check。
// - 未來要換成 prometheus-net 也只需要替換這個 IRealtimeMetrics 實作；workers 完全不必動。
//
// 解決什麼問題：
// - 給「Kafka push → 規則計算 → SignalR push」整條 pipeline 做一個 cheap 的數字儀表板，
//   讓 demo 時可以一眼看到「目前每秒推幾點、平均延遲多少 ms、累計違規幾次」。

using System.Collections.Concurrent;
using AOIOpsPlatform.Application.Observability;

namespace AOIOpsPlatform.Api.Observability;

/// <summary>
/// 即時觀測指標的記憶體實作（singleton）。
/// </summary>
public sealed class RealtimeMetricsService : IRealtimeMetrics
{
    private readonly ConcurrentQueue<(DateTimeOffset At, double LatencyMs)> _spcLatencies = new();
    private readonly ConcurrentQueue<(DateTimeOffset At, double LatencyMs)> _alarmLatencies = new();
    private readonly ConcurrentQueue<(DateTimeOffset At, double LatencyMs)> _workorderLatencies = new();

    private long _spcPointsTotal;
    private long _spcViolationsTotal;
    private long _alarmsTotal;
    private long _workordersTotal;

    /// <summary>
    /// 為什麼只保留 1024 筆：
    /// - 純粹避免 memory leak；對 demo 而言 1024 筆 SignalR 推播樣本已足以算 P95 / mean。
    /// </summary>
    private const int MaxSamples = 1024;

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public void RecordSpcPoint(bool hadViolation, double latencyMs)
    {
        Interlocked.Increment(ref _spcPointsTotal);
        if (hadViolation) Interlocked.Increment(ref _spcViolationsTotal);
        Enqueue(_spcLatencies, latencyMs);
    }

    public void RecordAlarm(double latencyMs)
    {
        Interlocked.Increment(ref _alarmsTotal);
        Enqueue(_alarmLatencies, latencyMs);
    }

    public void RecordWorkorder(double latencyMs)
    {
        Interlocked.Increment(ref _workordersTotal);
        Enqueue(_workorderLatencies, latencyMs);
    }

    /// <summary>
    /// 取目前快照。Controller 會把它序列化成 JSON 回傳。
    /// </summary>
    public RealtimeMetricsSnapshot Snapshot()
    {
        return new RealtimeMetricsSnapshot(
            StartedAt: StartedAt,
            UptimeSeconds: (DateTimeOffset.UtcNow - StartedAt).TotalSeconds,
            Spc: BuildGroup(_spcPointsTotal, _spcViolationsTotal, _spcLatencies),
            Alarm: BuildGroup(_alarmsTotal, null, _alarmLatencies),
            Workorder: BuildGroup(_workordersTotal, null, _workorderLatencies));
    }

    private static void Enqueue(ConcurrentQueue<(DateTimeOffset At, double LatencyMs)> q, double latencyMs)
    {
        q.Enqueue((DateTimeOffset.UtcNow, latencyMs));
        // 為什麼 while 而非 if：執行緒競爭時可能多筆同時 enqueue，要把超過上限的舊樣本一次清完。
        while (q.Count > MaxSamples && q.TryDequeue(out _)) { }
    }

    private static RealtimeMetricsGroup BuildGroup(
        long total,
        long? violations,
        ConcurrentQueue<(DateTimeOffset At, double LatencyMs)> q)
    {
        var samples = q.ToArray();
        if (samples.Length == 0)
        {
            return new RealtimeMetricsGroup(total, violations, 0, 0, 0, 0);
        }

        var values = samples.Select(s => s.LatencyMs).OrderBy(v => v).ToArray();
        var mean = values.Average();
        var p50 = Percentile(values, 0.50);
        var p95 = Percentile(values, 0.95);

        // 為什麼用最近 60 秒當 throughput 估計：避免 demo 時把整段啟動空檔平均下去。
        var now = DateTimeOffset.UtcNow;
        var lastMin = samples.Count(s => (now - s.At).TotalSeconds <= 60);

        return new RealtimeMetricsGroup(total, violations, mean, p50, p95, lastMin);
    }

    private static double Percentile(double[] sortedAsc, double p)
    {
        if (sortedAsc.Length == 0) return 0;
        var idx = (int)Math.Min(sortedAsc.Length - 1, Math.Floor(p * sortedAsc.Length));
        return sortedAsc[idx];
    }
}

/// <summary>整體 metrics 回傳結構。</summary>
public sealed record RealtimeMetricsSnapshot(
    DateTimeOffset StartedAt,
    double UptimeSeconds,
    RealtimeMetricsGroup Spc,
    RealtimeMetricsGroup Alarm,
    RealtimeMetricsGroup Workorder);

/// <summary>
/// 個別 hub 的 metrics 群組。
/// </summary>
/// <param name="Total">啟動以來的累計事件數。</param>
/// <param name="Violations">SPC 專用：累計違規次數，其他 hub 為 null。</param>
/// <param name="MeanLatencyMs">最近 1024 筆樣本平均推播延遲。</param>
/// <param name="P50LatencyMs">最近 1024 筆樣本 P50。</param>
/// <param name="P95LatencyMs">最近 1024 筆樣本 P95。</param>
/// <param name="EventsLast60s">最近 60 秒事件數。</param>
public sealed record RealtimeMetricsGroup(
    long Total,
    long? Violations,
    double MeanLatencyMs,
    double P50LatencyMs,
    double P95LatencyMs,
    int EventsLast60s);
