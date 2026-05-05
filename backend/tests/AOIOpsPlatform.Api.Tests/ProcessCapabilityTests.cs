// ProcessCapabilityTests：驗證 Cpk 由後端單一計算來源輸出且具刻度一致性。
//
// 為什麼補這組測試：
// - 專案已決定前端不再重算 Cpk，若後端輸出口徑漂移，儀表板就沒有補救空間。
// - 這裡用「同一批資料等比例縮放」驗證 Cpk 不變，確保後端公式在不同顯示刻度下仍一致。
//
// 解決什麼問題：
// - 避免再回到「前端 workaround 修 Cpk」的雙重計算模式，維持單一真相來源。

using AOIOpsPlatform.Application.Spc;

namespace AOIOpsPlatform.Api.Tests;

public sealed class ProcessCapabilityTests
{
    [Fact]
    public void Calculate_should_keep_cpk_consistent_when_values_and_spec_are_scaled_equally()
    {
        // 為什麼用兩組刻度資料（0~1 與 0~100）：
        // - 這是歷史上最容易造成 Cpk 誤判的情境；若縮放前後 Cpk 不同，就代表公式口徑有問題。
        var ratioValues = new[] { 0.94, 0.95, 0.96, 0.97, 0.955, 0.965, 0.958, 0.962 };
        var percentValues = ratioValues.Select(v => v * 100.0).ToArray();

        var ratioResult = ProcessCapability.Calculate(
            values: ratioValues,
            usl: 1.0,
            lsl: 0.0,
            target: 0.95);

        var percentResult = ProcessCapability.Calculate(
            values: percentValues,
            usl: 100.0,
            lsl: 0.0,
            target: 95.0);

        Assert.NotNull(ratioResult.Cpk);
        Assert.NotNull(percentResult.Cpk);

        // 為什麼容忍極小誤差：
        // - 浮點運算存在二進位表示誤差，這裡只驗證統計意義上的一致口徑。
        Assert.Equal(ratioResult.Cpk!.Value, percentResult.Cpk!.Value, precision: 10);
    }
}
