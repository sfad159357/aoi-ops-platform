// SpcRulesEngineTests：Nelson 8 大規則（+OutOfSpec）單元測試。
//
// 為什麼要用「串流」方式測：
// - 真實系統是 Kafka 一點一點來；規則判定應該在「最後一點進來」時被觸發。
// - 如果只丟最終 window，不容易看出 off-by-one（例如 8 點/9 點邊界）是否正確。
//
// 為什麼固定 cl/sigma：
// - Nelson rules 的門檻是以 cl 與 σ 為基準；
//   若讓 cl/sigma 隨 window 自己算，造資料時容易被「平均值漂移」或「sigma 變小」影響到門檻。
// - 這裡在 Evaluate 固定 fixedCl=0、fixedSigma=1，讓每條規則的假資料可精準對齊門檻。

using AOIOpsPlatform.Application.Spc;
using System.Linq;


namespace AOIOpsPlatform.Api.Tests;

public sealed class SpcRulesEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 04, 28, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 逐點餵入 stream，回傳最後一點的 Evaluate 結果。
    /// </summary>
    /// <remarks>
    /// 解決什麼問題：
    /// - 把「串流假數據」的建構集中，所有測試都用同一條路徑，避免各測各的導致不一致。
    /// </remarks>
    private static SpcPointPayload EvaluateLast(IReadOnlyList<double> stream, double usl = 999, double lsl = -999)
    {
        var window = new List<double>();
        SpcPointPayload? last = null;
        for (var i = 0; i < stream.Count; i++)
        {
            window.Add(stream[i]);
            last = SpcRulesEngine.Evaluate(
                window: window,
                lineCode: "SMT-A",
                toolCode: "AOI-A01",
                parameterCode: "yield_rate",
                usl: usl,
                lsl: lsl,
                target: 0,
                timestamp: T0.AddSeconds(i),
                fixedCl: 0,
                fixedSigma: 1);
        }
        return last!;
    }

    private static void AssertTriggered(SpcPointPayload payload, int ruleId)
        => Assert.Contains(payload.Violations, v => v.RuleId == ruleId);

    [Fact]
    
    public void Rule1_any_point_beyond_3sigma_should_trigger()
    {
        // 造資料：最後一點 = 3.2σ（>3σ） → 觸發 rule 1
        var payload = EvaluateLast(new[] { 0, 0, 0, 0, 3.2 });
        AssertTriggered(payload, 1);
    }

    [Fact]
    public void Rule2_nine_points_same_side_should_trigger()
    {
        // 造資料：連續 9 點全在中心線上方（不能等於 cl）
        var stream = Enumerable.Repeat(0.2, 9).ToArray();
        var payload = EvaluateLast(stream);
        AssertTriggered(payload, 2);
    }

    [Fact]
    public void Rule3_six_points_monotonic_should_trigger()
    {
        // 造資料：最後 6 點單調遞增
        var payload = EvaluateLast(new[] { -0.1, 0.0, 0.1, 0.2, 0.3, 0.4 });
        AssertTriggered(payload, 3);
    }

    [Fact]
    public void Rule4_fourteen_points_alternating_should_trigger()
    {
        // 造資料：相鄰差值正負交替（0.2, -0.2, 0.2, -0.2,...）
        var stream = new List<double>();
        for (var i = 0; i < 14; i++)
        {
            stream.Add(i % 2 == 0 ? 0.2 : -0.2);
        }
        var payload = EvaluateLast(stream);
        AssertTriggered(payload, 4);
    }

    [Fact]
    public void Rule5_two_of_three_beyond_2sigma_same_side_should_trigger()
    {
        // 造資料：最近 3 點中有 2 點都在同側 2σ 區外（>2）
        // 例：2.2、0.1、2.3 → countAbove=2
        var payload = EvaluateLast(new[] { 2.2, 0.1, 2.3 });
        AssertTriggered(payload, 5);
    }

    [Fact]
    public void Rule6_four_of_five_beyond_1sigma_same_side_should_trigger()
    {
        // 造資料：最近 5 點中有 4 點都在同側 1σ 區外（>1）
        // 例：1.2、1.3、0.2、1.4、1.1 → countAbove=4
        var payload = EvaluateLast(new[] { 1.2, 1.3, 0.2, 1.4, 1.1 });
        AssertTriggered(payload, 6);
    }

    [Fact]
    public void Rule7_fifteen_points_within_1sigma_should_trigger()
    {
        // 造資料：連續 15 點都落在 ±1σ 內（注意 engine 用的是 <1σ，不含等於 1σ）
        var stream = Enumerable.Range(0, 15).Select(i => i % 2 == 0 ? 0.6 : -0.6).ToArray();
        var payload = EvaluateLast(stream);
        AssertTriggered(payload, 7);
    }

    [Fact]
    public void Rule8_eight_points_outside_1sigma_should_trigger()
    {
        // 造資料：連續 8 點都落在 ±1σ 外（注意 engine 用的是 >1σ，不含等於 1σ）
        var stream = Enumerable.Range(0, 8).Select(i => i % 2 == 0 ? 1.2 : -1.2).ToArray();
        var payload = EvaluateLast(stream);
        AssertTriggered(payload, 8);
    }

    [Fact]
    public void OutOfSpec_should_trigger_ruleId0()
    {
        // 造資料：最後一點超出 USL
        var payload = EvaluateLast(new[] { 0.0, 0.0, 5.0 }, usl: 1.0, lsl: -1.0);
        Assert.Contains(payload.Violations, v => v.RuleId == 0 && v.RuleName == "OutOfSpec");
    }

    [Fact]
    public void Normal_stream_should_not_trigger_any_nelson_rule()
    {
        // 為什麼這樣造資料：
        // - 用小幅度、無固定規律的抖動，避免觸發 monotonic / alternating / hug-center 等模式規則。
        // - 幅度刻意讓它有時落在 1σ 內、有時落在 1σ 外，避免 rule7/rule8 連續成立。
        var rng = new Random(7);
        var stream = Enumerable.Range(0, 40)
            .Select(_ => (rng.NextDouble() - 0.5) * 2.2) // 約落在 [-1.1, 1.1]
            .ToList();

        var payload = EvaluateLast(stream);
        Assert.DoesNotContain(payload.Violations, v => v.RuleId is >= 1 and <= 8);
    }
}
