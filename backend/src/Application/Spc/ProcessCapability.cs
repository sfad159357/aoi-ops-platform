// ProcessCapability：Ca / Cp / Cpk 計算。
//
// 為什麼自己寫而不靠第三方：
// - 公式單純（平均、標準差、上下限），第三方依賴只會增加不必要 attack surface；
//   寫單元測試也可以對齊 Python 版輸出。
//
// 解決什麼問題：
// - 提供 SpcRulesEngine 與 controllers 一致的能力指標計算介面，
//   讓 dashboard KPI / Cpk 即時欄位有單一事實來源。

namespace AOIOpsPlatform.Application.Spc;

/// <summary>
/// 製程能力指標計算。
/// </summary>
public static class ProcessCapability
{
    /// <summary>
    /// 計算 Ca / Cp / Cpk 與等級。
    /// </summary>
    /// <param name="values">最近 N 筆量測值（建議 >=20 才有統計意義）。</param>
    /// <param name="usl">規格上限。</param>
    /// <param name="lsl">規格下限。</param>
    /// <param name="target">目標值（用來算 Ca）。</param>
    /// <remarks>
    /// 為什麼回傳 nullable double：
    /// - 樣本不足或 sigma=0（資料完全相同）時公式無解，回 null 比丟例外友善很多。
    /// - 等級用 string，方便前端直接顯示而不需要再 enum 對照。
    /// </remarks>
    public static ProcessCapabilityResult Calculate(
        IReadOnlyList<double> values,
        double usl,
        double lsl,
        double target)
    {
        if (values.Count < 2)
        {
            return ProcessCapabilityResult.Empty;
        }

        var mean = Mean(values);
        var sigma = StdDev(values, mean);
        if (sigma <= 0.0 || double.IsNaN(sigma))
        {
            return ProcessCapabilityResult.Empty with { Mean = mean, Sigma = sigma };
        }

        // 為什麼用 (USL-LSL)/2 當分母：
        // - 行業標準 Ca 公式：Ca = (X̄ - 目標) / ((USL-LSL)/2)
        //   反映製程平均偏離目標多少；公式定義固定，不要自己發明。
        var halfSpec = (usl - lsl) / 2.0;
        var ca = halfSpec == 0 ? (double?)null : (mean - target) / halfSpec;

        var cp = (usl - lsl) / (6.0 * sigma);
        var cpu = (usl - mean) / (3.0 * sigma);
        var cpl = (mean - lsl) / (3.0 * sigma);
        var cpk = Math.Min(cpu, cpl);

        return new ProcessCapabilityResult(
            Mean: mean,
            Sigma: sigma,
            Ca: ca,
            Cp: cp,
            Cpk: cpk,
            Grade: GradeFor(cpk));
    }

    /// <summary>
    /// Cpk 等級對照表（業界常用）。
    /// </summary>
    /// <remarks>
    /// 為什麼用 ≥ 而不是 ＞：
    /// - 邊界值（例如 1.33）通常算「達標」；用 ≥ 與多數品保部門報告一致。
    /// </remarks>
    public static string GradeFor(double cpk) => cpk switch
    {
        >= 1.67 => "A+",
        >= 1.33 => "A",
        >= 1.00 => "B",
        >= 0.67 => "C",
        _ => "D",
    };

    private static double Mean(IReadOnlyList<double> values)
    {
        double sum = 0;
        foreach (var v in values) sum += v;
        return sum / values.Count;
    }

    private static double StdDev(IReadOnlyList<double> values, double mean)
    {
        double s = 0;
        foreach (var v in values)
        {
            var d = v - mean;
            s += d * d;
        }
        // 為什麼用 n-1（樣本標準差）：
        // - SPC 為小樣本估計母體變異；n-1 是無偏估計，比 n 更貼近實際 sigma。
        return Math.Sqrt(s / (values.Count - 1));
    }
}

public sealed record ProcessCapabilityResult(
    double Mean,
    double Sigma,
    double? Ca,
    double? Cp,
    double? Cpk,
    string Grade)
{
    public static readonly ProcessCapabilityResult Empty =
        new(0, 0, null, null, null, "N/A");
}
