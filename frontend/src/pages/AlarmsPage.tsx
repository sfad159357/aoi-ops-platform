// AlarmsPage：異常記錄頁。
//
// 為什麼這頁要做：
// - 前端 4 大選單之一；對應 RabbitMQ alert queue 的事件落地。
// - 工程師最常問「誰在叫、嚴重不嚴重、何時開始」，列表型態最直覺。
//
// 為什麼用「REST 預載 + SignalR push」雙來源：
// - REST 確保「打開頁面就有最近 100 筆」，使用者不會看到空白；
// - SignalR push 確保「新事件不用 F5 就跑出來」。
//
// 解決什麼問題：
// - 一個頁面同時兼顧「歷史可看」與「即時更新」。

import { useEffect, useMemo, useState } from 'react'
import { HubConnectionState } from '@microsoft/signalr'
import { useProfile } from '../domain/useProfile'
import { useAlarmStream, type AlarmEvent } from '../realtime/useAlarmStream'

type ApiAlarm = {
  id: string
  alarmCode: string
  alarmLevel: string | null
  message: string | null
  triggeredAt: string
  clearedAt: string | null
  status: string | null
  source: string | null
  toolCode: string | null
}

/**
 * 把 REST 回傳的 ApiAlarm 轉成 hook buffer 期望的 AlarmEvent。
 *
 * 為什麼要轉：
 * - REST 回傳沒有 lotNo（為了避免額外 join 拖慢 list），但 hook event 型別有；
 *   統一型別比較不會在 row render 時又判斷 null。
 */
function toEvent(a: ApiAlarm): AlarmEvent {
  return {
    id: a.id,
    alarmCode: a.alarmCode,
    alarmLevel: a.alarmLevel,
    message: a.message,
    triggeredAt: a.triggeredAt,
    status: a.status,
    source: a.source,
    toolCode: a.toolCode,
    lotNo: null,
  }
}

