// SpcRulesEngine：八大 Nelson Rules 判定 + Cpk + 控制限計算。
//
// 為什麼集中在這個 engine：
// - 規則邏輯有不少 magic number（連續 N 點、Sigma 倍數），分散會難維護。
// - 引擎本身無狀態（吃陣列吐結果），方便寫單元測試對齊 Python 版。
//
// 解決什麼問題：
// - 提供 SpcRealtimeWorker 一支 Evaluate 方法，丟視窗就回 SpcPointPayload，
//   把規則細節藏在實作裡。

namespace AOIOpsPlatform.Application.Spc;

/// <summary>
/// 用滑動視窗點 + USL/LSL/target 算 SPC 點與違規。
/// </summary>
/// <remarks>
/// 為什麼是 sealed static-like 容器：
/// - engine 沒有實例狀態（狀態在 SpcWindowState）；做成 static 比較像 utility。
/// - 但保留 instance 簽名是為了未來想注入 logger / metrics 時不用大改。
/// </remarks>
public static class SpcRulesEngine
{
    /// <summary>
    /// 用視窗 + 規格 / 目標算出 SPC 點與違規。
    /// </summary>
    /// <param name="window">視窗，至少要有 1 筆。</param>
    /// <param name="lineCode">產線代碼（會塞回 payload）。</param>
    /// <param name="toolCode">機台代碼。</param>
    /// <param name="parameterCode">SPC 參數代碼。</param>
    /// <param name="usl">規格上限。</param>
    /// <param name="lsl">規格下限。</param>
    /// <param name="target">目標值。</param>
    /// <param name="timestamp">這一點的時間戳。</param>
    /// <remarks>
    /// 為什麼把「上下限」一起回傳：
    /// - 前端管制圖需要 UCL/CL/LCL 才能畫線；
    ///   讓 worker 不用每次再算一遍，引擎一次給齊。
    /// </remarks>
    public static SpcPointPayload Evaluate(
        IReadOnlyList<double> window,
        string lineCode,
        string toolCode,
        string parameterCode,
        double usl,
        double lsl,
        double target,
        DateTimeOffset timestamp,
        int inspectedQty = 1,
        double? fixedCl = null,
        double? fixedSigma = null)
    {
        if (window.Count == 0) throw new ArgumentException("window 不能為空", nameof(window));

        var capability = ProcessCapability.Calculate(window, usl, lsl, target);
        // 為什麼 n=1 時要分支：
        // - ProcessCapability 在樣本數 &lt; 2 回 Empty，Mean=0，若仍當 cl 用會讓 UCL/LCL 縮在 0 附近、幾乎「每點都超過 ±3σ」（Rule1 全紅）；
        //   單點用該點當暫時 CL、σ 用規格寬度的保守比例估計，圖上線才合理。
        double cl;
        double sigma;
        var specSpan = Math.Max(usl - lsl, 1e-9);
        // 固定管制線（baseline）優先：
        // - 使用者要求「先收 20~25 點穩定件數 → 計算 X̄̄、σ → 後續水平固定」；
        // - fixedCl/fixedSigma 由 SpcWindowState 在收滿 baseline 時鎖定，後續 Evaluate 全程沿用。
        if (fixedCl.HasValue && fixedSigma.HasValue && fixedSigma.Value > 0 && !double.IsNaN(fixedSigma.Value))
        {
            cl = fixedCl.Value;
            sigma = fixedSigma.Value;
        }
        else if (window.Count < 2)
        {
            cl = window[0];
            sigma = Math.Max(specSpan / 30.0, 1e-5);
        }
        else if (capability.Sigma > 0 && !double.IsNaN(capability.Sigma))
        {
            cl = capability.Mean;
            sigma = capability.Sigma;
        }
        else
        {
            // σ 估不出（例如兩點同值）：避免用 0.0001 造成管制線太窄
            cl = capability.Mean;
            sigma = Math.Max(specSpan / 30.0, 1e-5);
        }
        var ucl = cl + 3 * sigma;
        var lcl = cl - 3 * sigma;

        var latest = window[^1];
        var violations = new List<SpcRuleViolation>();
        EvaluateRules(window, cl, sigma, usl, lsl, violations);

        // 為什麼把 USL/LSL 也一起記錄違規（規則 1 的延伸）：
        // - 即使尚未超出 ±3σ，也可能已超 USL/LSL（製程能力不足）；
        //   在違規清單顯示「超出規格」對品保最有感。
        if (latest > usl)
        {
            violations.Add(new SpcRuleViolation(
                RuleId: 0,
                RuleName: "OutOfSpec",
                Severity: "red",
                Description: $"量測值 {latest:0.###} 超出 USL {usl:0.###}"));
        }
        else if (latest < lsl)
        {
            violations.Add(new SpcRuleViolation(
                RuleId: 0,
                RuleName: "OutOfSpec",
                Severity: "red",
                Description: $"量測值 {latest:0.###} 低於 LSL {lsl:0.###}"));
        }

        var q = inspectedQty < 1 ? 1 : inspectedQty;
        // Cpk：固定管制線模式下，Cpk 也要跟著 baseline 的 mean/σ 算，才不會每點亂跳。
        double? cpk;
        if (sigma > 0 && !double.IsNaN(sigma))
        {
            var cpu = (usl - cl) / (3 * sigma);
            var cpl = (cl - lsl) / (3 * sigma);
            cpk = Math.Min(cpu, cpl);
        }
        else
        {
            cpk = capability.Cpk;
        }

        return new SpcPointPayload(
            LineCode: lineCode,
            ToolCode: toolCode,
            ParameterCode: parameterCode,
            Timestamp: timestamp,
            Value: latest,
            Mean: cl,
            Sigma: sigma,
            Ucl: ucl,
            Cl: cl,
            Lcl: lcl,
            Cpk: cpk,
            Violations: violations,
            InspectedQty: q);
    }

