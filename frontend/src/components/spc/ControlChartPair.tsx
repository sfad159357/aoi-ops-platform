// ControlChartPair：X̄ + R 雙圖共用元件。
//
// 為什麼把兩張圖放同一個元件：
// - HTML 設計圖中 X̄ 與 R 圖永遠成對顯示，且共用同一筆資料來源；
//   分開組裝會導致 prop 重複。
// - X̄ 圖看「均值」，R 圖看「全距 = max - min（n=1 時是兩點移動全距）」；
//   ControlChartPair 內部負責把單點 stream 轉換成兩條曲線。
//
// 解決什麼問題：
// - 從 SignalR 點 stream 直接畫圖，不需要再呼叫後端做計算。
// - 一旦違規（紅 / 黃）會把該點塗對應顏色，立刻看到趨勢。
//
// 刻度統一問題（2026-04-28 修正）：
// - ingestion 送出 yield_rate 在 0~100 範圍（例 97.2），後端 NormalizeYieldToRatio 應轉為 0~1，
//   但若容器未重啟仍可能送 0~100；而 profile 的 usl/lsl 固定在 0~1。
// - 這裡用「最後一點的 value 是否 > 1.5」自動偵測刻度，並把 usl/lsl 換算到與資料同一刻度，
//   避免 Y 軸混用兩套數字造成管制線消失或偏至底部。

