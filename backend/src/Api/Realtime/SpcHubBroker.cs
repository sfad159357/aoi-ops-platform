// SpcHubBroker / AlarmHubBroker / WorkorderHubBroker：
// Application ISpcHubBroker 等介面的具體 SignalR 實作。
//
// 為什麼放在 Api 層：
// - 只有 Api 層才會引用 Microsoft.AspNetCore.SignalR，broker 自然落在這裡。
// - Application 透過 DI 拿到的還是介面，依賴方向乾淨。
//
// 解決什麼問題：
// - 把「該推到哪個 group / 哪個 method 名稱」這種 SignalR 細節集中管理，
//   Worker 程式碼不需要知道 group key 的拼字規則。

using AOIOpsPlatform.Api.Hubs;
using AOIOpsPlatform.Application.Hubs;
using AOIOpsPlatform.Application.Spc;
using Microsoft.AspNetCore.SignalR;

namespace AOIOpsPlatform.Api.Realtime;

/// <summary>SignalR 版的 SPC broker。</summary>
public sealed class SpcHubBroker : ISpcHubBroker
{
    private readonly IHubContext<SpcHub> _hub;

    public SpcHubBroker(IHubContext<SpcHub> hub) => _hub = hub;

    public Task PushSpcPointAsync(SpcPointPayload payload, CancellationToken cancellationToken)
    {
        // 為什麼用 group：
        // - SPC 點頻率高，全部 broadcast 會浪費前端流量；
        //   讓只看 "SMT-A / yield_rate" 的人不會收到 "AOI / temperature" 的點。
        var group = SpcHub.BuildGroupName(payload.LineCode, payload.ParameterCode);
        return _hub.Clients.Group(group).SendAsync("spcPoint", payload, cancellationToken);
    }

    public Task PushSpcViolationAsync(SpcPointPayload payload, CancellationToken cancellationToken)
    {
        // 為什麼違規事件 broadcast 到 All：
        // - 違規表設計成「全產線通用視野」，所有打開 SPC 頁的人都該看到，
        //   不需要再多開一條 group。
        return _hub.Clients.All.SendAsync("spcViolation", payload, cancellationToken);
    }
}

/// <summary>SignalR 版的異常 broker。</summary>
public sealed class AlarmHubBroker : IAlarmHubBroker
{
    private readonly IHubContext<AlarmHub> _hub;
    public AlarmHubBroker(IHubContext<AlarmHub> hub) => _hub = hub;

    public Task PushAlarmAsync(object payload, CancellationToken cancellationToken)
        => _hub.Clients.All.SendAsync("alarm", payload, cancellationToken);
}

/// <summary>SignalR 版的工單 broker。</summary>
public sealed class WorkorderHubBroker : IWorkorderHubBroker
{
    private readonly IHubContext<WorkorderHub> _hub;
    public WorkorderHubBroker(IHubContext<WorkorderHub> hub) => _hub = hub;

    public Task PushWorkorderAsync(object payload, CancellationToken cancellationToken)
        => _hub.Clients.All.SendAsync("workorder", payload, cancellationToken);
}
