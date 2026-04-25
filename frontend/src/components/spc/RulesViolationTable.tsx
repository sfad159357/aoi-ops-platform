/**
 * RulesViolationTable（SPC 八大規則違規清單）
 *
 * 為什麼要有這個元件：
 * - 管制圖能看到「哪個點」有問題，但 Table 能一眼看到「哪條規則」被觸發了多少次。
 * - 結合圖表與表格，讓 QC 工程師不用猜就能知道「現在最嚴重的問題是什麼規則」。
 *
 * 解決什麼問題：
 * - 八大規則各有對應的嚴重程度；用顏色 badge 區分讓視覺優先級更清楚。
 * - 顯示觸發點的索引，方便回溯原始資料。
 */

import type { RuleViolation } from '../../api/spc'

// 嚴重程度對應顯示設定
const SEVERITY_CONFIG = {
  red:    { emoji: '🔴', label: '最高', bg: '#450a0a', text: '#f87171', border: '#7f1d1d' },
  yellow: { emoji: '🟡', label: '高',   bg: '#451a03', text: '#fbbf24', border: '#78350f' },
  green:  { emoji: '🟢', label: '中/低', bg: '#052e16', text: '#4ade80', border: '#14532d' },
}

// 八大規則完整說明（對應後端 rules.py 的 RULE_DESCRIPTIONS）
const RULE_ACTIONS: Record<number, string> = {
  1: '立即停機檢查，確認設備狀態與材料批次',
  2: '追蹤均值偏移原因：刀具磨損、溫度漂移、批次差異',
  3: '檢查製程趨勢：設備老化、耗材消耗、環境變化',
  4: '檢查過度補償或交互干擾因素',
  5: '確認製程是否出現系統性偏移',
  6: '製程穩定性下降，追蹤 ±1σ 外的變因',
  7: '確認量測系統是否失靈（過度穩定可能是假訊號）',
  8: '製程雙峰分布或來源混合，需分層分析',
}

type Props = {
  violations: RuleViolation[]
  /** 是否顯示觸發點索引（資料點多時可關閉） */
  showPoints?: boolean
}

export default function RulesViolationTable({ violations, showPoints = true }: Props) {
  if (violations.length === 0) {
    return (
      <div style={{
        background: '#052e16',
        border: '1px solid #14532d',
        borderRadius: 8,
        padding: '16px 20px',
        color: '#4ade80',
        fontSize: 14,
        textAlign: 'center',
      }}>
        ✅ 未偵測到八大規則違規，製程狀態正常
      </div>
    )
  }

  // 依嚴重程度排序：red > yellow > green
  const sorted = [...violations].sort((a, b) => {
    const order = { red: 0, yellow: 1, green: 2 }
    return order[a.severity] - order[b.severity]
  })

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div style={{ fontSize: 13, fontWeight: 600, color: '#9ca3af', marginBottom: 4 }}>
        八大規則違規清單（共 {violations.length} 條）
      </div>

      {sorted.map((v) => {
        const cfg = SEVERITY_CONFIG[v.severity]
        return (
          <div
            key={v.rule_id}
            style={{
              background: cfg.bg,
              border: `1px solid ${cfg.border}`,
              borderRadius: 8,
              padding: '12px 16px',
            }}
          >
            {/* 標題列 */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <span style={{ fontSize: 14 }}>{cfg.emoji}</span>
                <span style={{ fontSize: 13, fontWeight: 700, color: cfg.text }}>
                  規則 {v.rule_id}：{v.rule_name}
                </span>
              </div>
              <span style={{
                background: cfg.border,
                color: cfg.text,
                borderRadius: 4,
                padding: '1px 8px',
                fontSize: 11,
                fontWeight: 600,
              }}>
                {cfg.label}優先
              </span>
            </div>

            {/* 建議處理動作 */}
            <div style={{ fontSize: 12, color: '#9ca3af', marginBottom: showPoints ? 6 : 0 }}>
              💡 {RULE_ACTIONS[v.rule_id] ?? '請聯絡製程工程師確認'}
            </div>

            {/* 觸發點索引 */}
            {showPoints && (
              <div style={{ fontSize: 11, color: '#6b7280' }}>
                觸發點：{v.points.slice(0, 20).map((p) => `#${p + 1}`).join(', ')}
                {v.points.length > 20 && ` … 共 ${v.points.length} 點`}
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}