import React, { useMemo } from 'react'
import {
  CartesianGrid,
  ComposedChart,
  Line,
  ReferenceLine,
  ResponsiveContainer,
  Scatter,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { SpcPointPayload } from '../../realtime/useSpcStream'

type Props = {
  points: SpcPointPayload[]
  parameterLabel: string
  unit: string
  usl: number
  lsl: number
  target: number
  /**
   * 若後端 Cpk 因刻度不一致而失真，允許由外部傳入修正後的 Cpk。
   * 為什麼需要：後端可能用 usl/lsl(0~1) 對 value(0~100) 算，導致 Cpk 顯示 -40 這種假負值。
   */
  cpkOverride?: number | null
}

/**
 * 用 SignalR 來的 SPC 點畫雙圖。
 *
 * 為什麼用 useMemo 處理資料：
 * - 每來一筆新點都重算整個 R 序列會浪費；
 *   useMemo 只在 points 改變才重算，render 成本可接受。
 */
export default function ControlChartPair({
  points,
  parameterLabel,
  unit,
  usl,
  lsl,
  target,
  cpkOverride = null,
}: Props) {
  // ─── 顯示刻度統一（良率一律用 0~100% 顯示）────────────────────────────
  // 為什麼要做：後端可能推 0~1（已 NormalizeYieldToRatio）或 0~100（容器未重啟），
  // 圖表若直接用原值會讓「良率%」有時顯示 0.97、有時顯示 97，使用者無法判讀。
  const shouldDisplayPercent = unit.includes('%')
  const dataAlreadyPercent = points.length > 0 && points.some((p) => p.value > 1.5)
  const displayScale = shouldDisplayPercent && !dataAlreadyPercent ? 100 : 1

  const chartData = useMemo(() => buildChartData(points, displayScale), [points, displayScale])

  // 最末點取管制線（後端已算好）
  const lastPt = points.at(-1)
  const ucl = (lastPt?.ucl ?? target + (usl - lsl) / 6) * displayScale
  const cl = (lastPt?.cl ?? target) * displayScale
  const lcl = (lastPt?.lcl ?? target - (usl - lsl) / 6) * displayScale
  const cpkText = formatCpk(cpkOverride ?? lastPt?.cpk ?? null)

  // ─── Y 軸 domain（只用資料同一刻度的數值）─────────────────────────────
  // 為什麼要顯式計算：Recharts 預設只以「資料點」決定 domain，
  // ReferenceLine 若落在 domain 外則被 discard，UCL/LCL 就「消失」。
  const yDomainXbar = useMemo((): [number, number] => {
    if (chartData.length === 0) return [lcl, ucl]
    const vals = chartData.map((p) => p.value)
    const lo = Math.min(...vals, lcl, cl, ucl)
    const hi = Math.max(...vals, lcl, cl, ucl)
    const pad = Math.max((hi - lo) * 0.12, 0.002)
    return [lo - pad, hi + pad]
  }, [chartData, ucl, lcl, cl])

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <ChartCard
        title={`X̄ 管制圖 — ${parameterLabel}（${unit}）`}
        subtitle={`UCL ${ucl.toFixed(3)} ｜ CL ${cl.toFixed(3)} ｜ LCL ${lcl.toFixed(3)} ｜ Cpk ${cpkText}`}
        accent="#58a6ff"
      >
        <ResponsiveContainer width="100%" height={260}>
          <ComposedChart data={chartData} margin={{ top: 16, right: 80, bottom: 6, left: 6 }}>
            <CartesianGrid stroke="#21262d" strokeDasharray="3 3" />
            {/* X 軸改用時間（秒級）。為什麼像股票：使用者要看「當下每秒」的變化，而不是第幾點。 */}
            <XAxis
              dataKey="t"
              type="number"
              domain={['dataMin', 'dataMax']}
              scale="time"
              stroke="#6b7280"
              fontSize={11}
              tickFormatter={(ms: number) => {
                try {
                  const d = new Date(ms)
                  return d.toLocaleTimeString('zh-TW', { hour12: false })
                } catch {
                  return String(ms)
                }
              }}
              minTickGap={28}
            />
            {/* Y 軸：最多 3 位小數，避免長串浮點數或舊版顯示整數 */}
            <YAxis
              stroke="#6b7280"
              fontSize={11}
              domain={yDomainXbar}
              width={52}
              allowDataOverflow
              tickFormatter={(v: number) =>
                typeof v === 'number' && isFinite(v)
                  ? parseFloat(v.toFixed(3)).toString()
                  : String(v)
              }
            />
            <Tooltip
              content={({ active, payload }) => (
                <XbarTooltip active={active} payload={payload} unit={unit} />
              )}
            />

            {/* 固定水平管制線（教科書 SPC）：UCL/CL/LCL 只算一次，之後保持水平不變 */}
            <ReferenceLine
              y={ucl}
              ifOverflow="visible"
              stroke="#ef4444"
              strokeWidth={1.5}
              strokeDasharray="6 4"
              label={{ value: 'UCL', fill: '#ef4444', fontSize: 11, position: 'right' }}
            />
            <ReferenceLine y={cl} ifOverflow="visible" stroke="#9ca3af" strokeWidth={1} />
            <ReferenceLine
              y={lcl}
              ifOverflow="visible"
              stroke="#ef4444"
              strokeWidth={1.5}
              strokeDasharray="6 4"
              label={{ value: 'LCL', fill: '#ef4444', fontSize: 11, position: 'right' }}
            />

            <Line
              dataKey="value"
              stroke="#58a6ff"
              strokeWidth={2}
              // 預設每個點都要可見（綠色實心）；違規點再由 Scatter 覆蓋成紅色實心。
              dot={(p: { cx?: number; cy?: number }) => {
                const { cx, cy } = p
                if (!cx || !cy) return null
                return <circle cx={cx} cy={cy} r={3.6} fill="#3fb950" stroke="#0d1117" strokeWidth={1} />
              }}
              isAnimationActive={false}
            />
            <Scatter
              dataKey="violationY"
              shape={(p: { cx?: number; cy?: number; payload?: ChartRow }) => renderDot(p)}
            />
          </ComposedChart>
        </ResponsiveContainer>
      </ChartCard>

      <ChartCard
        title="R 管制圖 — 移動全距"
        subtitle="觀察相鄰量測點的差異，判斷是否變異增大"
        accent="#d2a8ff"
      >
        <ResponsiveContainer width="100%" height={180}>
          <ComposedChart data={chartData} margin={{ top: 10, right: 24, bottom: 0, left: 0 }}>
            <CartesianGrid stroke="#21262d" strokeDasharray="3 3" />
            <XAxis dataKey="idx" stroke="#6b7280" fontSize={11} />
            <YAxis stroke="#6b7280" fontSize={11} domain={[0, 'auto']} />
            <Tooltip
              contentStyle={{
                background: '#0d1117',
                border: '1px solid #21262d',
                fontFamily: 'JetBrains Mono, monospace',
                fontSize: 12,
              }}
              labelFormatter={(label) => {
                // Recharts 會把 XAxis label 傳進來（型別是 ReactNode），但我們的 idx 是數字。
                // 這裡做一次安全轉型，避免 TS 因為簽名不符而擋 build；顯示文字維持「第 N 點」。
                const idx = typeof label === 'number' ? label : Number(label)
                if (Number.isFinite(idx)) return `第 ${idx + 1} 點`
                return String(label ?? '')
              }}
            />
            <Line
              dataKey="movingRange"
              stroke="#d2a8ff"
              strokeWidth={2}
              dot={false}
              isAnimationActive={false}
            />
          </ComposedChart>
        </ResponsiveContainer>
      </ChartCard>
    </div>
  )
}

// ─── 型別 ──────────────────────────────────────────────────────────────

type ChartRow = {
  idx: number
  t: number
  value: number
  ucl: number
  cl: number
  lcl: number
  movingRange: number
  violationColor: string | null
  violationY: number | null
  lotNo: string | null
  waferNo: number | null
}

// ─── Tooltip ──────────────────────────────────────────────────────────

/**
 * Tooltip：顯示批次、板號與數值（與 KPI 追溯同欄位）。
 */
