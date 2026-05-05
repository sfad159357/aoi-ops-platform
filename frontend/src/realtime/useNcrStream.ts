// useNcrStream：訂閱 NcrHub、收 ncr 事件的 React hook。
//
// 為什麼從 useWorkorderStream 改名：
// - 「workorder」在 MES 常指生產工單/製令；這裡實際推的是缺陷/異常觸發的處置追蹤單，
//   用 NCR（不良單）命名可避免使用者誤解成生產指令。
//
// 解決什麼問題：
// - 不良單管理頁面在打開時要能即時長出新單，UX 與 alarm 頁一致。

import { useEffect, useRef, useState } from 'react'
import { HubConnection, HubConnectionState } from '@microsoft/signalr'
import { createHubConnection, safeStopHub } from './signalr'

export type NcrEvent = {
  id: string
  ncrNo: string
  priority: string | null
  status: string | null
  createdAt: string
  lotNo: string | null
  severity: string | null
  panelNo: string | null
  toolCode: string | null
  lineCode: string | null
  stationCode: string | null
  operatorCode: string | null
  operatorName: string | null
  defectCode: string | null
}

export type UseNcrStreamOptions = {
  bootstrap?: NcrEvent[]
  maxItems?: number
}

export type UseNcrStreamResult = {
  connectionState: HubConnectionState | 'idle'
  ncrs: NcrEvent[]
}

/**
 * 訂閱 NcrHub 的 hook，行為與 useAlarmStream 對稱。
 */
export function useNcrStream(opts: UseNcrStreamOptions = {}): UseNcrStreamResult {
  const { bootstrap, maxItems = 200 } = opts
  const [connectionState, setConnectionState] = useState<HubConnectionState | 'idle'>('idle')
  const [ncrs, setNcrs] = useState<NcrEvent[]>(bootstrap ?? [])
  const connectionRef = useRef<HubConnection | null>(null)
  const bootstrappedRef = useRef(false)

  useEffect(() => {
    if (bootstrappedRef.current) return
    if (!bootstrap || bootstrap.length === 0) return
    bootstrappedRef.current = true
    setNcrs(bootstrap)
  }, [bootstrap])

  useEffect(() => {
    let cancelled = false

    async function connect() {
      try {
        const conn = await createHubConnection('ncr')
        if (cancelled) {
          await safeStopHub(conn)
          return
        }
        connectionRef.current = conn
        setConnectionState(conn.state)

        conn.onreconnecting(() => setConnectionState(HubConnectionState.Reconnecting))
        conn.onreconnected(() => setConnectionState(HubConnectionState.Connected))
        conn.onclose(() => setConnectionState(HubConnectionState.Disconnected))

        conn.on('ncr', (payload: NcrEvent) => {
          setNcrs((prev) => {
            const next = [payload, ...prev]
            if (next.length > maxItems) next.length = maxItems
            return next
          })
        })
      } catch (err) {
        console.warn('[useNcrStream] 連線失敗：', err)
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

  return { connectionState, ncrs }
}

