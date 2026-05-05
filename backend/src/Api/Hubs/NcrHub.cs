// NcrHub：不良單（NCR）管理頁面的即時推播 Hub。
//
// 為什麼從 WorkorderHub 改名：
// - 在 MES 業務語言中，work order 通常指「生產工單/製令」；
//   這個 Hub 推的是「缺陷/異常觸發的處置追蹤單」，改為 NCR 才不會誤導使用者。

using Microsoft.AspNetCore.SignalR;

namespace AOIOpsPlatform.Api.Hubs;

/// <summary>
/// 不良單（NCR）管理頁面用的 SignalR Hub。
/// </summary>
public sealed class NcrHub : Hub
{
}

