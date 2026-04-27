// WorkordersPage：工單管理頁。
//
// 為什麼這頁要做：
// - 4 大選單之一；對應 RabbitMQ workorder queue 落地的工單事件。
// - 工程主管最常問「P1 工單還有沒有沒處理的、最近多了幾張」，列表型態即可。
//
// 為什麼樣式跟異常記錄頁幾乎一樣：
// - 都是「列表 + 即時長新一行」的功能，UX 一致使用者最不容易迷路；
//   不過資料欄位完全不同，所以分兩個頁面與兩個 hook，避免泛型過度抽象。
//
// 解決什麼問題：
// - 工單建立後不需要重整就會跑出來，配合「新一行高亮 1.5 秒」動畫，
//   讓主管能即時感受系統壓力。

import { Fragment, useEffect, useMemo, useState } from 'react'
import { HubConnectionState } from '@microsoft/signalr'
import { useProfile } from '../domain/useProfile'
import { useWorkorderStream, type WorkorderEvent } from '../realtime/useWorkorderStream'

type ApiWorkorder = {
  id: string
  workorderNo: string
  priority: string | null
  status: string | null
  sourceQueue: string | null
  createdAt: string
  lotNo: string | null
}

function toEvent(w: ApiWorkorder): WorkorderEvent {
  return {
    id: w.id,
    workorderNo: w.workorderNo,
    priority: w.priority,
    status: w.status,
    createdAt: w.createdAt,
    lotNo: w.lotNo,
    severity: null,
  }
}

