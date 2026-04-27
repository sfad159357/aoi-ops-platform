// SpcDashboard：SPC 統計製程管制儀表板（v2 — Kafka → SignalR 即時版）。
//
// 為什麼整個重寫：
// - 舊版有 demo / api / live 三種模式，UI 與資料源混雜，很難維護；
//   新版只走「Kafka 即時推播 + SignalR」一條路，完全取消前端 demo 與後端 API 輪詢。
// - 配合 HTML 設計圖採暗色 + JetBrains Mono，並用 KPI / X̄+R 雙圖 / 違規表三段式。
//
// 解決什麼問題：
// - 工程師打開頁面立刻看到「真實產線推來的點」，不再需要按按鈕觸發計算。
// - 切換產業 demo 時，所有文案 / 規格 / 站別自動跟著 profile，不需要改任何程式碼。

import { useEffect, useMemo, useState } from 'react'
import { HubConnectionState } from '@microsoft/signalr'
import { useProfile } from '../domain/useProfile'
import { useSpcStream } from '../realtime/useSpcStream'
import KpiBar, { type KpiCardData } from '../components/spc/KpiBar'
import FilterBar, { type FilterBarValue } from '../components/spc/FilterBar'
import ControlChartPair from '../components/spc/ControlChartPair'
import ViolationTable from '../components/spc/ViolationTable'

type Tool = { code: string; label: string }

export default function SpcDashboard() {
  const { profile } = useProfile()

  // 為什麼預設挑 AOI 線：
  // - 現行 ingestion simulator 的 toolCode 主要是 AOI-A / AOI-B，能確保 demo 一進來就看到即時點；
  // - 若 profile 沒有 AOI 線，再退回第一條線即可（保持 profile 可擴展性）。
  const defaultLine = useMemo(
    () => profile.lines.find((l) => l.code.startsWith('AOI')) ?? profile.lines[0],
    [profile.lines]
  )

  // 為什麼預設挑 yield_rate：
  // - profile 一定有「良率」欄位（PCB / 半導體都有意義），demo 開啟就能看到資料。
  const defaultParameter = useMemo(
    () => profile.parameters.find((p) => p.code === 'yield_rate') ?? profile.parameters[0],
    [profile.parameters]
  )

  const [filter, setFilter] = useState<FilterBarValue>({
    lineCode: defaultLine?.code ?? '',
    toolCode: '',
    parameterCode: defaultParameter?.code ?? 'yield_rate',
  })

  // 為什麼 profile 改變要 reset filter：
  // - 切換產業（pcb ↔ semiconductor）後，原本選的參數可能不存在；
  //   reset 為 profile 的第一個合法選項可以保證 SignalR group 不會卡在「沒人推」的狀態。
  useEffect(() => {
    setFilter({
      lineCode: defaultLine?.code ?? '',
      toolCode: '',
      parameterCode: defaultParameter?.code ?? 'yield_rate',
    })
  }, [profile.profileId, defaultLine, defaultParameter])

  const tools = useTools()
  const parameter = useMemo(
    () => profile.parameters.find((p) => p.code === filter.parameterCode) ?? defaultParameter,
    [filter.parameterCode, profile.parameters, defaultParameter]
  )

  const { connectionState, points, violations } = useSpcStream({
    lineCode: filter.lineCode,
    parameterCode: filter.parameterCode,
    maxPoints: 80,
    maxViolations: 30,
  })

  // 機台篩選只在前端做（hub group 不分機台，因為機台太多會 group 爆炸）；
  // 這個取捨見 docs/realtime-signalr.md。
  const filteredPoints = useMemo(() => {
    if (!filter.toolCode) return points
    return points.filter((p) => p.toolCode === filter.toolCode)
  }, [points, filter.toolCode])
  const filteredViolations = useMemo(() => {
    if (!filter.toolCode) return violations
    return violations.filter((v) => v.toolCode === filter.toolCode)
  }, [violations, filter.toolCode])

  const kpis = useMemo<KpiCardData[]>(() => {
    const yieldKpi = profile.kpi['yield_rate']
    const cpkKpi = profile.kpi['cpk']
    const violationKpi = profile.kpi['violation_today']
    const throughputKpi = profile.kpi['panels_per_hour']

    const latest = filteredPoints.at(-1)
    return [
      {
        key: 'yield_rate',
        config: yieldKpi ?? { labelZh: '良率' },
        value: latest?.value ?? null,
        format: 'percent',
      },
      {
        key: 'cpk',
        config: cpkKpi ?? { labelZh: 'Cpk' },
        value: latest?.cpk ?? null,
        format: 'decimal2',
      },
      {
        key: 'violation_today',
        config: violationKpi ?? { labelZh: '今日違規數' },
        value: filteredViolations.length,
        format: 'int',
      },
      {
        key: 'panels_per_hour',
        config: throughputKpi ?? { labelZh: '每小時產出' },
        value: estimateHourlyThroughput(filteredPoints),
        format: 'int',
        suffix: profile.wording['panel_plural'] ?? '',
      },
    ]
  }, [profile, filteredPoints, filteredViolations])

  return (
    <div
      style={{
        background: '#0d1117',
        color: '#e5e7eb',
        minHeight: 'calc(100vh - 48px)',
        padding: 24,
        fontFamily: 'Noto Sans TC, system-ui, sans-serif',
      }}
    >
      {/* 標題列 */}
      <div
        style={{
          display: 'flex',
          alignItems: 'baseline',
          justifyContent: 'space-between',
          marginBottom: 16,
        }}
      >
        <div>
          <h1 style={{ fontSize: 18, fontWeight: 600, margin: 0 }}>{profile.displayName}</h1>
          <div style={{ color: '#6b7280', fontSize: 12, marginTop: 4 }}>
            {profile.factory.name} · {profile.factory.site} ·
            <span style={{ marginLeft: 6, fontFamily: 'JetBrains Mono, monospace' }}>
              SignalR {labelForState(connectionState)}
            </span>
          </div>
        </div>
        <div style={{ color: '#9ca3af', fontSize: 12 }}>
          資料來源：Kafka <code style={mono}>aoi.inspection.raw</code> → SignalR <code style={mono}>/hubs/spc</code>
        </div>
      </div>

      <KpiBar cards={kpis} />

      <FilterBar
        lines={profile.lines}
        tools={tools}
        parameters={profile.parameters}
        value={filter}
        onChange={setFilter}
      />

      <div style={{ display: 'grid', gridTemplateColumns: '1.6fr 1fr', gap: 16 }}>
        <ControlChartPair
          points={filteredPoints}
          parameterLabel={parameter?.labelZh ?? filter.parameterCode}
          unit={parameter?.unit ?? ''}
          usl={parameter?.usl ?? 1}
          lsl={parameter?.lsl ?? 0}
          target={parameter?.target ?? 0.5}
        />

        <ViolationTable
          rows={filteredViolations}
          parameterLabel={parameter?.labelZh ?? filter.parameterCode}
        />
      </div>

      {filteredPoints.length === 0 && (
        <div
          style={{
            marginTop: 24,
            padding: 16,
            background: '#161b22',
            border: '1px dashed #21262d',
            borderRadius: 8,
            color: '#9ca3af',
            fontSize: 12,
            textAlign: 'center',
          }}
        >
          尚未收到任何 SPC 點。請確認 ingestion 容器與 backend 已啟動，並檢查右上角 SignalR 狀態。
        </div>
      )}
    </div>
  )
}

