// KpiBar：SPC Dashboard 上方 4 張 KPI 卡。
//
// 為什麼獨立成元件：
// - HTML 設計圖中 KPI 卡是固定 4 格，每格有「label / value / trend」三欄；
//   獨立元件後，未來新增 KPI（例如 OEE）只需要加一個 prop。
// - 顏色 / 達標判定的邏輯封裝起來，頁面層只關心數值。
//
// 解決什麼問題：
// - profile.kpi 設定的 good_threshold / good_threshold_lt 由這個元件解讀，
//   切到不同產業 demo 時不需要改頁面 code。

import type { DomainKpi } from '../../domain/profile'

export type KpiCardData = {
  /** profile.kpi 的 key，例如 yield_rate / cpk / violation_today / panels_per_hour */
  key: string
  /** profile.kpi 中查到的設定（含 labelZh / threshold） */
  config: DomainKpi
  /** 真實或 demo 數值 */
  value: number | null
  /** 數值顯示格式：percent / decimal2 / int，預設 decimal2 */
  format?: 'percent' | 'decimal2' | 'int'
  /** 自訂單位字串，會接在 value 後 */
  suffix?: string
  /** 額外註腳，例如「比昨日 +2%」 */
  caption?: string
}

type Props = {
  cards: KpiCardData[]
}

/**
 * 顯示 4 張 KPI 卡的 grid。
 *
 * 為什麼用 grid-template-columns repeat(4, 1fr)：
 * - HTML 設計圖固定 4 卡，等寬 grid 最簡單；
 *   超過 4 卡時切換成 auto-fit minmax 即可，現在保持精準對齊。
 */
export default function KpiBar({ cards }: Props) {
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(4, 1fr)',
        gap: 16,
        marginBottom: 16,
      }}
    >
      {cards.map((c) => (
        <KpiCard key={c.key} data={c} />
      ))}
    </div>
  )
}

function KpiCard({ data }: { data: KpiCardData }) {
  const { config, value, format = 'decimal2', suffix, caption } = data
  const passed = isPassed(value, config)
  const accent = value === null ? '#9ca3af' : passed ? '#3fb950' : '#f85149'

  return (
    <div
      style={{
        background: '#161b22',
        border: '1px solid #21262d',
        borderRadius: 8,
        padding: 16,
        display: 'flex',
        flexDirection: 'column',
        gap: 8,
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
      <div style={{ color: '#6b7280', fontSize: 11 }}>
        {caption ?? renderThresholdHint(config)}
      </div>
    </div>
  )
}

function formatValue(v: number | null, format: KpiCardData['format']): string {
  if (v === null || Number.isNaN(v)) return '—'
  switch (format) {
    case 'percent':
      return `${(v * 100).toFixed(1)}%`
    case 'int':
      return v.toFixed(0)
    case 'decimal2':
    default:
      return v.toFixed(2)
  }
}

function isPassed(value: number | null, config: DomainKpi): boolean {
  if (value === null || Number.isNaN(value)) return false
  if (config.goodThreshold !== undefined && config.goodThreshold !== null) {
    return value >= config.goodThreshold
  }
  if (config.goodThresholdLt !== undefined && config.goodThresholdLt !== null) {
    return value < config.goodThresholdLt
  }
  return true
}

function renderThresholdHint(config: DomainKpi): string {
  if (config.goodThreshold !== undefined && config.goodThreshold !== null) {
    return `達標：≥ ${config.goodThreshold}`
  }
  if (config.goodThresholdLt !== undefined && config.goodThresholdLt !== null) {
    return `達標：< ${config.goodThresholdLt}`
  }
  return '—'
}