    /// <summary>
    /// 八大 Nelson rules 判定。
    /// </summary>
    /// <remarks>
    /// 為什麼把 8 條規則寫成獨立 if：
    /// - 每條規則邏輯不同，硬抽象成「規則表」反而失去可讀性；
    ///   保留 if-by-if 對應教科書最直觀，後續維護一看就懂。
    /// </remarks>
    private static void EvaluateRules(
        IReadOnlyList<double> values,
        double cl,
        double sigma,
        double usl,
        double lsl,
        List<SpcRuleViolation> violations)
    {
        var n = values.Count;
        var latest = values[^1];

        // Rule 1：任一點超出 ±3σ → 紅燈
        if (Math.Abs(latest - cl) > 3 * sigma)
        {
            violations.Add(new SpcRuleViolation(1, "Beyond3Sigma", "red",
                $"最新點 {latest:0.###} 超出 ±3σ 控制線"));
        }

        // Rule 2：連續 9 點在中心線同側 → 黃燈
        if (n >= 9 && AllSameSide(values, n - 9, n - 1, cl))
        {
            violations.Add(new SpcRuleViolation(2, "NinePointsSameSide", "yellow",
                "連續 9 點在中心線同側"));
        }

        // Rule 3：連續 6 點單調遞增或遞減 → 黃燈
        if (n >= 6 && IsMonotonic(values, n - 6, n - 1))
        {
            violations.Add(new SpcRuleViolation(3, "SixPointsMonotonic", "yellow",
                "連續 6 點單調遞增或遞減（趨勢）"));
        }

        // Rule 4：連續 14 點交替上下震盪 → 黃燈
        if (n >= 14 && IsAlternating(values, n - 14, n - 1, cl))
        {
            violations.Add(new SpcRuleViolation(4, "FourteenAlternating", "yellow",
                "連續 14 點交替上下震盪"));
        }

        // Rule 5：連續 3 點中有 2 點在同側 ±2σ 區外 → 紅燈
        if (n >= 3 && CountAtLeast(values, n - 3, n - 1, cl, 2 * sigma, sameSide: true) >= 2)
        {
            violations.Add(new SpcRuleViolation(5, "TwoOfThreeBeyond2Sigma", "red",
                "連續 3 點中有 2 點在同側 2σ 區外"));
        }

        // Rule 6：連續 5 點中有 4 點在同側 ±1σ 區外 → 紅燈
        if (n >= 5 && CountAtLeast(values, n - 5, n - 1, cl, sigma, sameSide: true) >= 4)
        {
            violations.Add(new SpcRuleViolation(6, "FourOfFiveBeyond1Sigma", "red",
                "連續 5 點中有 4 點在同側 1σ 區外"));
        }

        // Rule 7：連續 15 點都落在 ±1σ 區內 → 黃燈（製程過於集中，可能量測精度問題）
        if (n >= 15 && AllWithin(values, n - 15, n - 1, cl, sigma))
        {
            violations.Add(new SpcRuleViolation(7, "FifteenPointsHugCenter", "yellow",
                "連續 15 點都落在 ±1σ 區內（製程過度集中）"));
        }

        // Rule 8：連續 8 點都落在 ±1σ 區外 → 紅燈
        if (n >= 8 && AllOutside(values, n - 8, n - 1, cl, sigma))
        {
            violations.Add(new SpcRuleViolation(8, "EightPointsBeyond1Sigma", "red",
                "連續 8 點都落在 ±1σ 區外"));
        }

        _ = usl; _ = lsl; // 保留簽名以便未來規則用
    }