function XbarTooltip({
  active,
  payload,
  unit,
}: {
  active?: boolean
  payload?: ReadonlyArray<{ payload?: ChartRow }>
  unit: string
}) {
  if (!active || !payload?.length) return null
  const d = payload[0].payload
  if (!d) return null
  return (
    <div
      style={{
        background: '#0d1117',
        border: '1px solid #21262d',
        borderRadius: 6,
        padding: '8px 12px',
        fontFamily: 'JetBrains Mono, monospace',
        fontSize: 12,
        color: '#e5e7eb',
      }}
    >
      <div style={{ fontWeight: 600, marginBottom: 4 }}>第 {d.idx + 1} 點</div>
      <div style={{ color: '#9ca3af', fontSize: 11, marginBottom: 4 }}>
        時間：{new Date(d.t).toLocaleTimeString('zh-TW', { hour12: false })}
      </div>
      <div>
        值：{d.value.toFixed(4)} {unit}
      </div>
      <div style={{ marginTop: 4, color: '#9ca3af', fontSize: 11 }}>
        UCL/CL/LCL：{d.ucl.toFixed(3)} / {d.cl.toFixed(3)} / {d.lcl.toFixed(3)}
      </div>
      <div style={{ marginTop: 4, color: '#9ca3af', fontSize: 11 }}>
        批次：{d.lotNo?.trim() || '—'} · 板號：{d.waferNo != null ? d.waferNo : '—'}
      </div>
    </div>
  )
}

// ─── 輔助函式 ──────────────────────────────────────────────────────────

/**
 * 把 stream 轉成 chart row。
 */
function buildChartData(points: SpcPointPayload[], scale: number): ChartRow[] {
  const rows: ChartRow[] = []
  let prev: number | null = null
  for (let i = 0; i < points.length; i++) {
    const p = points[i]
    const v = p.value * scale
    const movingRange = prev === null ? 0 : Math.abs(v - prev)
    const color = pickColor(p)
    const ucl = p.ucl * scale
    const cl = p.cl * scale
    const lcl = p.lcl * scale
    const ts = Number.isFinite(Date.parse(p.timestamp)) ? new Date(p.timestamp).getTime() : i
    rows.push({
      idx: i,
      t: ts,
      value: v,
      ucl,
      cl,
      lcl,
      movingRange,
      violationColor: color,
      violationY: color ? v : null,
      lotNo: p.lotNo ?? null,
      waferNo: p.waferNo ?? null,
    })
    prev = v
  }
  return rows
}

function pickColor(p: SpcPointPayload): string | null {
  // 為什麼不直接看 violations.severity：
  // - OutOfSpec(ruleId=0) 可能因刻度不一致而大量出現「假陽性」，會讓每個點都短暫變紅（你看到的閃爍）。
  // - 這裡改成只認「真實 SPC 違規」：超出 UCL/LCL 或 Nelson Rule（ruleId≥1）。
  const hasNelson = Array.isArray(p.violations) && p.violations.some((v) => v.ruleId >= 1)
  const beyond = (p.value > p.ucl) || (p.value < p.lcl)
  if (beyond) return '#f85149'
  if (hasNelson) {
    // 若只有黃燈規則則顯示黃；若後端有紅燈規則則顯示紅
    const rules = p.violations ?? []
    if (rules.some((v) => v.ruleId >= 1 && v.severity === 'red')) return '#f85149'
    return '#f0b429'
  }
  return null
}

/**
 * Recharts Scatter 自訂點 renderer（違規時才畫）。
 */
function renderDot(props: {
  cx?: number
  cy?: number
  payload?: ChartRow
}): React.ReactElement<SVGElement> {
  const { cx, cy, payload } = props
  if (!cx || !cy || !payload?.violationColor) {
    return <circle cx={0} cy={0} r={0} opacity={0} />
  }
  return (
    <circle
      cx={cx}
      cy={cy}
      r={4.5}
      fill={payload.violationColor}
      stroke="#0d1117"
      strokeWidth={1}
    />
  )
}

// ─── ChartCard ─────────────────────────────────────────────────────────

function ChartCard({
  title,
  subtitle,
  accent,
  children,
}: {
  title: string
  subtitle?: string
  accent: string
  children: React.ReactNode
}) {
  return (
    <div
      style={{
        background: '#161b22',
        border: '1px solid #21262d',
        borderRadius: 8,
        padding: '16px 16px 8px',
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'baseline',
          justifyContent: 'space-between',
          marginBottom: 8,
        }}
      >
        <div>
          <div style={{ color: '#e5e7eb', fontWeight: 600, fontSize: 14 }}>{title}</div>
          {subtitle && (
            <div
              style={{
                color: '#6b7280',
                fontSize: 11,
                marginTop: 2,
                fontFamily: 'JetBrains Mono, monospace',
              }}
            >
              {subtitle}
            </div>
          )}
        </div>
        <span
          style={{
            display: 'inline-block',
            width: 8,
            height: 8,
            borderRadius: '50%',
            background: accent,
            opacity: 0.7,
          }}
        />
      </div>
      {children}
    </div>
  )
}

function formatCpk(cpk: number | null): string {
  if (cpk === null || Number.isNaN(cpk)) return '—'
  return cpk.toFixed(2)
}
