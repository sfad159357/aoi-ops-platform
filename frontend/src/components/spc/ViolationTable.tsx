// ViolationTable：SPC 違規事件清單（紅 / 黃燈）。
//
// 為什麼用簡易 table：
// - 違規事件量不大（每天可能幾十筆），沒必要引入 react-table 之類的依賴；
//   原生 table + flex 排版就足夠看清楚資訊。
//
// 解決什麼問題：
// - 配合 SignalR `spcViolation` 事件，讓品保工程師第一時間看到違規；
//   不用一直盯著管制圖找紅點。
//
// 2026-04-28 更新：
// - 遵循「後端規則引擎為權威」：違規事件以後端 payload 的 `violations`（含 OutOfSpec ruleId=0、Nelson ruleId≥1）為準，
//   並以後端輸出的 ucl/lcl 補強「超出管制線」的直覺判定。
// - 說明欄位改寫邏輯：OutOfSpec (ruleId=0) 仍用 ucl/lcl（與 value 同刻度）重述，避免刻度不一致時出現難讀文字。

import React from 'react'
import type { SpcPointPayload, SpcRuleViolation } from '../../realtime/useSpcStream'

type Props = {
  rows: SpcPointPayload[]
  parameterLabel: string
  /**
   * profile 規格上限（0~1 或 0~100 均可，僅用於說明欄位補充資訊）。
   * 為什麼是可選：刻度與 value 可能不同（0~1 vs 0~100），不做數值計算只做顯示。
   */
  usl?: number | null
  /** profile 規格下限 */
  lsl?: number | null
}

export default function ViolationTable({ rows, parameterLabel, usl = null, lsl = null }: Props) {
  // 只顯示「後端判定的違規」：
  // - 主要依 violations（後端規則引擎輸出）
  // - 次要補強：value 超出後端 ucl/lcl（同刻度）
  const displayRows = rows.filter(isBackendViolation)
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
        <div style={{ color: '#6b7280', fontSize: 11 }}>
          顯示 {displayRows.length} 筆（後端規則引擎 violations / UCL/LCL）
        </div>
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
            <th style={th}>產線 / 站別 / 機台</th>
            <th style={th}>批次</th>
            <th style={th}>板號</th>
            <th style={th}>作業員</th>
            <th style={th}>量測值</th>
            <th style={th}>UCL / LCL</th>
            <th style={th}>規則</th>
            <th style={th}>嚴重度</th>
            <th style={th}>說明</th>
          </tr>
        </thead>
        <tbody>
          {displayRows.length === 0 && (
            <tr>
              <td colSpan={10} style={{ color: '#6b7280', textAlign: 'center', padding: '24px 0' }}>
                目前沒有違規事件。
              </td>
            </tr>
          )}
          {displayRows.map((r, i) => (
            <ViolationRow key={`${r.timestamp}-${i}`} row={r} usl={usl} lsl={lsl} />
          ))}
        </tbody>
      </table>
    </div>
  )
}

/**
 * 判斷該點是否為「真實違規」：
 * - 超出管制線（ucl/lcl 與 value 同刻度，由後端視窗計算）
 * - 或有 Nelson Rule（ruleId ≥ 1）違規
 */
function isBackendViolation(row: SpcPointPayload): boolean {
  const v = row.violations ?? []
  if (v.length > 0) return true
  if (row.ucl != null && row.lcl != null && isFinite(row.ucl) && isFinite(row.lcl)) {
    if (row.value > row.ucl || row.value < row.lcl) return true
  }
  return false
}


/**
 * 建立說明文字，讓「超出 USL 1」這種因刻度不一致產生的後端描述變成可讀文字。
 *
 * 規則：
 * 1. 優先顯示 Nelson Rule（ruleId ≥ 1）的說明（由後端產生，語義正確）。
 * 2. OutOfSpec（ruleId=0）→ 用 ucl/lcl（與 value 同刻度）重新描述，加上 USL/LSL 備註。
 *    - value > ucl → 超出管制上限
 *    - value < lcl → 低於管制下限
 *    - value 在管制線內但有 OutOfSpec → 規格能力不足（通常是刻度問題）
 */
function buildDescription(
  violations: SpcRuleViolation[],
  row: SpcPointPayload,
  usl: number | null | undefined,
  lsl: number | null | undefined,
): string {
  // 為什麼優先顯示 ruleId ≥ 1：
  // - Nelson rule 的語義最接近「SPC 規則」本身；OutOfSpec 更像規格/能力提醒。
  const nelsonViol = violations.find((v) => v.ruleId >= 1)
  if (nelsonViol) return nelsonViol.description

  const { value, ucl, lcl } = row

  if (value > ucl) {
    const note = usl != null ? `　USL=${formatSpecInValueScale(value, usl)}` : ''
    return `${value.toFixed(3)} 超出管制上限 UCL ${ucl.toFixed(3)}${note}`
  }
  if (value < lcl) {
    const note = lsl != null ? `　LSL=${formatSpecInValueScale(value, lsl)}` : ''
    return `${value.toFixed(3)} 低於管制下限 LCL ${lcl.toFixed(3)}${note}`
  }

  // 在管制線內但仍有 OutOfSpec：以「實際 USL/LSL（換算到 value 同刻度）」說清楚，不要丟泛用句。
  const hasOos = violations.some((v) => v.ruleId === 0)
  if (hasOos && (usl != null || lsl != null)) {
    const uslText = usl != null ? `USL=${formatSpecInValueScale(value, usl)}` : ''
    const lslText = lsl != null ? `LSL=${formatSpecInValueScale(value, lsl)}` : ''
    const joined = [uslText, lslText].filter(Boolean).join('，')
    return `規格超限（但未超出 UCL/LCL）→ ${joined}`
  }
  return `規格超限（但未超出 UCL/LCL）`
}

