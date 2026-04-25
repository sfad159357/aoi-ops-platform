/**
 * ControlChart（通用 SPC 管制圖元件）
 *
 * 為什麼抽出這個元件：
 * - Xbar-R、I-MR、P 圖、C 圖都有相同的「資料點 + UCL/CL/LCL 參考線 + 違規點高亮」結構。
 * - 抽出共用元件後，各圖表只需傳入不同的資料，不需重複寫 Recharts 的 ReferenceLine 邏輯。
 *
 * 解決什麼問題：
 * - Recharts 的 ReferenceLine 和 Dot 自訂顏色需要 render prop，初學者容易卡住。
 * - 八大規則的違規點需要依嚴重程度顯示不同顏色，這段邏輯集中在這裡管理。
 *
 * 顏色對應：
 * - 規則1（超限）→ 紅色 #ef4444
 * - 規則2/3/5（黃色規則）→ 橘色 #f97316
 * - 規則4/6/7/8（綠色規則）→ 藍色 #3b82f6
 * - 正常點 → 深灰 #374151
 */

import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ReferenceLine,
  ResponsiveContainer,
  Dot,
} from 'recharts'
import type { ChartPoint, ControlLimits, RuleViolation } from '../../api/spc'

// ─── 違規嚴重程度對應顏色 ─────────────────────────────────────────────────

const SEVERITY_COLOR: Record<string, string> = {
  red:    '#ef4444',   // 規則1：超限，最高優先
  yellow: '#f97316',   // 規則2/3/5：趨勢/偏移，高優先
  green:  '#3b82f6',   // 規則4/6/7/8：中/低優先
}

const NORMAL_DOT_COLOR = '#374151'   // 正常點：深灰
const LINE_COLOR = '#6b7280'          // 折線本身：灰色

/**
 * 根據某個點的違規規則清單，判斷應顯示哪個顏色。
 * 優先級：red > yellow > green > 正常
 */
function pointColor(violationRules: number[], allViolations: RuleViolation[]): string {
  if (violationRules.length === 0) return NORMAL_DOT_COLOR

  // 找出這些規則裡最嚴重的 severity
  const severities = violationRules.map((ruleId) => {
    const found = allViolations.find((v) => v.rule_id === ruleId)
    return found?.severity ?? 'green'
  })

  if (severities.includes('red'))    return SEVERITY_COLOR.red
  if (severities.includes('yellow')) return SEVERITY_COLOR.yellow
  return SEVERITY_COLOR.green
}

// ─── Props 型別 ──────────────────────────────────────────────────────────

type ControlChartProps = {
  /** 圖表標題（顯示在左上角） */
  title: string
  /** 資料點序列 */
  points: ChartPoint[]
  /** 管制線（UCL / CL / LCL） */
  limits: ControlLimits
  /** 所有違規記錄（用於顏色判斷） */
  violations: RuleViolation[]
  /** Y 軸標籤 */
  yLabel?: string
  /** 圖表高度（預設 220px） */
  height?: number
  /** Y 軸顯示範圍（若不傳則自動） */
  yDomain?: [number | 'auto', number | 'auto']
  /** 是否顯示 ±1σ / ±2σ 輔助線（預設 true） */
  showSigmaBands?: boolean
}

// ─── 主元件 ─────────────────────────────────────────────────────────────

