// KpiBar：SPC Dashboard 上方 KPI 卡（多欄響應式）。
//
// 為什麼獨立成元件：
// - HTML 設計圖中 KPI 卡是固定 4 格，每格有「label / value / trend」三欄；
//   獨立元件後，未來新增 KPI（例如 OEE）只需要加一個 prop。
// - 顏色 / 達標判定的邏輯封裝起來，頁面層只關心數值。
//
// 解決什麼問題：
// - profile.kpi 設定的 good_threshold / good_threshold_lt 由這個元件解讀，
//   切到不同產業 demo 時不需要改頁面 code。
// - definitionZh 可寫清指標公式（例如總累積計算量 N＝|P|），優先於「達標」提示顯示。

import type { DomainKpi } from '../../domain/profile'

export type KpiCardData = {
  /** profile.kpi 的 key，例如 yield_rate / cpk / violation_today / panels_per_hour / cumulative_output */
  key: string
  /** profile.kpi 中查到的設定（含 labelZh / threshold） */
  config: DomainKpi
  /** 真實或 demo 數值 */
  value: number | null
  /** 數值顯示格式：percent / decimal1 / decimal2 / decimal3 / int，預設 decimal2 */
  format?: 'percent' | 'decimal1' | 'decimal2' | 'decimal3' | 'int'
  /** 自訂單位字串，會接在 value 後 */
  suffix?: string
  /** 額外註腳，例如「比昨日 +2%」 */
  caption?: string
}

type Props = {
  cards: KpiCardData[]
  /** 點擊卡片查看公式定義（modal 由父層渲染） */
  onCardClick?: (key: string) => void
}

/**
 * 顯示 KPI 卡的 grid。
 *
 * 為什麼用 auto-fit + minmax：
 * - SPC 頁可能 5 張（良率／Cpk／違規／每小時／累積），窄螢幕需自動換行；
 *   minmax 避免卡片被壓到不可讀。
 */
export default function KpiBar({ cards, onCardClick }: Props) {
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(auto-fit, minmax(158px, 1fr))',
        gap: 16,
        marginBottom: 16,
      }}
    >
      {cards.map((c) => (
        <KpiCard key={c.key} data={c} onCardClick={onCardClick} />
      ))}
    </div>
  )
}

function KpiCard({
  data,
  onCardClick,
}: {
  data: KpiCardData
  onCardClick?: (key: string) => void
}) {
  const { config, value, format = 'decimal2', suffix, caption } = data
  const passed = isPassed(value, config, format)
  const accent = value === null ? '#9ca3af' : passed ? '#3fb950' : '#f85149'
  const clickable = typeof onCardClick === 'function'

  return (
    <div
      role={clickable ? 'button' : undefined}
      tabIndex={clickable ? 0 : undefined}
      onKeyDown={
        clickable
          ? (e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault()
                onCardClick(data.key)
              }
            }
          : undefined
      }
      onClick={clickable ? () => onCardClick(data.key) : undefined}
      style={{
        background: '#161b22',
        border: '1px solid #21262d',
        borderRadius: 8,
        padding: 16,
        display: 'flex',
        flexDirection: 'column',
        gap: 8,
        cursor: clickable ? 'pointer' : 'default',
        outline: 'none',
      }}
    >
      <div style={{ color: '#9ca3af', fontSize: 12, letterSpacing: 0.5 }}>
        {config.labelZh}
      </div>
      <div
        style={{
          display: 'flex',
          alignItems: 'baseline',
          gap: 6,
          fontFamily: 'JetBrains Mono, ui-monospace, SFMono-Regular, monospace',
        }}
      >
        <span style={{ fontSize: 28, fontWeight: 600, color: accent }}>
          {formatValue(value, format)}
        </span>
        {suffix && <span style={{ color: '#6b7280', fontSize: 13 }}>{suffix}</span>}
      </div>
      <div style={{ color: '#6b7280', fontSize: 11, lineHeight: 1.45 }}>
        {renderKpiFootnote(caption, config, format)}
        {clickable && (
          <span style={{ display: 'block', marginTop: 4, color: '#484f58', fontSize: 10 }}>點擊看公式定義</span>
        )}
      </div>
    </div>
  )
}

/**
 * 將良率等「可能是 0~1 或 0~100」的欄位轉成 0~1，再顯示為百分比。
 * 為什麼在本地也做一次：歷史 SignalR 點或舊版後端可能已混刻度；轉完再 clamp 絕不會顯示 &gt;100%。
 */
function toPercentDisplayRatio(v: number): number {
  const r = v > 1 ? v / 100 : v
  return Math.min(1, Math.max(0, r))
}

function formatValue(v: number | null, format: KpiCardData['format']): string {
  if (v === null || Number.isNaN(v)) return '—'
  switch (format) {
    case 'percent':
      return `${(toPercentDisplayRatio(v) * 100).toFixed(1)}%`
    case 'decimal1':
      return v.toFixed(1)
    case 'decimal3':
      return v.toFixed(3)
    case 'int':
      return v.toFixed(0)
    case 'decimal2':
    default:
      return v.toFixed(2)
  }
}

function isPassed(
  value: number | null,
  config: DomainKpi,
  format: KpiCardData['format'] = 'decimal2',
): boolean {
  if (value === null || Number.isNaN(value)) return false
  const v =
    format === 'percent' ? toPercentDisplayRatio(value) : value
  if (config.goodThreshold !== undefined && config.goodThreshold !== null) {
    return v >= config.goodThreshold
  }
  if (config.goodThresholdLt !== undefined && config.goodThresholdLt !== null) {
    return v < config.goodThresholdLt
  }
  return true
}

/**
 * 底欄的達標提示要跟卡片格式一致。
 * - percent：把 0~1 閾值顯示成 95%（不要顯示 0.95）
 * - decimal3：固定 3 位（每秒產出）
 */
function renderKpiFootnote(
  caption: string | undefined,
  config: DomainKpi,
  format: KpiCardData['format'] = 'decimal2',
): string {
  if (caption) return caption
  const def = config.definitionZh?.trim()
  if (def) return def
  return renderThresholdHint(config, format)
}

function renderThresholdHint(config: DomainKpi, format: KpiCardData['format'] = 'decimal2'): string {
  if (config.goodThreshold !== undefined && config.goodThreshold !== null) {
    return `達標：≥ ${formatThreshold(config.goodThreshold, format)}`
  }
  if (config.goodThresholdLt !== undefined && config.goodThresholdLt !== null) {
    return `達標：< ${formatThreshold(config.goodThresholdLt, format)}`
  }
  return '—'
}

function formatThreshold(v: number, format: KpiCardData['format']): string {
  if (Number.isNaN(v)) return '—'
  switch (format) {
    case 'percent': {
      // goodThreshold 在 profile 以 0~1 存（例如 0.95），達標提示要顯示 95%
      const r = Math.min(1, Math.max(0, v))
      return `${(r * 100).toFixed(0)}%`
    }
    case 'decimal3':
      return v.toFixed(3)
    case 'decimal1':
      return v.toFixed(1)
    case 'int':
      return v.toFixed(0)
    case 'decimal2':
    default:
      return v.toFixed(2)
  }
}
