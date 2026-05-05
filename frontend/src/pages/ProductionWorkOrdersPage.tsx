// ProductionWorkOrdersPage：生產工單（製令）查詢頁。
//
// 為什麼要做這頁：
// - 使用者把「生產工單」視為產線開工主體；但系統事件流多以 lot/panel 粒度落地，
//   因此需要一個聚合視角：production_work_order → lots → panels → station_logs。
//
// 解決什麼問題：
// - 讓使用者可以用模糊搜尋快速定位某張製令，並直接看到生產進度與「目前卡在哪一站」（依站序推導），
//   不必先猜 lot_no 或 panel_no 才查得到資料。

import { Fragment, useEffect, useMemo, useState } from 'react'
import { useProfile } from '../domain/useProfile'

type PanelCurrentStation = {
  stationCode: string
  situation: string
  enteredAt: string | null
  exitedAt: string | null
  result: string | null
  toolCode: string | null
  operator: string | null
  operatorName: string | null
}

type Panel = {
  id: string
  panelNo: string
  lotNo: string
  status: string | null
  currentStation: PanelCurrentStation | null
}

type Lot = {
  id: string
  lotNo: string
  status: string | null
  panels: Panel[]
}

type Progress = {
  lots: number
  panelsTotal: number
  panelsPass: number
  panelsFail: number
  panelsInProgress: number
}

type ProductionWorkOrder = {
  id: string
  workOrderNo: string
  lineCode: string | null
  status: string | null
  productCode: string | null
  plannedQuantity: number | null
  createdAt: string
  progress: Progress
  lots: Lot[]
}