export default function AlarmsPage() {
  const { profile } = useProfile()
  const [bootstrap, setBootstrap] = useState<AlarmEvent[] | undefined>(undefined)
  const [loadError, setLoadError] = useState<string | null>(null)
  const baseUrl = useMemo(
    () => (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:8080',
    []
  )

  // 為什麼 bootstrap 用 useState + 一次 effect：
  // - 預載只需打一次，bootstrap 之後新事件交給 SignalR；
  //   把預載資料丟給 hook 做為 initial buffer。
  useEffect(() => {
    const ctrl = new AbortController()
    void (async () => {
      try {
        const res = await fetch(`${baseUrl}/api/alarms?take=100`, { signal: ctrl.signal })
        if (!res.ok) throw new Error(`alarms list failed: ${res.status}`)
        const json = (await res.json()) as ApiAlarm[]
        setBootstrap(json.map(toEvent))
      } catch (e) {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setLoadError(e instanceof Error ? e.message : String(e))
      }
    })()
    return () => ctrl.abort()
  }, [baseUrl])

  const { alarms, connectionState } = useAlarmStream({ bootstrap, maxItems: 200 })

  // 為什麼計算「新進來的 id 集合」：
  // - 給 row 加 highlight 動畫，使用者一眼看出「剛剛跳出來的」。
  // - 用 ref 比對前後一筆 id 即可，不用每筆 timestamp 都比。
  const [recentlyAdded, setRecentlyAdded] = useState<Set<string>>(new Set())
  useEffect(() => {
    if (alarms.length === 0) return
    const newest = alarms[0]
    if (!newest) return
    setRecentlyAdded((prev) => {
      const next = new Set(prev)
      next.add(newest.id)
      return next
    })
    const tid = setTimeout(() => {
      setRecentlyAdded((prev) => {
        const next = new Set(prev)
        next.delete(newest.id)
        return next
      })
    }, 1500)
    return () => clearTimeout(tid)
  }, [alarms])

  return (
    <div style={pageStyle}>
      <div style={headerRowStyle}>
        <div>
          <h1 style={{ margin: 0, fontSize: 18, fontWeight: 600 }}>
            {profile.menus.find((m) => m.id === 'alarm')?.labelZh ?? '異常記錄'}
          </h1>
          <div style={{ color: '#6b7280', fontSize: 12, marginTop: 4 }}>
            來源：RabbitMQ <code style={mono}>alert</code> → SignalR <code style={mono}>/hubs/alarm</code>
            <span style={{ marginLeft: 8 }}>SignalR {labelForState(connectionState)}</span>
          </div>
        </div>
        <div style={{ color: '#9ca3af', fontSize: 12 }}>共 {alarms.length} 筆（即時）</div>
      </div>

      {loadError && (
        <div style={errorStyle}>歷史告警讀取失敗：{loadError}（仍會顯示 SignalR 推來的即時事件）</div>
      )}

      <div style={tableContainerStyle}>
        <table style={tableStyle}>
          <thead>
            <tr style={{ background: '#161b22', color: '#9ca3af', fontSize: 12 }}>
              <th style={thStyle}>觸發時間</th>
              <th style={thStyle}>等級</th>
              <th style={thStyle}>機台</th>
              <th style={thStyle}>告警碼</th>
              <th style={thStyle}>訊息</th>
              <th style={thStyle}>狀態</th>
              <th style={thStyle}>來源</th>
            </tr>
          </thead>
          <tbody>
            {alarms.map((a) => (
              <tr
                key={a.id}
                style={{
                  background: recentlyAdded.has(a.id) ? '#1f2a37' : 'transparent',
                  transition: 'background 1.2s ease-out',
                  borderBottom: '1px solid #21262d',
                }}
              >
                <td style={tdMonoStyle}>{formatTime(a.triggeredAt)}</td>
                <td style={tdStyle}>
                  <SeverityBadge level={a.alarmLevel} />
                </td>
                <td style={tdMonoStyle}>{a.toolCode ?? '-'}</td>
                <td style={tdMonoStyle}>{a.alarmCode}</td>
                <td style={tdStyle}>{a.message ?? '-'}</td>
                <td style={tdStyle}>{a.status ?? '-'}</td>
                <td style={tdStyle}>{a.source ?? '-'}</td>
              </tr>
            ))}
            {alarms.length === 0 && (
              <tr>
                <td colSpan={7} style={{ ...tdStyle, textAlign: 'center', color: '#6b7280', padding: 24 }}>
                  尚未收到任何告警。請確認 ingestion 容器與 RabbitMQ 已啟動。
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}

/**
 * 嚴重度色彩 badge。
 *
 * 為什麼把顏色放在這支元件而不是 CSS：
 * - 整個 frontend 還沒導入 design token / tailwind，
 *   inline style + 一張字典是最不增加心智負擔的做法。
 */
function SeverityBadge({ level }: { level: string | null }) {
  const normalized = (level ?? '').toLowerCase()
  const palette = severityPalette(normalized)
  return (
    <span
      style={{
        display: 'inline-block',
        padding: '2px 8px',
        borderRadius: 4,
        background: palette.bg,
        color: palette.fg,
        fontSize: 11,
        fontWeight: 600,
        fontFamily: 'JetBrains Mono, monospace',
        textTransform: 'uppercase',
      }}
    >
      {level ?? 'unknown'}
    </span>
  )
}

function severityPalette(severity: string): { fg: string; bg: string } {
  switch (severity) {
    case 'critical':
    case 'high':
      return { fg: '#fff', bg: '#f85149' }
    case 'medium':
      return { fg: '#1f1300', bg: '#f0b429' }
    case 'low':
      return { fg: '#0d1117', bg: '#3fb950' }
    default:
      return { fg: '#e5e7eb', bg: '#374151' }
  }
}

function formatTime(iso: string): string {
  try {
    const d = new Date(iso)
    return d.toLocaleString('zh-TW', { hour12: false })
  } catch {
    return iso
  }
}

function labelForState(state: HubConnectionState | 'idle'): string {
  switch (state) {
    case HubConnectionState.Connected:
      return '已連線'
    case HubConnectionState.Reconnecting:
      return '重連中…'
    case HubConnectionState.Connecting:
      return '連線中…'
    case HubConnectionState.Disconnected:
      return '斷線'
    default:
      return '未連線'
  }
}

const pageStyle: React.CSSProperties = {
  background: '#0d1117',
  color: '#e5e7eb',
  minHeight: 'calc(100vh - 48px)',
  padding: 24,
  fontFamily: 'Noto Sans TC, system-ui, sans-serif',
}

const headerRowStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'baseline',
  justifyContent: 'space-between',
  marginBottom: 16,
}

const tableContainerStyle: React.CSSProperties = {
  background: '#0d1117',
  border: '1px solid #21262d',
  borderRadius: 8,
  overflow: 'hidden',
}

const tableStyle: React.CSSProperties = {
  width: '100%',
  borderCollapse: 'collapse',
  fontSize: 13,
}

const thStyle: React.CSSProperties = {
  textAlign: 'left',
  padding: '10px 12px',
  borderBottom: '1px solid #21262d',
  fontWeight: 500,
}

const tdStyle: React.CSSProperties = {
  padding: '10px 12px',
}

const tdMonoStyle: React.CSSProperties = {
  ...tdStyle,
  fontFamily: 'JetBrains Mono, monospace',
}

const mono: React.CSSProperties = {
  fontFamily: 'JetBrains Mono, monospace',
  background: '#161b22',
  border: '1px solid #21262d',
  padding: '1px 4px',
  borderRadius: 4,
}

const errorStyle: React.CSSProperties = {
  padding: 12,
  marginBottom: 12,
  background: '#1f1300',
  border: '1px solid #f0b429',
  borderRadius: 6,
  color: '#f0b429',
  fontSize: 12,
}
