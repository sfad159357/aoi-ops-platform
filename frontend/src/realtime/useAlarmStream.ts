// useAlarmStream：訂閱 AlarmHub、收 alarm 事件的 React hook。
//
// 為什麼跟 useSpcStream 拆兩個 hook：
// - 兩者的 buffer 行為不一樣：SPC 點走滑動視窗（保留最新 N 點繪圖），
//   告警走「最新在前 + REST 預載歷史」；硬塞同一個 hook 會難維護。
// - 也讓使用方可以單獨在「異常記錄」頁開連線，不被 SPC 高頻流量干擾。
//
// 解決什麼問題：
// - 異常記錄頁面要能「打開就有最近 100 筆 + 新事件即時長出來」；
//   把預載 + push 兩件事合在 hook 內，元件層只關心畫面。

import { useEffect, useRef, useState } from 'react'
import { HubConnection, HubConnectionState } from '@microsoft/signalr'
import { createHubConnection, safeStopHub } from './signalr'

/**
 * 對齊後端 AlarmRabbitWorker 推出來的物件結構。
 *
 * 為什麼用 string 表示 datetime：
 * - SignalR / System.Text.Json 預設把 DateTimeOffset 序列化成 ISO 8601 字串，
 *   留 string 比 Date 更貼近 wire format，讓元件決定怎麼格式化。
 */
// 為什麼把 alarm 事件擴充到 line/station/panel/operator：
// - AlarmRabbitWorker 已將完整關聯欄位寫進 alarms 表並 push 到 SignalR；
// - 前端列表要顯示「機台、產線、站別、批次、板號、人員」六個關聯欄位，事件型別必須先對齊。
export type AlarmEvent = {
  id: string
  alarmCode: string
  alarmLevel: string | null
  message: string | null
  triggeredAt: string
  status: string | null
  source: string | null
  toolCode: string | null
  lineCode: string | null
  stationCode: string | null
  lotNo: string | null
  panelNo: string | null
  operatorCode: string | null
  operatorName: string | null
}

export type UseAlarmStreamOptions = {
  /** 預載的歷史告警；hook 會放在 buffer 最後面，新訊息會 prepend 在前。 */
  bootstrap?: AlarmEvent[]
  /** buffer 大小，預設 200。 */
  maxItems?: number
}

export type UseAlarmStreamResult = {
  connectionState: HubConnectionState | 'idle'
  alarms: AlarmEvent[]
}

/**
 * 訂閱 AlarmHub，並把推進來的事件 prepend 到 buffer。
 *
 * 為什麼把 bootstrap 也納入 hook：
 * - 避免元件需要分別管 fetch 與 push 兩個 state；
 *   element 只看到一份「目前所有告警」陣列即可。
 */
export function useAlarmStream(opts: UseAlarmStreamOptions = {}): UseAlarmStreamResult {
  const { bootstrap, maxItems = 200 } = opts
  const [connectionState, setConnectionState] = useState<HubConnectionState | 'idle'>('idle')
  const [alarms, setAlarms] = useState<AlarmEvent[]>(bootstrap ?? [])
  const connectionRef = useRef<HubConnection | null>(null)
  const bootstrappedRef = useRef(false)

  // 為什麼用 ref 控制只 bootstrap 一次：
  // - 連線重連後不該重新蓋掉剛收到的新事件；
  //   只在第一次傳入 bootstrap 時把它套到 state 上。
  useEffect(() => {
    if (bootstrappedRef.current) return
    if (!bootstrap || bootstrap.length === 0) return
    bootstrappedRef.current = true
    setAlarms(bootstrap)
  }, [bootstrap])

  useEffect(() => {
    let cancelled = false

    async function connect() {
      try {
        const conn = await createHubConnection('alarm')
        if (cancelled) {
          await safeStopHub(conn)
          return
        }
        connectionRef.current = conn
        setConnectionState(conn.state)

        conn.onreconnecting(() => setConnectionState(HubConnectionState.Reconnecting))
        conn.onreconnected(() => setConnectionState(HubConnectionState.Connected))
        conn.onclose(() => setConnectionState(HubConnectionState.Disconnected))

        conn.on('alarm', (payload: AlarmEvent) => {
          setAlarms((prev) => {
            const next = [payload, ...prev]
            if (next.length > maxItems) next.length = maxItems
            return next
          })
        })
      } catch (err) {
        console.warn('[useAlarmStream] 連線失敗：', err)
        setConnectionState(HubConnectionState.Disconnected)
      }
    }

    void connect()

    return () => {
      cancelled = true
      const conn = connectionRef.current
      connectionRef.current = null
      void safeStopHub(conn)
    }
  }, [maxItems])

  return { connectionState, alarms }
}
