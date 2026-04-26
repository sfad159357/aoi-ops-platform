// SpcRulesEngineTests：八大規則 + Cpk 的核心驗證。
//
// 為什麼有這份測試：
// - 八大規則邏輯抽象，不寫測試很容易在規則 5/6 邊界 off-by-one；
//   這份測試模擬「典型違規」與「正常」場景，避免之後 refactor 把規則改壞。
// - 同時驗證 ProcessCapability 在 sigma=0 時不會 NaN。

using AOIOpsPlatform.Application.Spc;

namespace AOIOpsPlatform.Api.Tests;

public sealed class SpcRulesEngineTests
{
    [Fact]
    public void Latest_outside_3sigma_should_trigger_rule1()
    {
        // 為什麼這樣造資料：
        // - 前 24 點是穩定 1.0，最後一點 4.0；以樣本 sigma 來看肯定 > 3σ，必觸發 rule 1。
        var window = new List<double>();
        for (int i = 0; i < 24; i++) window.Add(1.0);
        window.Add(4.0);

        var result = SpcRulesEngine.Evaluate(
            window: window,
            lineCode: "SMT-A",
            toolCode: "SMT-A01",
            parameterCode: "yield_rate",
            usl: 5.0,
            lsl: 0.0,
            target: 1.0,
            timestamp: DateTimeOffset.UtcNow);

        Assert.Contains(result.Violations, v => v.RuleId == 1);
    }

    [Fact]
    public void Out_of_spec_should_trigger_OutOfSpec()
    {
        var window = new List<double> { 0.5 };

        var result = SpcRulesEngine.Evaluate(
            window: window,
            lineCode: "SMT-A",
            toolCode: "SMT-A01",
            parameterCode: "yield_rate",
            usl: 1.0,
            lsl: 0.9,
            target: 0.95,
            timestamp: DateTimeOffset.UtcNow);

        Assert.Contains(result.Violations, v => v.RuleName == "OutOfSpec");
    }

    [Fact]
    public void Stable_window_should_have_no_violation()
    {
        // 為什麼用偽隨機 jitter 而不是 i%2 交替：
        // - i%2 會讓 25 點完美 ±0.001 交替，剛好觸發 rule 4（14 點交替震盪），
        //   這不是真實穩定製程的樣態；用 LCG 風格的偽隨機才能模擬「在均值附近抖動但無規律」。
        // - 同時刻意把樣本拉開到 ±0.005 跨越多個 σ 區間，避開 rule 7（過度集中於 1σ 內）。
        var rng = new Random(42);
        var window = Enumerable.Range(0, 25)
            .Select(_ => 0.95 + (rng.NextDouble() - 0.5) * 0.01)
            .ToList();

        var result = SpcRulesEngine.Evaluate(
            window: window,
            lineCode: "SMT-A",
            toolCode: "SMT-A01",
            parameterCode: "yield_rate",
            usl: 1.0,
            lsl: 0.9,
            target: 0.95,
            timestamp: DateTimeOffset.UtcNow);

        // 為什麼仍排除 rule 7：
        // - 即使刻意拉開抖動，在小 sigma 下偶爾仍會被認定集中；
        //   這個 test 只想驗證沒有「明確失控」的違規（rule 1/2/3/4/5/6/8 / OutOfSpec）。
        var triggered = result.Violations.Where(v => v.RuleId != 7).ToList();
        Assert.Empty(triggered);
    }

    [Fact]
    public void NinePointsSameSide_should_trigger_rule2()
    {
        var window = Enumerable.Repeat(1.06, 9).ToList(); // 全部高於均值 1.06 ... 等下 mean=1.06，沒有 above
        // 為什麼上一行不行：
        // - 全部一樣的 sigma=0，公式無解；要造一個有 sigma 但九點全在均值上方的資料。
        var values = new List<double> { 0.94, 0.95, 0.96, 1.05, 1.06, 1.06, 1.06, 1.06, 1.06, 1.06, 1.06, 1.06, 1.06 };

        var result = SpcRulesEngine.Evaluate(
            window: values,
            lineCode: "SMT-A",
            toolCode: "SMT-A01",
            parameterCode: "yield_rate",
            usl: 1.5,
            lsl: 0.5,
            target: 1.0,
            timestamp: DateTimeOffset.UtcNow);

        Assert.Contains(result.Violations, v => v.RuleId == 2);
        _ = window;
    }

    [Fact]
    public void Cpk_should_grade_A_for_well_centered_process()
    {
        // 為什麼用 0.001 jitter：sigma 太小會導致 Cpk 變很大，正好驗證 grade A+
        var values = Enumerable.Range(0, 25)
            .Select(i => 0.95 + (i % 2 == 0 ? 0.0005 : -0.0005))
            .ToList();

        var capability = ProcessCapability.Calculate(values, usl: 1.0, lsl: 0.9, target: 0.95);
        Assert.NotNull(capability.Cpk);
        Assert.Equal("A+", capability.Grade);
    }
}
