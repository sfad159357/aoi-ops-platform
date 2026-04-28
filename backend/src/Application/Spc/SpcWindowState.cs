// SpcWindowState：每 (line, tool, parameter) 一份的滑動視窗狀態。
//
// 為什麼用 ConcurrentDictionary 收狀態：
// - SpcRealtimeWorker 一次只處理一筆訊息（同一 partition 內串行），但同時可能有多個 partition 並行；
//   ConcurrentDictionary 保證新增 key 時不會撞、不需自己加鎖。
// - 每個 key 對應一個獨立 SpcWindowState，內部用單一 List + lock 保證 append / 計算原子。
//
// 解決什麼問題：
// - 把「最近 N 點」這個狀態從 worker 主流程拆出來，
//   未來要把狀態搬到 Redis 或重啟回放，介面對外不變。

namespace AOIOpsPlatform.Application.Spc;

/// <summary>
/// 一條 (line, tool, parameter) 的最近 N 點滑動視窗。
/// </summary>
/// <remarks>
/// 為什麼預設 25 點：
/// - SPC 八大規則（Nelson rules）大多在 8~15 點窗就足以判定；
///   25 點留一些 margin，同時讓 Cpk 計算 sample 數穩定。
/// </remarks>
public sealed class SpcWindowState
{
    private readonly object _lock = new();
    private readonly Queue<double> _values;
    private readonly int _capacity;
    private bool _hasBaseline;
    private double _baselineCl;
    private double _baselineSigma;

    public SpcWindowState(int capacity = 25)
    {
        _capacity = capacity;
        _values = new Queue<double>(capacity);
    }

    /// <summary>視窗最大尺寸。</summary>
    public int Capacity => _capacity;

    /// <summary>
    /// 加入一筆值，超過容量時丟掉最舊的；回傳當下完整視窗副本。
    /// </summary>
    /// <remarks>
    /// 為什麼回傳副本：
    /// - 規則引擎接 array 計算時希望快照不變；
    ///   給內部 queue reference 會被下個 append 改掉，造成計算結果不穩。
    /// </remarks>
    public SpcWindowSnapshot Add(double value)
    {
        lock (_lock)
        {
            _values.Enqueue(value);
            while (_values.Count > _capacity) _values.Dequeue();

            // Baseline（固定管制線）策略：
            // - 先收滿 capacity（預設 25）個點，視為「穩定期」；
            // - 只在第一次收滿時算一次 CL/σ，之後固定水平管制線（教科書 SPC 標準做法）。
            //
            // 為什麼不每點重算：
            // - 滑動視窗每點重算會讓 UCL/LCL 變成曲線，偏向監控儀表板而非傳統管制圖；
            // - 使用者要求參照「先收 20~25 點再固定」的流程，因此這裡把 baseline 鎖定起來。
            if (!_hasBaseline && _values.Count == _capacity)
            {
                var arr = _values.ToArray();
                _baselineCl = Mean(arr);
                _baselineSigma = SampleStdDev(arr, _baselineCl);
                _hasBaseline = true;
            }

            var window = _values.ToArray();
            return new SpcWindowSnapshot(
                Window: window,
                Baseline: _hasBaseline ? new SpcBaseline(_baselineCl, _baselineSigma, _capacity) : null);
        }
    }

    private static double Mean(IReadOnlyList<double> values)
    {
        double s = 0;
        for (var i = 0; i < values.Count; i++) s += values[i];
        return s / Math.Max(1, values.Count);
    }

    private static double SampleStdDev(IReadOnlyList<double> values, double mean)
    {
        // 為什麼用樣本標準差（n-1）：
        // - SPC 常用樣本估計 σ；若 n 太小或全同值，σ 會趨近 0，需要下游做保守處理。
        var n = values.Count;
        if (n < 2) return 0;
        double ss = 0;
        for (var i = 0; i < n; i++)
        {
            var d = values[i] - mean;
            ss += d * d;
        }
        return Math.Sqrt(ss / (n - 1));
    }
}

/// <summary>視窗快照（含 baseline 固定管制線資訊）。</summary>
public sealed record SpcWindowSnapshot(IReadOnlyList<double> Window, SpcBaseline? Baseline);

/// <summary>Baseline：固定水平管制線用的 CL/σ。</summary>
public sealed record SpcBaseline(double Cl, double Sigma, int N);
