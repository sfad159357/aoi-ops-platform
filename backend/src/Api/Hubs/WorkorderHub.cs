// WorkorderHub：工單管理頁面的即時推播 Hub。
//
// 為什麼工單獨立一個 Hub：
// - 工單事件由 RabbitMQ workorder queue 觸發，與異常事件來源不同（流量、優先級也不同）；
//   分 Hub 可避免「告警一多就把工單推播淹掉」的問題。
// - 工單頁面通常不會跟 SPC / Alarm 同時被打開；分 Hub 對應頁面，連線開關更乾淨。

using Microsoft.AspNetCore.SignalR;

namespace AOIOpsPlatform.Api.Hubs;

/// <summary>
/// 工單管理頁面用的 SignalR Hub。
/// </summary>
public sealed class WorkorderHub : Hub
{
}
