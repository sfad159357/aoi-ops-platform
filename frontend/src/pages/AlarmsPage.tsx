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

import { Fragment, useEffect, useMemo, useState } from 'react'
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
  lineCode: string | null
  stationCode: string | null
  lotNo: string | null
  panelNo: string | null
  operatorCode: string | null
  operatorName: string | null
}

/**
 * 把 REST 回傳的 ApiAlarm 轉成 hook buffer 期望的 AlarmEvent。
 *
 * 為什麼要轉：
 * - REST 與 SignalR 兩條來源型別差異不大（皆是 alarms 表的 DTO），
 *   有專用 toEvent 可保留欄位 mapping 在一個地方，未來新增欄位只需動這裡與 hook。
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
    lineCode: a.lineCode,
    stationCode: a.stationCode,
    lotNo: a.lotNo,
    panelNo: a.panelNo,
    operatorCode: a.operatorCode,
    operatorName: a.operatorName,
  }
}

export default function AlarmsPage() {
  const { profile } = useProfile()
  const [bootstrap, setBootstrap] = useState<AlarmEvent[] | undefined>(undefined)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [dateFilter, setDateFilter] = useState<string>(() => todayYmd())
  const [levelFilters, setLevelFilters] = useState<string[]>([])
  const [lineFilters, setLineFilters] = useState<string[]>([])
  const [stationFilters, setStationFilters] = useState<string[]>([])
  const [toolFilters, setToolFilters] = useState<string[]>([])
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

  // 為什麼把「日期」先切成獨立結果：
  // - 使用者要求多欄位篩選要和日期一起關聯，先得到日期集合後，
  //   後面的等級/產線/站別/機台選項與結果都會跟著同一天資料變動。
  const dateScopedAlarms = useMemo(() => {
    if (!dateFilter) return alarms
    return alarms.filter((a) => toYmd(a.triggeredAt) === dateFilter)
  }, [alarms, dateFilter])

  // 為什麼選項要從 dateScopedAlarms 推導：
  // - 讓多選下拉只顯示「該日期真實存在」的值，避免選到該日不存在的條件造成誤解。
  const levelOptions = useMemo(
    () => uniqueValues(dateScopedAlarms, (a) => normalizeFilterValue(a.alarmLevel)),
    [dateScopedAlarms]
  )
  const lineOptions = useMemo(
    () => uniqueValues(dateScopedAlarms, (a) => normalizeFilterValue(a.lineCode)),
    [dateScopedAlarms]
  )
  const stationOptions = useMemo(
    () => uniqueValues(dateScopedAlarms, (a) => normalizeFilterValue(a.stationCode)),
    [dateScopedAlarms]
  )
  const toolOptions = useMemo(
    () => uniqueValues(dateScopedAlarms, (a) => normalizeFilterValue(a.toolCode)),
    [dateScopedAlarms]
  )

  // 為什麼日期變更後要清理已選條件：
  // - 避免保留「新日期不存在」的舊選項，造成畫面看起來像被無效條件卡住。
  useEffect(() => {
    setLevelFilters((prev) => prev.filter((v) => levelOptions.includes(v)))
    setLineFilters((prev) => prev.filter((v) => lineOptions.includes(v)))
    setStationFilters((prev) => prev.filter((v) => stationOptions.includes(v)))
    setToolFilters((prev) => prev.filter((v) => toolOptions.includes(v)))
  }, [levelOptions, lineOptions, stationOptions, toolOptions])

  // 為什麼多欄位篩選採用 AND 關係：
  // - 一次定位特定異常場景（例如某日 + 某線 + 某站 + 某機台）時，AND 才符合現場追查習慣。
  const filteredAlarms = useMemo(() => {
    const levelSet = new Set(levelFilters)
    const lineSet = new Set(lineFilters)
    const stationSet = new Set(stationFilters)
    const toolSet = new Set(toolFilters)
    return dateScopedAlarms.filter((a) => {
      const level = normalizeFilterValue(a.alarmLevel)
      const line = normalizeFilterValue(a.lineCode)
      const station = normalizeFilterValue(a.stationCode)
      const tool = normalizeFilterValue(a.toolCode)
      return (
        (levelSet.size === 0 || levelSet.has(level)) &&
        (lineSet.size === 0 || lineSet.has(line)) &&
        (stationSet.size === 0 || stationSet.has(station)) &&
        (toolSet.size === 0 || toolSet.has(tool))
      )
    })
  }, [dateScopedAlarms, levelFilters, lineFilters, stationFilters, toolFilters])

  // 為什麼做日期分組：
  // - 同一天內仍可能有上百筆事件；用日期段落可快速定位「哪一天爆量」。
  const grouped = useMemo(() => groupByYmd(filteredAlarms, (a) => a.triggeredAt), [filteredAlarms])

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
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <label style={{ color: '#9ca3af', fontSize: 12 }}>日期</label>
          <input
            type="date"
            value={dateFilter}
            onChange={(e) => setDateFilter(e.target.value)}
            style={dateInputStyle}
          />
          {/* 為什麼提供清除全部篩選：
              - 多選條件變多後，使用者常需要快速回到預設視角重新查詢；
              - 一鍵重置可降低逐一取消選項的操作成本。 */}
          <button
            type="button"
            onClick={() => {
              setDateFilter(todayYmd())
              setLevelFilters([])
              setLineFilters([])
              setStationFilters([])
              setToolFilters([])
            }}
            style={resetButtonStyle}
          >
            清除全部篩選
          </button>
          <div style={{ color: '#9ca3af', fontSize: 12 }}>共 {filteredAlarms.length} 筆</div>
        </div>
      </div>

      <div style={filterRowStyle}>
        <FilterMultiSelect
          label="等級"
          options={levelOptions}
          selected={levelFilters}
          onChange={setLevelFilters}
        />
        <FilterMultiSelect
          label="產線"
          options={lineOptions}
          selected={lineFilters}
          onChange={setLineFilters}
        />
        <FilterMultiSelect
          label="站別"
          options={stationOptions}
          selected={stationFilters}
          onChange={setStationFilters}
        />
        <FilterMultiSelect
          label="機台"
          options={toolOptions}
          selected={toolFilters}
          onChange={setToolFilters}
        />
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
              <th style={thStyle}>產線</th>
              <th style={thStyle}>站別</th>
              <th style={thStyle}>機台</th>
              {/* 為什麼 lot/panel 欄位標題改讀 profile：
                  - 不同產業 profile 對 lot/panel 名稱可能不同；
                  - 前端不硬寫名詞，改由後端設定統一詞彙。 */}
              <th style={thStyle}>{profile.entities.lot.labelZh}</th>
              {/* 為什麼此處固定顯示「板號」：
                  - 現場使用者回饋「板」語意太短，容易和其他名詞混淆；
                  - 固定為「板號」可直接對齊 panel_no 欄位語意，降低判讀成本。 */}
              <th style={thStyle}>板號</th>
              <th style={thStyle}>作業員</th>
              <th style={thStyle}>告警碼</th>
              <th style={thStyle}>訊息</th>
              <th style={thStyle}>狀態</th>
            </tr>
          </thead>
          <tbody>
            {grouped.map(([ymd, items]) => (
              <Fragment key={`group-${ymd}`}>
                <tr key={`group-${ymd}`}>
                  <td colSpan={11} style={groupHeaderStyle}>
                    {ymd}（{items.length} 筆）
                  </td>
                </tr>
                {items.map((a) => (
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
                    <td style={tdMonoStyle}>{a.lineCode ?? '-'}</td>
                    <td style={tdMonoStyle}>{a.stationCode ?? '-'}</td>
                    <td style={tdMonoStyle}>{a.toolCode ?? '-'}</td>
                    <td style={tdMonoStyle}>{a.lotNo ?? '-'}</td>
                    <td style={tdMonoStyle}>{a.panelNo ?? '-'}</td>
                    <td style={tdStyle}>{formatOperator(a.operatorCode, a.operatorName)}</td>
                    <td style={tdMonoStyle}>{a.alarmCode}</td>
                    <td style={tdStyle}>{a.message ?? '-'}</td>
                    <td style={tdStyle}>{a.status ?? '-'}</td>
                  </tr>
                ))}
              </Fragment>
            ))}
            {filteredAlarms.length === 0 && (
              <tr>
                <td colSpan={11} style={{ ...tdStyle, textAlign: 'center', color: '#6b7280', padding: 24 }}>
                  這一天沒有任何告警（或尚未收到即時事件）。請確認 ingestion 容器與 RabbitMQ 已啟動。
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

// 為什麼把作業員顯示寫成 helper：
// - 列表「OP-001 王小明」是常見格式，未來新頁面（工單/物料追溯）也會用同樣模式；
//   抽 helper 讓四個頁面顯示一致。
function formatOperator(code: string | null, name: string | null): string {
  if (!code && !name) return '-'
  if (code && name) return `${code} ${name}`
  return code ?? name ?? '-'
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
  // 最新日期在上
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

/**
 * 異常記錄多選篩選器。
 *
 * 為什麼使用原生 multiple select：
 * - 不引入額外 UI 套件就能支援多選，維持目前 MVP 的低依賴；
 * - 以 Ctrl/Cmd 多選符合內部 demo 場景，實作成本最小且可立即交付。
 *
 * 解決什麼問題：
 * - 讓使用者可同時鎖定多個等級/產線/站別/機台，快速縮小異常範圍。
 */
function FilterMultiSelect({
  label,
  options,
  selected,
  onChange,
}: {
  label: string
  options: string[]
  selected: string[]
  onChange: (values: string[]) => void
}) {
  // 為什麼要提供 All 預設值：
  // - 使用者可明確看見目前是「全部」狀態，而不是靠空陣列隱含語意；
  // - 可降低多選條件清空後，不知道是否已回到全量資料的疑慮。
  const value = selected.length === 0 ? [ALL_FILTER_OPTION] : selected
  return (
    <label style={filterBlockStyle}>
      <span style={{ color: '#9ca3af', fontSize: 12 }}>{label}</span>
      <select
        multiple
        value={value}
        onChange={(e) => {
          const next = Array.from(e.currentTarget.selectedOptions, (o) => o.value)
          if (next.length === 0 || next.includes(ALL_FILTER_OPTION)) {
            onChange([])
            return
          }
          onChange(next.filter((v) => v !== ALL_FILTER_OPTION))
        }}
        style={multiSelectStyle}
      >
        <option value={ALL_FILTER_OPTION}>{ALL_FILTER_OPTION}</option>
        {options.map((option) => (
          <option key={option} value={option}>
            {option}
          </option>
        ))}
      </select>
    </label>
  )
}

function normalizeFilterValue(value: string | null | undefined): string {
  return (value ?? '-').trim() || '-'
}

function uniqueValues<T>(rows: T[], pick: (row: T) => string): string[] {
  return Array.from(new Set(rows.map(pick))).sort((a, b) => a.localeCompare(b, 'zh-Hant'))
}

const ALL_FILTER_OPTION = 'All'

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

const filterRowStyle: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(4, minmax(0, 1fr))',
  gap: 10,
  marginBottom: 12,
}

const filterBlockStyle: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 6,
}

const multiSelectStyle: React.CSSProperties = {
  background: '#161b22',
  color: '#e5e7eb',
  border: '1px solid #21262d',
  borderRadius: 6,
  fontFamily: 'JetBrains Mono, monospace',
  fontSize: 12,
  minHeight: 84,
  padding: 6,
}

const resetButtonStyle: React.CSSProperties = {
  background: '#1f2937',
  color: '#e5e7eb',
  border: '1px solid #374151',
  borderRadius: 6,
  padding: '6px 10px',
  fontSize: 12,
  cursor: 'pointer',
}

const groupHeaderStyle: React.CSSProperties = {
  padding: '8px 12px',
  background: '#0b1220',
  color: '#9ca3af',
  fontSize: 11,
  borderBottom: '1px solid #21262d',
  fontFamily: 'JetBrains Mono, monospace',
}