    private static bool AllSameSide(IReadOnlyList<double> values, int from, int to, double cl)
    {
        bool above = values[from] > cl;
        for (int i = from; i <= to; i++)
        {
            if ((values[i] > cl) != above) return false;
            if (values[i] == cl) return false; // 在中心線上不算同側
        }
        return true;
    }

    private static bool IsMonotonic(IReadOnlyList<double> values, int from, int to)
    {
        bool inc = true, dec = true;
        for (int i = from + 1; i <= to; i++)
        {
            if (values[i] <= values[i - 1]) inc = false;
            if (values[i] >= values[i - 1]) dec = false;
        }
        return inc || dec;
    }

    private static bool IsAlternating(IReadOnlyList<double> values, int from, int to, double cl)
    {
        // 為什麼用「相對於前一點」來判定交替：
        // - Nelson 原始定義為「up-down-up-down…」即相鄰差值正負交替。
        for (int i = from + 1; i <= to; i++)
        {
            var diff = values[i] - values[i - 1];
            var prevDiff = i >= from + 2 ? values[i - 1] - values[i - 2] : -diff;
            if (diff == 0) return false;
            if (Math.Sign(diff) == Math.Sign(prevDiff)) return false;
        }
        _ = cl;
        return true;
    }

    private static int CountAtLeast(
        IReadOnlyList<double> values, int from, int to,
        double cl, double threshold, bool sameSide)
    {
        int countAbove = 0, countBelow = 0;
        for (int i = from; i <= to; i++)
        {
            var d = values[i] - cl;
            if (d > threshold) countAbove++;
            else if (d < -threshold) countBelow++;
        }
        return sameSide ? Math.Max(countAbove, countBelow) : countAbove + countBelow;
    }

    private static bool AllWithin(IReadOnlyList<double> values, int from, int to, double cl, double sigma)
    {
        for (int i = from; i <= to; i++)
        {
            if (Math.Abs(values[i] - cl) >= sigma) return false;
        }
        return true;
    }

    private static bool AllOutside(IReadOnlyList<double> values, int from, int to, double cl, double sigma)
    {
        for (int i = from; i <= to; i++)
        {
            if (Math.Abs(values[i] - cl) <= sigma) return false;
        }
        return true;
    }
}
