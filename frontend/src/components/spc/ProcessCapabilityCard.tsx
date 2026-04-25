/**
 * ProcessCapabilityCard（製程能力指數卡片）
 *
 * 為什麼獨立一個元件：
 * - Ca/Cp/Cpk 是 SPC 最常被問到的三個數字，需要一眼能看出好壞。
 * - 顏色判斷邏輯（A+/A/B/C/D 對應綠/藍/黃/橘/紅）集中在這裡，不分散到 Dashboard。
 *
 * 解決什麼問題：
 * - Cpk 業界標準評級需要視覺化（不只顯示數字，還要顯示等級和意義）。
 * - Ca 的正負號有意義（正=均值偏高規格中心，負=偏低），需要特別說明。
 */

import type { ProcessCapability } from '../../api/spc'

// ─── 製程等級顏色對應 ─────────────────────────────────────────────────────
// 依 AIAG 慣例：A+ 最好（綠），D 最差（紅）
const GRADE_STYLE: Record<string, { bg: string; text: string; label: string }> = {
  'A+': { bg: '#14532d', text: '#4ade80', label: '優秀' },
  'A':  { bg: '#1e3a5f', text: '#60a5fa', label: '良好' },
  'B':  { bg: '#713f12', text: '#fbbf24', label: '尚可' },
  'C':  { bg: '#7c2d12', text: '#fb923c', label: '改善' },
  'D':  { bg: '#450a0a', text: '#f87171', label: '危急' },
}

type Props = {
  capability: ProcessCapability
}

export default function ProcessCapabilityCard({ capability: cap }: Props) {
  const grade = GRADE_STYLE[cap.grade] ?? GRADE_STYLE['D']

  return (
    <div style={{
      background: '#111827',
      border: '1px solid #374151',
      borderRadius: 10,
      padding: '16px 20px',
    }}>
      {/* 標題列 */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14 }}>
        <span style={{ fontSize: 14, fontWeight: 700, color: '#e5e7eb' }}>製程能力指數</span>
        {/* 等級徽章 */}
        <span style={{
          background: grade.bg,
          color: grade.text,
          borderRadius: 6,
          padding: '2px 10px',
          fontSize: 13,
          fontWeight: 700,
        }}>
          {cap.grade} — {grade.label}
        </span>
      </div>

      {/* 三大指數列 */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 12, marginBottom: 14 }}>
        <IndexBlock
          label="Cpk"
          value={cap.cpk}
          subtitle="綜合能力（位置＋寬度）"
          highlight
          color={grade.text}
        />
        <IndexBlock
          label="Cp"
          value={cap.cp}
          subtitle="精密度（規格寬度 ÷ 6σ）"
        />
        <IndexBlock
          label="Ca"
          value={cap.ca ?? 0}
          subtitle={`準確度（均值偏離 ${cap.ca !== null && cap.ca > 0 ? '偏高' : '偏低'}）`}
          signed
        />
      </div>

      {/* 詳細數據 */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8, fontSize: 12, color: '#9ca3af' }}>
        <Detail label="Cpu（上側）" value={cap.cpu.toFixed(4)} />
        <Detail label="Cpl（下側）" value={cap.cpl.toFixed(4)} />
        <Detail label="製程均值 X̄" value={cap.mean.toFixed(4)} />
        <Detail label="製程標準差 σ" value={cap.std.toFixed(4)} />
        <Detail label="規格上限 USL" value={String(cap.usl)} />
        <Detail label="規格下限 LSL" value={String(cap.lsl)} />
        {cap.target !== null && <Detail label="目標值" value={String(cap.target)} />}
      </div>

      {/* Cpk 等級對照說明 */}
      <div style={{ marginTop: 14, fontSize: 11, color: '#6b7280', borderTop: '1px solid #1f2937', paddingTop: 10 }}>
        等級：
        <GradeBadge grade="A+" label="≥1.67 優秀" />
        <GradeBadge grade="A"  label="≥1.33 良好" />
        <GradeBadge grade="B"  label="≥1.00 尚可" />
        <GradeBadge grade="C"  label="≥0.67 改善" />
        <GradeBadge grade="D"  label="<0.67 危急" />
      </div>
    </div>
  )
}

// ─── 小子元件 ────────────────────────────────────────────────────────────

function IndexBlock({
  label,
  value,
  subtitle,
  highlight = false,
  color = '#e5e7eb',
  signed = false,
}: {
  label: string
  value: number
  subtitle: string
  highlight?: boolean
  color?: string
  signed?: boolean
}) {
  const displayValue = signed
    ? (value >= 0 ? `+${value.toFixed(4)}` : value.toFixed(4))
    : value.toFixed(4)

  return (
    <div style={{
      background: highlight ? '#1a2035' : '#1f2937',
      borderRadius: 8,
      padding: '10px 12px',
      border: highlight ? `1px solid ${color}40` : '1px solid #374151',
    }}>
      <div style={{ fontSize: 11, color: '#6b7280', marginBottom: 4 }}>{label}</div>
      <div style={{ fontSize: 20, fontWeight: 700, color: highlight ? color : '#e5e7eb', marginBottom: 4 }}>
        {displayValue}
      </div>
      <div style={{ fontSize: 10, color: '#6b7280', lineHeight: 1.3 }}>{subtitle}</div>
    </div>
  )
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: 'flex', justifyContent: 'space-between' }}>
      <span>{label}</span>
      <span style={{ color: '#d1d5db', fontFamily: 'monospace' }}>{value}</span>
    </div>
  )
}

function GradeBadge({ grade, label }: { grade: string; label: string }) {
  const s = GRADE_STYLE[grade]
  return (
    <span style={{
      display: 'inline-block',
      background: s.bg,
      color: s.text,
      borderRadius: 4,
      padding: '1px 6px',
      marginLeft: 4,
      fontSize: 10,
    }}>
      {label}
    </span>
  )
}
