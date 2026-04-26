// SpcHub：SPC 即時推播的 SignalR Hub。
//
// 為什麼選 SignalR 而不是直接從前端連 Kafka：
// - 瀏覽器無法直接消費 Kafka（二進位協議、CORS、認證問題），
//   讓 .NET 後端統一在伺服器側 consume Kafka，再透過 WebSocket / SSE 推給前端最合理。
// - SignalR 內建自動降級（WebSocket → SSE → Long Polling）和自動重連，
//   不用我們自己處理，相對於手刻 SSE 維運成本低很多。
//
// 解決什麼問題：
// - 給 worker（SpcRealtimeWorker）一個明確的注入點 IHubContext<SpcHub>；
// - 提供 group 訂閱，前端切換站別 / 機台 / 參數時只收自己關心的點，避免雪崩式廣播。

using Microsoft.AspNetCore.SignalR;

namespace AOIOpsPlatform.Api.Hubs;

/// <summary>
/// SPC Dashboard 的即時推播 Hub。
/// </summary>
/// <remarks>
/// 為什麼提供 JoinGroup / LeaveGroup：
/// - 前端切換 line / tool / parameter filter 時，需要動態加入或離開 group，
///   讓 SpcRealtimeWorker 可以用 <c>Clients.Group(...)</c> 把點精準推給該訂閱者。
/// - group key 約定為 <c>"line:{LINE}|param:{PARAM}"</c>，集中管理避免拼字錯誤。
/// </remarks>
public sealed class SpcHub : Hub
{
    /// <summary>
    /// 客戶端訂閱某 (lineCode, parameterCode) 組合的 SPC 推播。
    /// </summary>
    /// <remarks>
    /// 為什麼 lineCode 跟 parameterCode 拼成一個 group：
    /// - 同一條產線、同一個量測參數的點，會在同一張管制圖上呈現。
    /// - 用單一字串 group 比兩層 group 簡單，SignalR 端不用做交集運算。
    /// </remarks>
    public Task JoinGroup(string lineCode, string parameterCode)
    {
        var groupName = BuildGroupName(lineCode, parameterCode);
        return Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    /// <summary>
    /// 離開先前訂閱的群組（前端切 filter 時呼叫）。
    /// </summary>
    public Task LeaveGroup(string lineCode, string parameterCode)
    {
        var groupName = BuildGroupName(lineCode, parameterCode);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    /// <summary>
    /// 由 worker / controller 共用，避免兩邊拼錯 group key。
    /// </summary>
    public static string BuildGroupName(string lineCode, string parameterCode)
        => $"line:{lineCode}|param:{parameterCode}";
}