/**
 * 從 /api/tools 取機台清單，沒有 profile 也能列。
 *
 * 為什麼直接 fetch：
 * - 這是頁面層偶爾用一次的下拉列表，獨立 hook 沒太大價值；
 *   寫在 page 裡少抽象，讀起來最線性。
 */
function useTools(): Tool[] {
  const [tools, setTools] = useState<Tool[]>([])
  useEffect(() => {
    const baseUrl = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:8080'
    const ctrl = new AbortController()
    void (async () => {
      try {
        const res = await fetch(`${baseUrl}/api/tools`, { signal: ctrl.signal })
        if (!res.ok) return
        const json = (await res.json()) as Array<{ toolCode: string; toolName: string | null }>
        setTools(json.map((t) => ({ code: t.toolCode, label: t.toolName ?? '' })))
      } catch {
        // 忽略；下次 effect 會再試
      }
    })()
    return () => ctrl.abort()
  }, [])
  return tools
}

function estimateHourlyThroughput(points: { timestamp: string }[]): number | null {
  if (points.length < 2) return null
  const first = new Date(points[0].timestamp).getTime()
  const last = new Date(points[points.length - 1].timestamp).getTime()
  const hours = Math.max((last - first) / 3_600_000, 1 / 60)
  return Math.round(points.length / hours)
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

const mono: React.CSSProperties = {
  fontFamily: 'JetBrains Mono, monospace',
  background: '#161b22',
  border: '1px solid #21262d',
  padding: '1px 4px',
  borderRadius: 4,
}