/**
 * 把 profile 的 USL/LSL 轉成與 value 同刻度後再顯示。
 * 為什麼需要：yield_rate 可能是 0~1 或 0~100，若直接顯示 USL=1 會讓使用者誤解。
 */
function formatSpecInValueScale(value: number, spec: number): string {
  const valueLooksPercent = value > 1.5
  const specLooksRatio = spec <= 1.5
  const scaled = valueLooksPercent && specLooksRatio ? spec * 100 : spec
  return parseFloat(scaled.toFixed(3)).toString()
}

function ViolationRow({
  row,
  usl,
  lsl,
}: {
  row: SpcPointPayload
  usl: number | null | undefined
  lsl: number | null | undefined
}) {
  const violations = row.violations ?? []

  // 為什麼挑 severity 最重的當代表：
  // - 同一筆點可能同時觸發多條規則，前端顯示一個欄位就好；
  //   紅 > 黃 > 綠，挑最重的最直觀。
  const top = violations.reduce<{ severity: string; rules: string[] }>(
    (acc, v) => {
      acc.rules.push(v.ruleId === 0 ? 'OOS' : `R${v.ruleId}`)
      if (severityRank(v.severity) > severityRank(acc.severity)) acc.severity = v.severity
      return acc
    },
    { severity: 'green', rules: [] },
  )

  // 只顯示非 OutOfSpec 的規則代號（OOS 因刻度問題濾掉）
  const displayRules = violations
    .filter((v) => v.ruleId >= 1)
    .map((v) => `R${v.ruleId}`)
    .join(', ')

  // 若只有 OutOfSpec 且確實超出管制線，顯示 OOS；否則顯示 Nelson 規則
  const ruleDisplay =
    displayRules ||
    (row.value > row.ucl || row.value < row.lcl ? 'OOS' : top.rules.join(', '))

  const color = top.severity === 'red' ? '#f85149' : top.severity === 'yellow' ? '#f0b429' : '#3fb950'

  // 為什麼板號 fallback 顯示 panelNo：
  // - SignalR 已從 ingestion 帶來 panelNo（lot_no-wafer_no），優先用 panelNo 比 waferNo 更貼近 PCB 語意；
  // - waferNo 仍保留作 fallback，避免半導體 profile 沒 panel_no 時就空白。
  const panelText = row.panelNo
    ? row.panelNo
    : row.waferNo != null && !Number.isNaN(row.waferNo)
      ? String(row.waferNo)
      : '—'
  const operatorText = formatOperator(row.operatorCode ?? null, row.operatorName ?? null)

  return (
    <tr style={{ borderTop: '1px solid #21262d' }}>
      <td style={td}>{formatTime(row.timestamp)}</td>
      <td style={td}>
        {row.lineCode} / {row.stationCode ?? '?'} / {row.toolCode}
      </td>
      <td style={td}>{row.lotNo?.trim() || '—'}</td>
      <td style={td}>{panelText}</td>
      <td style={td}>{operatorText}</td>
      <td style={td}>{row.value.toFixed(3)}</td>
      {/* UCL/LCL 欄：讓工程師看到管制線當下的數值，知道是否真的超出 */}
      <td style={{ ...td, color: '#9ca3af', fontSize: 11 }}>
        {row.ucl.toFixed(3)} / {row.lcl.toFixed(3)}
      </td>
      <td style={td}>{ruleDisplay}</td>
      <td style={{ ...td, color }}>{top.severity.toUpperCase()}</td>
      <td style={{ ...td, maxWidth: 280, wordBreak: 'break-word' }}>
        {buildDescription(violations, row, usl, lsl)}
      </td>
    </tr>
  )
}

const th: React.CSSProperties = { padding: '6px 8px', borderBottom: '1px solid #21262d', fontWeight: 500 }
const td: React.CSSProperties = { padding: '6px 8px' }

// 為什麼把 operator helper 寫在這個檔：
// - SPC 是另一個獨立 dashboard，不引用 AlarmsPage 的 helper；
//   保持 component 自包，方便日後拆 monorepo 時不依賴跨頁 import。
function formatOperator(code: string | null, name: string | null): string {
  if (!code && !name) return '—'
  if (code && name) return `${code} ${name}`
  return code ?? name ?? '—'
}

function severityRank(s: string): number {
  switch (s) {
    case 'red': return 3
    case 'yellow': return 2
    case 'green': return 1
    default: return 0
  }
}

function formatTime(iso: string): string {
  try {
    return new Date(iso).toLocaleTimeString('zh-TW', { hour12: false })
  } catch {
    return iso
  }
}