export default function ControlChart({
  title,
  points,
  limits,
  violations,
  yLabel,
  height = 220,
  yDomain = ['auto', 'auto'],
  showSigmaBands = true,
}: ControlChartProps) {
  // 把 ChartPoint 轉成 Recharts 認識的格式（需要 key 是字串）
  const data = points.map((pt) => ({
    name: String(pt.index + 1),
    value: pt.value,
    violationRules: pt.violation_rules,
  }))

  // 自訂 Dot（依違規顏色上色，並讓違規點稍微大一點）
  const renderDot = (props: {
    cx?: number
    cy?: number
    payload?: { violationRules: number[] }
  }) => {
    const { cx = 0, cy = 0, payload } = props
    const rules = payload?.violationRules ?? []
    const color = pointColor(rules, violations)
    const isViolation = rules.length > 0
    return (
      <Dot
        key={`dot-${cx}-${cy}`}
        cx={cx}
        cy={cy}
        r={isViolation ? 5 : 3}
        fill={color}
        stroke={isViolation ? color : 'transparent'}
        strokeWidth={isViolation ? 2 : 0}
      />
    )
  }

  // Tooltip 內容（顯示值與違規規則說明）
  const CustomTooltip = ({
    active,
    payload,
  }: {
    active?: boolean
    payload?: Array<{ payload: { name: string; value: number; violationRules: number[] } }>
  }) => {
    if (!active || !payload || payload.length === 0) return null
    const d = payload[0].payload
    const rules = d.violationRules
    return (
      <div style={{
        background: '#1f2937',
        border: '1px solid #374151',
        borderRadius: 6,
        padding: '8px 12px',
        fontSize: 13,
        color: '#f9fafb',
        minWidth: 160,
      }}>
        <div style={{ fontWeight: 600, marginBottom: 4 }}>點 #{d.name}</div>
        <div>數值：<span style={{ color: '#60a5fa' }}>{d.value.toFixed(4)}</span></div>
        {rules.length > 0 && (
          <div style={{ marginTop: 4, color: '#fca5a5' }}>
            違規規則：{rules.map((r) => `Rule ${r}`).join(', ')}
          </div>
        )}
      </div>
    )
  }

  return (
    <div style={{ marginBottom: 16 }}>
      <div style={{ fontSize: 13, fontWeight: 600, color: '#9ca3af', marginBottom: 4 }}>
        {title}
        {yLabel && <span style={{ fontWeight: 400, marginLeft: 8 }}>（{yLabel}）</span>}
      </div>
      <ResponsiveContainer width="100%" height={height}>
        <LineChart data={data} margin={{ top: 8, right: 16, left: 8, bottom: 4 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#374151" />
          <XAxis
            dataKey="name"
            tick={{ fontSize: 11, fill: '#9ca3af' }}
            interval="preserveStartEnd"
          />
          <YAxis
            domain={yDomain}
            tick={{ fontSize: 11, fill: '#9ca3af' }}
            width={52}
          />
          <Tooltip content={<CustomTooltip />} />

          {/* UCL 線（紅色虛線） */}
          <ReferenceLine
            y={limits.ucl}
            stroke="#ef4444"
            strokeDasharray="6 3"
            label={{ value: `UCL ${limits.ucl.toFixed(3)}`, position: 'insideTopRight', fontSize: 10, fill: '#ef4444' }}
          />

          {/* CL 線（綠色實線） */}
          <ReferenceLine
            y={limits.cl}
            stroke="#22c55e"
            strokeWidth={1.5}
            label={{ value: `CL ${limits.cl.toFixed(3)}`, position: 'insideTopRight', fontSize: 10, fill: '#22c55e' }}
          />

          {/* LCL 線（紅色虛線） */}
          <ReferenceLine
            y={limits.lcl}
            stroke="#ef4444"
            strokeDasharray="6 3"
            label={{ value: `LCL ${limits.lcl.toFixed(3)}`, position: 'insideBottomRight', fontSize: 10, fill: '#ef4444' }}
          />

          {/* ±2σ 輔助線（橘色虛線） */}
          {showSigmaBands && limits.sigma2 != null && (
            <>
              <ReferenceLine y={limits.sigma2} stroke="#f97316" strokeDasharray="3 4" strokeOpacity={0.6}
                label={{ value: '+2σ', position: 'insideTopRight', fontSize: 9, fill: '#f97316' }} />
              <ReferenceLine y={limits.cl * 2 - limits.sigma2} stroke="#f97316" strokeDasharray="3 4" strokeOpacity={0.6}
                label={{ value: '-2σ', position: 'insideBottomRight', fontSize: 9, fill: '#f97316' }} />
            </>
          )}

          {/* ±1σ 輔助線（藍色虛線） */}
          {showSigmaBands && limits.sigma1 != null && (
            <>
              <ReferenceLine y={limits.sigma1} stroke="#3b82f6" strokeDasharray="2 5" strokeOpacity={0.5}
                label={{ value: '+1σ', position: 'insideTopRight', fontSize: 9, fill: '#3b82f6' }} />
              <ReferenceLine y={limits.cl * 2 - limits.sigma1} stroke="#3b82f6" strokeDasharray="2 5" strokeOpacity={0.5}
                label={{ value: '-1σ', position: 'insideBottomRight', fontSize: 9, fill: '#3b82f6' }} />
            </>
          )}

          {/* 資料折線 */}
          <Line
            type="linear"
            dataKey="value"
            stroke={LINE_COLOR}
            strokeWidth={1.5}
            dot={renderDot}
            activeDot={{ r: 6, fill: '#60a5fa' }}
            isAnimationActive={false}   // 關閉動畫：管制圖通常點很多，動畫反而干擾閱讀
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  )
}
