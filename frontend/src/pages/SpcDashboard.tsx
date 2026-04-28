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

import { useCallback, useEffect, useMemo, useState } from 'react'
import { HubConnectionState } from '@microsoft/signalr'
import { useProfile } from '../domain/useProfile'
import { useSpcStream, type SpcPointPayload } from '../realtime/useSpcStream'
import KpiBar, { type KpiCardData } from '../components/spc/KpiBar'
import KpiFormulaModal from '../components/spc/KpiFormulaModal'
import FilterBar, { type FilterBarValue } from '../components/spc/FilterBar'
import ControlChartPair from '../components/spc/ControlChartPair'
import ViolationTable from '../components/spc/ViolationTable'
import {
  computeObservationWindow,
  computeTodayYieldRate,
  computeCorrectedCpk,
  countTodayViolationQtyInWindow,
  isBackendViolationPoint,
  type ObservationWindowStats,
} from './spcKpiContext'

type Tool = { code: string; label: string }

export default function SpcDashboard() {
  const { profile } = useProfile()
  const [formulaKey, setFormulaKey] = useState<string | null>(null)
  const openFormula = useCallback((key: string) => setFormulaKey(key), [])
  const closeFormula = useCallback(() => setFormulaKey(null), [])

  // 為什麼預設挑第一條線（通常是 SMT-A）：
  // - 我們的 ingestion 會把「站別」(AOI/ICT/...) 與「產線」(SMT-A/SMT-B/...) 分開；
  //   AOI 是 station，不一定會出現在 lineCode。
  // - 若預設選到 AOI-A 但 ingestion 實際推的是 SMT-A/SMT-B，就會造成 JoinGroup 的 lineCode 不一致，
  //   前端看得到畫面但永遠收不到點（最常見的「SPC 沒資料」原因）。
  const defaultLine = useMemo(() => profile.lines[0], [profile.lines])

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

  // 違規事件表：以「圖上的點」為準，避免出現「點已突破 UCL 但 violation stream 沒推」而漏列的情況。
  // 為什麼要合併 violations + points：
  // - violations buffer 代表後端推播的違規事件紀錄（可保留歷史）；
  // - 但若後端因刻度/規則修正或 edge case 沒推 violation，圖上仍可能顯示超出 UCL/LCL，
  //   使用者期待表格要跟圖一致，所以補上 points 視窗內的「真實違規點」。
  const violationEventRows = useMemo(() => {
    // 以後端規則引擎輸出的 violations/UCL/LCL 為權威，確保表格與 KPI 與後端同一套規則。
    const fromPoints = filteredPoints.filter(isBackendViolationPoint)
    const all = [...filteredViolations, ...fromPoints]
    // 去重：同一筆點可能同時在 violations buffer 與 points 內出現
    const seen = new Set<string>()
    const deduped: SpcPointPayload[] = []
    for (const r of all) {
      const key = `${r.timestamp}|${r.lineCode}|${r.toolCode}|${r.parameterCode}`
      if (seen.has(key)) continue
      seen.add(key)
      deduped.push(r)
    }
    // 最新在前，避免表格跳來跳去
    deduped.sort((a, b) => (a.timestamp < b.timestamp ? 1 : a.timestamp > b.timestamp ? -1 : 0))
    return deduped.slice(0, 30)
  }, [filteredPoints, filteredViolations])

  // 今日違規 KPI：與累積、圖表同一 filteredPoints，只計「本日且超出管制線或 Nelson Rule」的件數
  // 今日違規數要與「累積產出」同量綱（件），才能與今日良率一起對帳
  const violationTodayCount = useMemo(() => countTodayViolationQtyInWindow(filteredPoints), [filteredPoints])
  const windowStats = useMemo(() => computeObservationWindow(filteredPoints), [filteredPoints])
  const qtyPerSec =
    windowStats != null && windowStats.pointCount >= 2 ? windowStats.qtyPerSecond : null
  const cumulativeQty = windowStats?.totalInspectedQty ?? 0

  // 今日良率 = (今日累積件 - 今日違規件) / 今日累積件（與違規 KPI 同一分母，可互相對帳）
  const todayYieldRate = useMemo(
    () => computeTodayYieldRate(filteredPoints),
    [filteredPoints],
  )

  // 前端重算 Cpk：後端因 usl/lsl（0~1 比例）vs value（0~100 百分比）刻度不同，Cpk 會算成 −40。
  // 用後端給的 cl（mean）與 sigma（同一刻度），再把 usl/lsl 換算到與 value 相同刻度後重算，即可修正。
  const correctedCpk = useMemo(
    () => computeCorrectedCpk(filteredPoints.at(-1), parameter?.usl ?? 1, parameter?.lsl ?? 0),
    [filteredPoints, parameter],
  )

  // profile 的 good_threshold 仍以「件/小時」填寫；顯示為件/秒 時，達標比輯換算為 threshold/3600
  const panelsPerSecKpiConfig = useMemo(() => {
    const c = profile.kpi['panels_per_hour'] ?? { labelZh: '每秒產出' }
    const t = c.goodThreshold
    return {
      ...c,
      // 文案：使用者要「每秒產出」，不再顯示「每小時」
      labelZh: '每秒產出',
      goodThreshold: t != null && !Number.isNaN(t) ? t / 3600 : null,
    }
  }, [profile])

  const kpis = useMemo<KpiCardData[]>(() => {
    const yieldKpi = profile.kpi['yield_rate']
    const cpkKpi = profile.kpi['cpk']
    const violationKpi = profile.kpi['violation_today']
    const cumulativeKpi = profile.kpi['cumulative_output']

    return [
      {
        key: 'yield_rate',
        config: yieldKpi ?? { labelZh: '良率' },
        // 今日良率 = (今日累積件 − 今日違規件) / 今日累積件；與違規 KPI 同一分母，可相互對帳。
        // 退路：若今日尚無資料，顯示 null（KPI 卡呈現「—」）
        value: todayYieldRate,
        format: 'percent',
      },
      {
        key: 'cpk',
        config: cpkKpi ?? { labelZh: 'Cpk' },
        // 用前端重算的 correctedCpk，避免後端刻度不一致導致顯示 −40 的假負值
        value: correctedCpk,
        format: 'decimal2',
      },
      {
        key: 'violation_today',
        config: violationKpi ?? { labelZh: '今日違規數' },
        value: violationTodayCount,
        format: 'int',
        // 為什麼用「件」：與累積產出單位一致（每點 inspected_qty ≥1，違規點數 ≤ 累積件數），
        // 方便使用者直接比對兩張 KPI 卡，不會誤以為是不同量綱的數字。
        suffix: ' 件',
      },
      {
        key: 'panels_per_hour',
        config: panelsPerSecKpiConfig,
        value: qtyPerSec,
        format: 'decimal3',
        suffix: ' 件/秒',
      },
      {
        key: 'cumulative_output',
        config: cumulativeKpi ?? { labelZh: '累積產出' },
        value: windowStats == null ? null : cumulativeQty,
        format: 'int',
        suffix: ' 件',
      },
    ]
  }, [profile, cumulativeQty, panelsPerSecKpiConfig, qtyPerSec, violationTodayCount, windowStats])

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

      <KpiBar cards={kpis} onCardClick={openFormula} />
      <KpiFormulaModal kpiKey={formulaKey} onClose={closeFormula} />

      {/* 為什麼要獨立一行說明：怕使用者把「良率」與「違規筆數」當成不相干的數字；從 SPC 角度它是同一觀測流上的不同讀法。 */}
      <div
        style={{
          color: '#6b7280',
          fontSize: 11,
          lineHeight: 1.5,
          marginBottom: 12,
          maxWidth: 960,
        }}
      >
        <SpcKpiNarrative
          windowStats={windowStats}
          violationTodayCount={violationTodayCount}
        />
      </div>

      <LatestTraceBar point={filteredPoints.at(-1) ?? null} wording={profile.wording} />

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
          cpkOverride={correctedCpk}
        />

        <ViolationTable
          rows={violationEventRows}
          parameterLabel={parameter?.labelZh ?? filter.parameterCode}
          usl={parameter?.usl}
          lsl={parameter?.lsl}
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

/**
 * 與 KPI 同源的對帳說明：避免使用者以為「累積」與「件/小時」是兩套獨立 demo 數字。
 */
/**
 * 與產線／機台並列顯示「最新一點」的批次、板號，對齊 Kafka 追溯欄位。
 */
function LatestTraceBar({
  point,
  wording,
}: {
  point: SpcPointPayload | null
  wording: Record<string, string>
}) {
  const lotLabel = wording['lot_id_label'] ?? '批次'
  const panelLabel = wording['panel_id_label'] ?? '板號'
  if (!point) {
    return (
      <div
        style={{
          marginBottom: 12,
          padding: '8px 12px',
          background: '#161b22',
          border: '1px dashed #30363d',
          borderRadius: 8,
          fontSize: 12,
          color: '#6b7280',
        }}
      >
        最新觀測追溯：尚無點（無批次／板號）
      </div>
    )
  }
  const lot = point.lotNo?.trim() || '—'
  const wafer = point.waferNo != null && !Number.isNaN(point.waferNo) ? String(point.waferNo) : '—'
  return (
    <div
      style={{
        marginBottom: 12,
        padding: '8px 12px',
        background: '#161b22',
        border: '1px solid #21262d',
        borderRadius: 8,
        fontSize: 12,
        color: '#9ca3af',
        fontFamily: 'JetBrains Mono, ui-monospace, monospace',
      }}
    >
      <span style={{ color: '#6b7280', marginRight: 8 }}>最新觀測追溯</span>
      產線 {point.lineCode} · 機台 {point.toolCode} · {lotLabel} {lot} · {panelLabel} {wafer}
    </div>
  )
}

function SpcKpiNarrative({
  windowStats,
  violationTodayCount,
}: {
  windowStats: ObservationWindowStats | null
  violationTodayCount: number
}) {
  const intro =
    '「觀測點」＝一則檢驗事件在圖上一點（n=1）；累積產出＝各點 inspected_qty 加總；件/秒＝累積÷首末點時間差(秒)。良率＝最右觀測點。'
  if (!windowStats || windowStats.pointCount < 1) {
    return (
      <>
        {intro} 尚無觀測資料。今日違規 {violationTodayCount} 件（與圖、累積同一窗）。
      </>
    )
  }
  const { pointCount: n, totalInspectedQty: sum, hours: h, qtyPerSecond: qps } = windowStats
  const check =
    windowStats.pointCount >= 2
      ? ` 驗算：累積 ${sum} 件、N=${n} 點、Δt=${h.toFixed(2)} h → 約 ${qps.toFixed(3)} 件/秒（= ${(qps * 3600).toFixed(1)} 件/小時）。`
      : ` 僅 1 點：累積 ${sum} 件；需至少 2 點才估算件/秒。`
  return (
    <>
      {intro}
      {check} 今日違規 {violationTodayCount} 件（本窗內超出 UCL/LCL 或 Nelson Rule 的觀測件數，恒 ≤ 累積產出件數）。
    </>
  )
}