export default function ProductionWorkOrdersPage() {
  const { profile } = useProfile()
  const baseUrl = useMemo(
    () => (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:8080',
    []
  )

  const [items, setItems] = useState<ProductionWorkOrder[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [q, setQ] = useState('')

  useEffect(() => {
    const ctrl = new AbortController()
    void (async () => {
      setLoading(true)
      setError(null)
      try {
        const url = new URL(`${baseUrl}/api/production-work-orders`)
        url.searchParams.set('take', '10')
        if (q.trim()) url.searchParams.set('q', q.trim())
        const res = await fetch(url.toString(), { signal: ctrl.signal })
        if (!res.ok) throw new Error(`production work orders list failed: ${res.status}`)
        const json = (await res.json()) as ProductionWorkOrder[]
        setItems(json)
      } catch (e) {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setError(e instanceof Error ? e.message : String(e))
      } finally {
        setLoading(false)
      }
    })()
    return () => ctrl.abort()
  }, [baseUrl, q])

  return (
    <div style={pageStyle}>
      <div style={headerRowStyle}>
        <div>
          <h1 style={{ margin: 0, fontSize: 18, fontWeight: 600 }}>
            {profile.menus.find((m) => m.id === 'pwo')?.labelZh ?? '工單查詢'}
          </h1>
          <div style={{ color: '#6b7280', fontSize: 12, marginTop: 4 }}>
            來源：SQL <code style={mono}>production_work_order</code> → lots → panels → station_logs
          </div>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <label style={{ color: '#9ca3af', fontSize: 12 }}>模糊搜尋</label>
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="輸入工單號或批次號（例：WO-20260505 / LOT-20260505）"
            style={searchInputStyle}
          />
          <div style={{ color: '#9ca3af', fontSize: 12 }}>{loading ? '讀取中…' : `共 ${items.length} 筆`}</div>
        </div>
      </div>

      {error && <div style={errorStyle}>工單讀取失敗：{error}</div>}

      <div style={tableContainerStyle}>
        <table style={tableStyle}>
          <thead>
            <tr style={{ background: '#161b22', color: '#9ca3af', fontSize: 12 }}>
              <th style={thStyle}>建立時間</th>
              <th style={thStyle}>工單號</th>
              <th style={thStyle}>產線</th>
              <th style={thStyle}>狀態</th>
              <th style={thStyle}>料號</th>
              <th style={thStyle}>計畫量</th>
              <th style={thStyle}>批次</th>
              <th style={thStyle}>板進度</th>
              <th style={thStyle}>完成率</th>
            </tr>
          </thead>
          <tbody>
            {items.map((pwo) => {
              const done = pwo.progress.panelsPass + pwo.progress.panelsFail
              const denom = Math.max(1, pwo.progress.panelsTotal)
              const pct = Math.round((done / denom) * 100)
              return (
                <Fragment key={pwo.id}>
                  <tr style={{ borderBottom: '1px solid #21262d' }}>
                    <td style={tdMonoStyle}>{formatTime(pwo.createdAt)}</td>
                    <td style={tdMonoStyle}>{pwo.workOrderNo}</td>
                    <td style={tdMonoStyle}>{pwo.lineCode ?? '-'}</td>
                    <td style={tdStyle}>{pwo.status ?? '-'}</td>
                    <td style={tdMonoStyle}>{pwo.productCode ?? '-'}</td>
                    <td style={tdMonoStyle}>{pwo.plannedQuantity ?? '-'}</td>
                    <td style={tdStyle}>{pwo.progress.lots}</td>
                    <td style={tdStyle}>
                      <span style={{ color: '#3fb950' }}>PASS {pwo.progress.panelsPass}</span>
                      <span style={{ marginLeft: 8, color: '#f85149' }}>FAIL {pwo.progress.panelsFail}</span>
                      <span style={{ marginLeft: 8, color: '#9ca3af' }}>/ {pwo.progress.panelsTotal}</span>
                    </td>
                    <td style={tdMonoStyle}>{pct}%</td>
                  </tr>
                  <tr style={{ borderBottom: '1px solid #21262d' }}>
                    <td colSpan={9} style={{ padding: '10px 12px 14px', background: '#0b1220' }}>
                      <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap' }}>
                        {pwo.lots.map((lot) => (
                          <div key={lot.id} style={lotCardStyle}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', gap: 10 }}>
                              <div style={{ fontFamily: mono.fontFamily, fontSize: 12, color: '#e5e7eb' }}>
                                {lot.lotNo}
                              </div>
                              <div style={{ fontSize: 12, color: '#9ca3af' }}>{lot.status ?? '-'}</div>
                            </div>
                            <div style={{ fontSize: 11, color: '#6b7280', marginTop: 6, lineHeight: 1.35 }}>
                              板狀態：後端依全站 <code style={monoCodeInline}>panel_station_log</code>{' '}
                              推導（與板號/批次查詢一致）；右欄為依 <code style={monoCodeInline}>stations.seq</code>{' '}
                              流程順序的「目前站點」。
                            </div>
                            <div style={{ marginTop: 8, display: 'grid', gap: 6 }}>
                              {lot.panels.slice(0, 5).map((p) => (
                                <div key={p.id} style={panelRowStyle}>
                                  <div style={{ fontFamily: mono.fontFamily, fontSize: 12 }}>{p.panelNo}</div>
                                  <div style={{ fontSize: 12, color: colorForPanelStatus(p.status) }}>
                                    {p.status ?? '-'}
                                  </div>
                                  <div style={{ fontSize: 12, color: '#9ca3af', textAlign: 'right' }}>
                                    {formatCurrentStation(p.currentStation)}
                                  </div>
                                </div>
                              ))}
                              {lot.panels.length === 0 && (
                                <div style={{ fontSize: 12, color: '#6b7280' }}>尚無板資料</div>
                              )}
                            </div>
                          </div>
                        ))}
                        {pwo.lots.length === 0 && <div style={{ fontSize: 12, color: '#6b7280' }}>尚無批次資料</div>}
                      </div>
                    </td>
                  </tr>
                </Fragment>
              )
            })}
            {items.length === 0 && !loading && (
              <tr>
                <td colSpan={9} style={{ ...tdStyle, textAlign: 'center', color: '#6b7280', padding: 24 }}>
                  沒有符合條件的工單。請先確認 backend 已套用 migrations 並完成 seed。
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function colorForPanelStatus(status: string | null): string {
  const s = (status ?? '').toLowerCase()
  if (s === 'pass') return '#3fb950'
  if (s === 'fail' || s === 'scrap') return '#f85149'
  return '#9ca3af'
}

function formatCurrentStation(c: PanelCurrentStation | null): string {
  if (!c) return '-'
  const station = c.stationCode || '-'
  const tool = c.toolCode ? `@${c.toolCode}` : ''
  const res = c.result ? String(c.result) : ''
  switch (c.situation) {
    case 'pending_entry':
      return `待進 ${station}`
    case 'in_station':
      return `${station}${tool} 站內${res ? ` (${res})` : ''}`.trim()
    case 'fail':
      return `${station}${tool} ${res || 'fail'}`
    case 'hold':
      return `${station}${tool} ${res || '待確認'}`
    case 'completed':
      return `已完成 ${station}${tool}${res ? ` ${res}` : ''}`.trim()
    default:
      return `${station}${tool}${res ? ` ${res}` : ''}`.trim()
  }
}

function formatTime(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())} ${pad2(d.getHours())}:${pad2(d.getMinutes())}`
}

function pad2(n: number): string {
  return String(n).padStart(2, '0')
}

const mono = { fontFamily: 'JetBrains Mono, ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace' }

const monoCodeInline: React.CSSProperties = {
  fontFamily: mono.fontFamily,
  fontSize: 10,
  color: '#9ca3af',
}

const pageStyle: React.CSSProperties = {
  padding: '18px 18px 30px',
  color: '#e5e7eb',
}

const headerRowStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  gap: 18,
  alignItems: 'flex-start',
  marginBottom: 14,
}

const searchInputStyle: React.CSSProperties = {
  width: 380,
  background: '#0d1117',
  border: '1px solid #30363d',
  color: '#e5e7eb',
  borderRadius: 8,
  padding: '8px 10px',
  outline: 'none',
  fontSize: 12,
}

const errorStyle: React.CSSProperties = {
  background: 'rgba(248,81,73,0.12)',
  border: '1px solid rgba(248,81,73,0.35)',
  color: '#fca5a5',
  borderRadius: 10,
  padding: '10px 12px',
  fontSize: 12,
  marginBottom: 12,
}

const tableContainerStyle: React.CSSProperties = {
  border: '1px solid #21262d',
  borderRadius: 12,
  overflow: 'hidden',
  background: '#0d1117',
}

const tableStyle: React.CSSProperties = {
  width: '100%',
  borderCollapse: 'collapse',
}

const thStyle: React.CSSProperties = {
  textAlign: 'left',
  padding: '10px 12px',
  fontWeight: 600,
}

const tdStyle: React.CSSProperties = {
  padding: '10px 12px',
  fontSize: 12,
  color: '#e5e7eb',
  verticalAlign: 'top',
}

const tdMonoStyle: React.CSSProperties = {
  ...tdStyle,
  fontFamily: mono.fontFamily,
}

const lotCardStyle: React.CSSProperties = {
  minWidth: 260,
  maxWidth: 360,
  flex: '1 1 260px',
  border: '1px solid rgba(148,163,184,0.18)',
  borderRadius: 12,
  padding: '10px 10px 8px',
  background: 'rgba(15,23,42,0.35)',
}

const panelRowStyle: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: '1fr 70px 1.2fr',
  gap: 10,
  alignItems: 'center',
  padding: '6px 8px',
  border: '1px solid rgba(148,163,184,0.12)',
  borderRadius: 10,
  background: 'rgba(2,6,23,0.25)',
}

