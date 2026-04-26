// ISpcHubBroker / IAlarmHubBroker / IWorkorderHubBroker：
// Application 層用來推 SignalR 訊息的抽象介面。
//
// 為什麼要在 Application 自己定義一組 broker：
// - 直接在 Application 引用 Microsoft.AspNetCore.SignalR 會把整包 ASP.NET Core 拖進來，
//   違反「Application 不依賴外部框架」的 Clean Architecture 慣例。
// - 由 Api 層提供 IHubContext-based 實作，測試時 mock 這個介面就好。
//
// 解決什麼問題：
// - Worker 不用知道 SignalR 怎麼運作，只看到「我有新點要推」這個語意。

using AOIOpsPlatform.Application.Spc;

namespace AOIOpsPlatform.Application.Hubs;

/// <summary>SPC Hub 推播抽象。</summary>
public interface ISpcHubBroker
{
    /// <summary>
    /// 把新的 SPC 點推給訂閱（lineCode, parameterCode）group 的客戶端。
    /// </summary>
    Task PushSpcPointAsync(SpcPointPayload payload, CancellationToken cancellationToken);

    /// <summary>
    /// 推一筆違規事件（紅燈 / 黃燈），讓「SPC 違規表」即時新增一列。
    /// </summary>
    Task PushSpcViolationAsync(SpcPointPayload payload, CancellationToken cancellationToken);
}

/// <summary>異常記錄 Hub 推播抽象。</summary>
public interface IAlarmHubBroker
{
    Task PushAlarmAsync(object payload, CancellationToken cancellationToken);
}

/// <summary>工單管理 Hub 推播抽象。</summary>
public interface IWorkorderHubBroker
{
    Task PushWorkorderAsync(object payload, CancellationToken cancellationToken);
}
