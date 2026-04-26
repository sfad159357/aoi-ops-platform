// ViolationTable：SPC 違規事件清單（紅 / 黃燈）。
//
// 為什麼用簡易 table：
// - 違規事件量不大（每天可能幾十筆），沒必要引入 react-table 之類的依賴；
//   原生 table + flex 排版就足夠看清楚資訊。
// - 新進來的違規會「閃一下」，給使用者「剛剛收到事件」的視覺提示。
//
// 解決什麼問題：
// - 配合 SignalR `spcViolation` 事件，讓品保工程師第一時間看到違規；
//   不用一直盯著管制圖找紅點。

import type { SpcPointPayload } from '../../realtime/useSpcStream'

type Props = {
  rows: SpcPointPayload[]
  parameterLabel: string
}

export default function ViolationTable({ rows, parameterLabel }: Props) {
  return (
    <div
      style={{
        background: '#161b22',
        border: '1px solid #21262d',
        borderRadius: 8,
        padding: 16,
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'baseline',
          justifyContent: 'space-between',
          marginBottom: 12,
        }}
      >
        <div style={{ color: '#e5e7eb', fontWeight: 600, fontSize: 14 }}>
          違規事件 — {parameterLabel}
        </div>
        <div style={{ color: '#6b7280', fontSize: 11 }}>最新 {rows.length} 筆</div>
      </div>

      <table
        style={{
          width: '100%',
          borderCollapse: 'collapse',
          fontFamily: 'JetBrains Mono, monospace',
          fontSize: 12,
          color: '#e5e7eb',
        }}
      >
        <thead>
          <tr style={{ color: '#9ca3af', textAlign: 'left' }}>
            <th style={th}>時間</th>
            <th style={th}>產線 / 機台</th>
            <th style={th}>量測值</th>
            <th style={th}>規則</th>
            <th style={th}>嚴重度</th>
            <th style={th}>說明</th>
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 && (
            <tr>
              <td colSpan={6} style={{ color: '#6b7280', textAlign: 'center', padding: '24px 0' }}>
                目前沒有違規事件。
              </td>
            </tr>
          )}
          {rows.map((r, i) => (
            <ViolationRow key={`${r.timestamp}-${i}`} row={r} />
          ))}
        </tbody>
      </table>
    </div>
  )
}

function ViolationRow({ row }: { row: SpcPointPayload }) {
  // 為什麼挑 severity 最重的當代表：
  // - 同一筆點可能同時觸發多條規則，前端顯示一個欄位就好；
  //   紅 > 黃 > 綠，挑最重的最直觀。
  const top = row.violations.reduce<{ severity: string; rules: string[]; descs: string[] }>(
    (acc, v) => {
      acc.rules.push(`R${v.ruleId}`)
      acc.descs.push(v.description)
      if (severityRank(v.severity) > severityRank(acc.severity)) acc.severity = v.severity
      return acc
    },
    { severity: 'green', rules: [], descs: [] }
  )

  const color = top.severity === 'red' ? '#f85149' : top.severity === 'yellow' ? '#f0b429' : '#3fb950'
  return (
    <tr style={{ borderTop: '1px solid #21262d' }}>
      <td style={td}>{formatTime(row.timestamp)}</td>
      <td style={td}>
        {row.lineCode} / {row.toolCode}
      </td>
      <td style={td}>{row.value.toFixed(3)}</td>
      <td style={td}>{top.rules.join(', ')}</td>
      <td style={{ ...td, color }}>{top.severity.toUpperCase()}</td>
      <td style={td}>{top.descs[0]}</td>
    </tr>
  )
}

const th: React.CSSProperties = { padding: '6px 8px', borderBottom: '1px solid #21262d', fontWeight: 500 }
const td: React.CSSProperties = { padding: '6px 8px' }

function severityRank(s: string): number {
  switch (s) {
    case 'red':
      return 3
    case 'yellow':
      return 2
    case 'green':
      return 1
    default:
      return 0
  }
}

function formatTime(iso: string): string {
  try {
    return new Date(iso).toLocaleTimeString('zh-TW', { hour12: false })
  } catch {
    return iso
  }
}
