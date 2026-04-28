// KpiFormulaModal：點擊 KPI 卡後顯示公式定義，可關閉（overlay + 關閉鈕 + Esc）。
//
// 為什麼獨立元件：避免 KpiBar 內混 modal 狀態，父層掌握「目前開哪一個 key」即可。

import { useEffect } from 'react'
import { KPI_FORMULA_BY_KEY, type KpiFormulaBlock } from './kpiFormulas'

type Props = {
  kpiKey: string | null
  onClose: () => void
}

export default function KpiFormulaModal({ kpiKey, onClose }: Props) {
  const block: KpiFormulaBlock | null =
    kpiKey && KPI_FORMULA_BY_KEY[kpiKey] ? KPI_FORMULA_BY_KEY[kpiKey] : null

  useEffect(() => {
    if (!kpiKey) return
    const h = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [kpiKey, onClose])

  if (!kpiKey || !block) return null

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="kpi-formula-title"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 2000,
        background: 'rgba(0,0,0,0.55)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 24,
      }}
      onClick={onClose}
    >
      <div
        style={{
          maxWidth: 480,
          width: '100%',
          background: '#161b22',
          border: '1px solid #30363d',
          borderRadius: 10,
          padding: 20,
          color: '#e5e7eb',
          boxShadow: '0 8px 32px rgba(0,0,0,0.4)',
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 12 }}>
          <h2 id="kpi-formula-title" style={{ margin: 0, fontSize: 16, fontWeight: 600 }}>
            {block.title} — 公式與定義
          </h2>
          <button
            type="button"
            onClick={onClose}
            style={{
              background: '#21262d',
              border: '1px solid #30363d',
              color: '#9ca3af',
              borderRadius: 6,
              padding: '4px 10px',
              cursor: 'pointer',
              fontSize: 12,
            }}
          >
            關閉
          </button>
        </div>
        <ul style={{ margin: '16px 0 0', paddingLeft: 20, lineHeight: 1.7, fontSize: 13, color: '#c9d1d9' }}>
          {block.lines.map((line, i) => (
            <li key={i} style={{ marginBottom: 8 }}>
              {line}
            </li>
          ))}
        </ul>
        <p style={{ margin: '12px 0 0', fontSize: 11, color: '#6b7280' }}>
          實作對應：後端 <code style={{ color: '#8b949e' }}>SpcRulesEngine</code>、前端{' '}
          <code style={{ color: '#8b949e' }}>spcKpiContext.ts</code>。
        </p>
      </div>
    </div>
  )
}
