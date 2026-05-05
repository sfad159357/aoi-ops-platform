// SpcWindowStateTests：驗證「前 20~25 點建立 baseline，後續即時點沿用固定 CL/σ」。
//
// 為什麼補這組測試：
// - 使用者要求 SPC 走固定基準線模式；若 baseline 被後續點改寫，管制線就會漂移，失去教科書 SPC 意義。
//
// 解決什麼問題：
// - 把 warm-up（20~25 點）與實際 ingestion（第 26 點以後）的銜接行為鎖進單元測試，避免未來回歸。

using AOIOpsPlatform.Application.Spc;

namespace AOIOpsPlatform.Api.Tests;

public sealed class SpcWindowStateTests
{
    [Fact]
    public void Add_should_create_baseline_once_reaching_baseline_sample_size()
    {
        // 為什麼 capacity=25 / baseline=20：
        // - 對齊 MES 實務常見「先收 20~25 點」規範；這裡驗證第 20 點時 baseline 會被建立。
        var state = new SpcWindowState(capacity: 25, baselineSampleSize: 20);

        SpcWindowSnapshot? snapshot = null;
        for (var i = 0; i < 19; i++)
        {
            snapshot = state.Add(0.90 + i * 0.001);
        }

        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.Baseline);

        snapshot = state.Add(0.919);
        Assert.NotNull(snapshot.Baseline);
        Assert.Equal(20, snapshot.Baseline!.N);
    }

    [Fact]
    public void Add_should_keep_baseline_fixed_after_warmup_even_when_new_points_arrive()
    {
        // 先用相對平穩的 20 點建立 baseline。
        var state = new SpcWindowState(capacity: 25, baselineSampleSize: 20);
        SpcWindowSnapshot snapshot = default!;
        for (var i = 0; i < 20; i++)
        {
            snapshot = state.Add(1.00 + (i % 2 == 0 ? 0.01 : -0.01));
        }

        Assert.NotNull(snapshot.Baseline);
        var baselineCl = snapshot.Baseline!.Cl;
        var baselineSigma = snapshot.Baseline.Sigma;

        // 模擬後續即時 ingestion 進來（第 21~35 點），故意放入偏離值。
        // 驗證：baseline 不應被重算，CL/σ 要維持第一次鎖定值。
        for (var i = 0; i < 15; i++)
        {
            snapshot = state.Add(1.20 + i * 0.02);
        }

        Assert.NotNull(snapshot.Baseline);
        Assert.Equal(baselineCl, snapshot.Baseline!.Cl, precision: 10);
        Assert.Equal(baselineSigma, snapshot.Baseline.Sigma, precision: 10);
    }
}
