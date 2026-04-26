// AlarmHub：異常記錄頁面的即時推播 Hub。
//
// 為什麼異常與 SPC 分兩個 Hub：
// - 異常事件來源是 RabbitMQ alert queue（業務層、需 ack），SPC 是 Kafka raw（高頻）；
//   分 Hub 可在前端把「高頻管制點」與「重要告警」走不同連線群組、降低互相干擾。
// - 前端只在「異常記錄」頁開連線就好，省下不需要時的 push 流量。

using Microsoft.AspNetCore.SignalR;

namespace AOIOpsPlatform.Api.Hubs;

/// <summary>
/// 異常記錄頁面用的 SignalR Hub。
/// </summary>
/// <remarks>
/// 為什麼先不用 group：
/// - 異常事件量比 SPC 點低非常多，全部 broadcast 對性能不痛不癢；
///   等之後要做「依產線過濾」再加 group 也不遲，避免提早抽象。
/// </remarks>
public sealed class AlarmHub : Hub
{
}
