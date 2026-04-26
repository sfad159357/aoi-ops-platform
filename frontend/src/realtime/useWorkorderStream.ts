// useWorkorderStream：訂閱 WorkorderHub、收 workorder 事件的 React hook。
//
// 為什麼跟 useAlarmStream 幾乎長得一樣還要拆：
// - 兩者欄位差很多（priority / status / workorderNo vs alarmCode / level）；
//   試圖共用 generic hook 只會讓 type 變得脆弱。
// - 兩個頁面有獨立連線生命週期，分開實作可以避免「離開告警頁卻還在收工單」。
//
// 解決什麼問題：
// - 工單管理頁面在打開時要能即時長出新工單，UX 與 alarm 頁一致。

import { useEffect, useRef, useState } from 'react'
import { HubConnection, HubConnectionState } from '@microsoft/signalr'
import { createHubConnection, safeStopHub } from './signalr'

export type WorkorderEvent = {
  id: string
  workorderNo: string
  priority: string | null
  status: string | null
  createdAt: string
  lotNo: string | null
  severity: string | null
}

export type UseWorkorderStreamOptions = {
  bootstrap?: WorkorderEvent[]
  maxItems?: number
}

export type UseWorkorderStreamResult = {
  connectionState: HubConnectionState | 'idle'
  workorders: WorkorderEvent[]
}

/**
 * 訂閱 WorkorderHub 的 hook，行為與 useAlarmStream 對稱。
 */
export function useWorkorderStream(opts: UseWorkorderStreamOptions = {}): UseWorkorderStreamResult {
  const { bootstrap, maxItems = 200 } = opts
  const [connectionState, setConnectionState] = useState<HubConnectionState | 'idle'>('idle')
  const [workorders, setWorkorders] = useState<WorkorderEvent[]>(bootstrap ?? [])
  const connectionRef = useRef<HubConnection | null>(null)
  const bootstrappedRef = useRef(false)

  useEffect(() => {
    if (bootstrappedRef.current) return
    if (!bootstrap || bootstrap.length === 0) return
    bootstrappedRef.current = true
    setWorkorders(bootstrap)
  }, [bootstrap])

  useEffect(() => {
    let cancelled = false

    async function connect() {
      try {
        const conn = await createHubConnection('workorder')
        if (cancelled) {
          await safeStopHub(conn)
          return
        }
        connectionRef.current = conn
        setConnectionState(conn.state)

        conn.onreconnecting(() => setConnectionState(HubConnectionState.Reconnecting))
        conn.onreconnected(() => setConnectionState(HubConnectionState.Connected))
        conn.onclose(() => setConnectionState(HubConnectionState.Disconnected))

        conn.on('workorder', (payload: WorkorderEvent) => {
          setWorkorders((prev) => {
            const next = [payload, ...prev]
            if (next.length > maxItems) next.length = maxItems
            return next
          })
        })
      } catch (err) {
        console.warn('[useWorkorderStream] 連線失敗：', err)
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

  return { connectionState, workorders }
}
