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
    public IReadOnlyList<double> Add(double value)
    {
        lock (_lock)
        {
            _values.Enqueue(value);
            while (_values.Count > _capacity) _values.Dequeue();
            return _values.ToArray();
        }
    }
}