export default function WorkordersPage() {
  const { profile } = useProfile()
  const [bootstrap, setBootstrap] = useState<WorkorderEvent[] | undefined>(undefined)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [dateFilter, setDateFilter] = useState<string>(() => todayYmd())
  const baseUrl = useMemo(
    () => (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:8080',
    []
  )

  useEffect(() => {
    const ctrl = new AbortController()
    void (async () => {
      try {
        const res = await fetch(`${baseUrl}/api/workorders?take=100`, { signal: ctrl.signal })
        if (!res.ok) throw new Error(`workorders list failed: ${res.status}`)
        const json = (await res.json()) as ApiWorkorder[]
        setBootstrap(json.map(toEvent))
      } catch (e) {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setLoadError(e instanceof Error ? e.message : String(e))
      }
    })()
    return () => ctrl.abort()
  }, [baseUrl])

  const { workorders, connectionState } = useWorkorderStream({ bootstrap, maxItems: 200 })

  // 為什麼要做「日期篩選，預設今天」：
  // - 工單屬於日常排程/追單工具，主管通常先看「今天新增/今天未結案」；
  // - 預設今天能讓 demo 或實際使用時不會看到空白或被歷史工單淹沒。
  const filteredWorkorders = useMemo(() => {
    if (!dateFilter) return workorders
    return workorders.filter((w) => toYmd(w.createdAt) === dateFilter)
  }, [workorders, dateFilter])

  const grouped = useMemo(
    () => groupByYmd(filteredWorkorders, (w) => w.createdAt),
    [filteredWorkorders]
  )

  const [recentlyAdded, setRecentlyAdded] = useState<Set<string>>(new Set())
  useEffect(() => {
    if (workorders.length === 0) return
    const newest = workorders[0]
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
  }, [workorders])

  return (
    <div style={pageStyle}>
      <div style={headerRowStyle}>
        <div>
          <h1 style={{ margin: 0, fontSize: 18, fontWeight: 600 }}>
            {profile.menus.find((m) => m.id === 'wo')?.labelZh ?? '工單管理'}
          </h1>
          <div style={{ color: '#6b7280', fontSize: 12, marginTop: 4 }}>
            來源：RabbitMQ <code style={mono}>workorder</code> → SignalR <code style={mono}>/hubs/workorder</code>
            <span style={{ marginLeft: 8 }}>SignalR {labelForState(connectionState)}</span>
          </div>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <label style={{ color: '#9ca3af', fontSize: 12 }}>日期</label>
          <input
            type="date"
            value={dateFilter}
            onChange={(e) => setDateFilter(e.target.value)}
            style={dateInputStyle}
          />
          <div style={{ color: '#9ca3af', fontSize: 12 }}>共 {filteredWorkorders.length} 筆</div>
        </div>
      </div>

      {loadError && (
        <div style={errorStyle}>歷史工單讀取失敗：{loadError}（仍會顯示 SignalR 推來的即時事件）</div>
      )}

      <div style={tableContainerStyle}>
        <table style={tableStyle}>
          <thead>
            <tr style={{ background: '#161b22', color: '#9ca3af', fontSize: 12 }}>
              <th style={thStyle}>建立時間</th>
              <th style={thStyle}>工單編號</th>
              <th style={thStyle}>優先級</th>
              <th style={thStyle}>狀態</th>
              <th style={thStyle}>{profile.entities.lot.labelZh ?? '工單批次'}</th>
            </tr>
          </thead>
          <tbody>
            {grouped.map(([ymd, items]) => (
              <Fragment key={`group-${ymd}`}>
                <tr>
                  <td colSpan={5} style={groupHeaderStyle}>
                    {ymd}（{items.length} 筆）
                  </td>
                </tr>
                {items.map((w) => (
                  <tr
                    key={w.id}
                    style={{
                      background: recentlyAdded.has(w.id) ? '#1f2a37' : 'transparent',
                      transition: 'background 1.2s ease-out',
                      borderBottom: '1px solid #21262d',
                    }}
                  >
                    <td style={tdMonoStyle}>{formatTime(w.createdAt)}</td>
                    <td style={tdMonoStyle}>{w.workorderNo}</td>
                    <td style={tdStyle}>
                      <PriorityBadge priority={w.priority} />
                    </td>
                    <td style={tdStyle}>{w.status ?? '-'}</td>
                    <td style={tdMonoStyle}>{w.lotNo ?? '-'}</td>
                  </tr>
                ))}
              </Fragment>
            ))}
            {filteredWorkorders.length === 0 && (
              <tr>
                <td colSpan={5} style={{ ...tdStyle, textAlign: 'center', color: '#6b7280', padding: 24 }}>
                  這一天沒有任何工單（或尚未收到即時事件）。觸發一次高嚴重度 defect 後即可看到 P1 工單。
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function PriorityBadge({ priority }: { priority: string | null }) {
  const palette = priorityPalette((priority ?? '').toUpperCase())
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
      }}
    >
      {priority ?? '-'}
    </span>
  )
}

function priorityPalette(priority: string): { fg: string; bg: string } {
  switch (priority) {
    case 'P1':
      return { fg: '#fff', bg: '#f85149' }
    case 'P2':
      return { fg: '#1f1300', bg: '#f0b429' }
    case 'P3':
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

function toYmd(iso: string): string {
  try {
    const d = new Date(iso)
    return d.toLocaleDateString('sv-SE')
  } catch {
    return ''
  }
}

function todayYmd(): string {
  return new Date().toLocaleDateString('sv-SE')
}

function groupByYmd<T>(rows: T[], getIso: (row: T) => string): Array<[string, T[]]> {
  const map = new Map<string, T[]>()
  for (const r of rows) {
    const k = toYmd(getIso(r)) || 'unknown'
    const arr = map.get(k)
    if (arr) arr.push(r)
    else map.set(k, [r])
  }
  return Array.from(map.entries()).sort((a, b) => (a[0] < b[0] ? 1 : a[0] > b[0] ? -1 : 0))
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

const dateInputStyle: React.CSSProperties = {
  background: '#161b22',
  color: '#e5e7eb',
  border: '1px solid #21262d',
  padding: '6px 8px',
  borderRadius: 6,
  fontFamily: 'JetBrains Mono, monospace',
  fontSize: 12,
}

const groupHeaderStyle: React.CSSProperties = {
  padding: '8px 12px',
  background: '#0b1220',
  color: '#9ca3af',
  fontSize: 11,
  borderBottom: '1px solid #21262d',
  fontFamily: 'JetBrains Mono, monospace',
}
