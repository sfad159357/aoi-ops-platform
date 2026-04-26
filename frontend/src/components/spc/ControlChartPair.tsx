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

import { useMemo } from 'react'
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
}: Props) {
  const chartData = useMemo(() => buildChartData(points), [points])
  const ucl = points.at(-1)?.ucl ?? target + (usl - lsl) / 6
  const cl = points.at(-1)?.cl ?? target
  const lcl = points.at(-1)?.lcl ?? target - (usl - lsl) / 6
  const cpkText = formatCpk(points.at(-1)?.cpk ?? null)

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <ChartCard
        title={`X̄ 管制圖 — ${parameterLabel}（${unit}）`}
        subtitle={`UCL ${ucl.toFixed(3)} ｜ CL ${cl.toFixed(3)} ｜ LCL ${lcl.toFixed(3)} ｜ Cpk ${cpkText}`}
        accent="#58a6ff"
      >
        <ResponsiveContainer width="100%" height={260}>
          <ComposedChart data={chartData} margin={{ top: 10, right: 24, bottom: 0, left: 0 }}>
            <CartesianGrid stroke="#21262d" strokeDasharray="3 3" />
            <XAxis dataKey="idx" stroke="#6b7280" fontSize={11} />
            <YAxis stroke="#6b7280" fontSize={11} domain={['auto', 'auto']} />
            <Tooltip
              contentStyle={{
                background: '#0d1117',
                border: '1px solid #21262d',
                fontFamily: 'JetBrains Mono, monospace',
                fontSize: 12,
              }}
              labelFormatter={(idx: number) => `第 ${idx + 1} 點`}
            />
            <ReferenceLine y={usl} stroke="#f85149" strokeDasharray="3 3" label={{ value: 'USL', fill: '#f85149', fontSize: 10 }} />
            <ReferenceLine y={lsl} stroke="#f85149" strokeDasharray="3 3" label={{ value: 'LSL', fill: '#f85149', fontSize: 10 }} />
            <ReferenceLine y={ucl} stroke="#f0b429" strokeDasharray="2 4" label={{ value: 'UCL', fill: '#f0b429', fontSize: 10 }} />
            <ReferenceLine y={cl} stroke="#9ca3af" />
            <ReferenceLine y={lcl} stroke="#f0b429" strokeDasharray="2 4" label={{ value: 'LCL', fill: '#f0b429', fontSize: 10 }} />
            <Line
              dataKey="value"
              stroke="#58a6ff"
              strokeWidth={2}
              dot={false}
              isAnimationActive={false}
            />
            <Scatter dataKey="violationY" shape={(p: { cx?: number; cy?: number; payload?: ChartRow }) => renderDot(p)} />
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
              labelFormatter={(idx: number) => `第 ${idx + 1} 點`}
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

type ChartRow = {
  idx: number
  value: number
  movingRange: number
  violationColor: string | null
  violationY: number | null
}

/**
 * 把 stream 轉成 chart row。
 *
 * 為什麼這裡才算 movingRange：
 * - 後端規則引擎已給 mean/sigma/UCL，但沒給「點對點移動全距」；
 *   前端用「相鄰差絕對值」自己算，省得後端為了畫圖再多送一個欄位。
 */
function buildChartData(points: SpcPointPayload[]): ChartRow[] {
  const rows: ChartRow[] = []
  let prev: number | null = null
  for (let i = 0; i < points.length; i++) {
    const p = points[i]
    const movingRange = prev === null ? 0 : Math.abs(p.value - prev)
    const color = pickColor(p)
    rows.push({
      idx: i,
      value: p.value,
      movingRange,
      violationColor: color,
      violationY: color ? p.value : null,
    })
    prev = p.value
  }
  return rows
}

function pickColor(p: SpcPointPayload): string | null {
  if (p.violations.length === 0) return null
  if (p.violations.some((v) => v.severity === 'red')) return '#f85149'
  if (p.violations.some((v) => v.severity === 'yellow')) return '#f0b429'
  return null
}

/**
 * Recharts Scatter 自訂點 renderer（違規時才畫）。
 */
function renderDot(props: { cx?: number; cy?: number; payload?: ChartRow }): React.ReactElement<SVGElement> {
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
      <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', marginBottom: 8 }}>
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
