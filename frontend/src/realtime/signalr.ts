// signalr.ts：SignalR 連線工廠。
//
// 為什麼集中在一個檔案：
// - 4 個 Hub（SPC / Alarm / Workorder / Trace）都要重連 / 退避 / log 設定，分散寫會很零散；
//   集中後每個 hook 只負責「連哪個 hub、註冊哪些 method」，更乾淨。
// - 後端 hub 路徑改變時，只需改這裡一個常數。
//
// 解決什麼問題：
// - 前端不需要直接接觸 Kafka / RabbitMQ；只透過 SignalR 收推播，
//   開發者無感於底層 broker 切換。

import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'

/**
 * SignalR Hub 邏輯路徑（不含 host）。
 *
 * 為什麼用對應表常數：
 * - 維護單一事實來源；hook 內就不會有人寫死字串造成拼錯。
 */
export const HubPaths = {
  spc: '/hubs/spc',
  alarm: '/hubs/alarm',
  workorder: '/hubs/workorder',
} as const

export type HubName = keyof typeof HubPaths

/**
 * 從環境變數推斷 SignalR base URL。
 *
 * 為什麼跟 VITE_API_BASE_URL 共用：
 * - SignalR Hub 與 REST API 走同一個 backend，沒有理由再多一個變數讓使用者搞混。
 */
export function getSignalrBaseUrl(): string {
  return (
    (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:8080'
  )
}

/**
 * 建立並啟動一條 SignalR 連線。
 *
 * 為什麼用 withAutomaticReconnect 預設策略：
 * - SignalR 內建會在 0/2/10/30 秒嘗試重連；對 demo 與一般使用情境足夠。
 * - 真正生產級需求（指數退避、上限、jitter）等真的有需要再客製。
 *
 * 為什麼預設 LogLevel.Warning：
 * - Information 太吵會把 console 灌爆；Warning 留得下重要訊息又不干擾。
 */
export async function createHubConnection(hub: HubName): Promise<HubConnection> {
  const url = `${getSignalrBaseUrl()}${HubPaths[hub]}`
  const connection = new HubConnectionBuilder()
    .withUrl(url)
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()

  await connection.start()
  return connection
}

/**
 * 安全停止連線；多次呼叫不會 throw。
 *
 * 為什麼包這層：
 * - React StrictMode 在 dev 會跑兩次 effect cleanup；直接 connection.stop() 在第二次有時會丟錯。
 *   先檢查狀態可避免 console 噪音。
 */
export async function safeStopHub(connection: HubConnection | null) {
  if (!connection) return
  if (connection.state === HubConnectionState.Disconnected) return
  try {
    await connection.stop()
  } catch {
    // 忽略 stop 期間的競態錯誤
  }
}
