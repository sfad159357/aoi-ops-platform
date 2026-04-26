// RealtimeStatusBadge：顯示 SignalR 連線狀態的 nav 小元件。
//
// 為什麼做這個：
// - W04 需要肉眼驗證「前端真的能連上後端 SignalR Hub」；
//   一個顏色 + 文字的 badge 比打開 DevTools 看 console 直觀很多。
// - 後續 W11 也可以用這個 badge 監控延遲（顯示最後一筆推播時間）。

import { HubConnectionState } from '@microsoft/signalr'
import { useEffect, useState } from 'react'
import { createHubConnection, safeStopHub } from './signalr'

type Props = {
  /** 用哪個 hub 當「總連線狀態」指標，預設 spc */
  hub?: 'spc' | 'alarm' | 'workorder'
}

/**
 * 顯示 SignalR 連線狀態的圓點 + 文字。
 *
 * 為什麼直接連 hub 而不是共用 useSpcStream：
 * - 這個 badge 只關心連線狀態，不需要 group 訂閱也不需要點 buffer。
 *   保持單純，避免不必要的記憶體 / 重渲染。
 */
export default function RealtimeStatusBadge({ hub = 'spc' }: Props) {
  const [state, setState] = useState<HubConnectionState | 'idle'>('idle')
  const [errMsg, setErrMsg] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    let conn: Awaited<ReturnType<typeof createHubConnection>> | null = null

    void (async () => {
      try {
        conn = await createHubConnection(hub)
        if (cancelled) {
          await safeStopHub(conn)
          return
        }
        setState(conn.state)
        conn.onreconnecting(() => setState(HubConnectionState.Reconnecting))
        conn.onreconnected(() => setState(HubConnectionState.Connected))
        conn.onclose(() => setState(HubConnectionState.Disconnected))
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err)
        setErrMsg(message)
        setState(HubConnectionState.Disconnected)
      }
    })()

    return () => {
      cancelled = true
      const target = conn
      conn = null
      void safeStopHub(target)
    }
  }, [hub])

  const color = pickColor(state)
  const label = pickLabel(state)
  return (
    <span
      title={errMsg ?? `SignalR ${hub} hub state: ${state}`}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        fontSize: 12,
        color: '#9ca3af',
        marginLeft: 'auto',
      }}
    >
      <span
        style={{
          width: 8,
          height: 8,
          borderRadius: '50%',
          background: color,
          boxShadow: state === HubConnectionState.Connected ? `0 0 6px ${color}` : 'none',
          display: 'inline-block',
        }}
      />
      Live: {label}
    </span>
  )
}

function pickColor(state: HubConnectionState | 'idle'): string {
  switch (state) {
    case HubConnectionState.Connected:
      return '#3fb950'
    case HubConnectionState.Connecting:
    case HubConnectionState.Reconnecting:
      return '#f0b429'
    case HubConnectionState.Disconnecting:
    case HubConnectionState.Disconnected:
      return '#f85149'
    default:
      return '#6b7280'
  }
}

function pickLabel(state: HubConnectionState | 'idle'): string {
  switch (state) {
    case HubConnectionState.Connected:
      return '連線中'
    case HubConnectionState.Connecting:
      return '連線中…'
    case HubConnectionState.Reconnecting:
      return '重連中…'
    case HubConnectionState.Disconnecting:
      return '中斷中…'
    case HubConnectionState.Disconnected:
      return '斷線'
    default:
      return '未連線'
  }
}
