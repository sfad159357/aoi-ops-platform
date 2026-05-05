// LineSearchPage：產線查詢（2D 平面圖）。
//
// 為什麼要做這頁：
// - 使用者希望像 MES 一樣先看工廠平面 → 知道有哪些 line → 點機台看「誰在操作 / 跑哪張板」；
// - 並要求一切資料必須從 SQL Server 真實表撈取，禁止任何 mock/hard coding。
//
// 解決什麼問題：
// - 讓「工單查詢」之外多一個入口，從產線/設備視角理解現場運作狀態。

import { useEffect, useMemo, useState } from 'react'

type ToolTile = {
  toolCode: string
  toolName: string
  toolType: string | null
  status: string | null
  mesState: string
  stationCode: string | null
  lastSeenAt: string | null
  recentTotalCount: number
  recentWarnCount: number
  recentFailCount: number
  currentOperatorCode: string | null
  currentOperatorName: string | null
  currentPanelNo: string | null
  currentLotNo: string | null
  currentResult: string | null
}

type LineTile = {
  lineCode: string
  lineName: string
  tools: ToolTile[]
}

type FactoryFloor = {
  stations: { stationCode: string; stationName: string; seq: number }[]
  lines: LineTile[]
}

type ToolRecentOperator = {
  operatorCode: string
  operatorName: string | null
  lastAt: string
  stationCode: string | null
  panelNo: string | null
  result: string | null
}

type ToolDetail = {
  toolCode: string
  toolName: string
  toolType: string | null
  status: string | null
  lineCode: string | null
  mesState: string
  stationCode: string | null
  lastSeenAt: string | null
  recentTotalCount: number
  recentWarnCount: number
  recentFailCount: number
  currentOperatorCode: string | null
  currentOperatorName: string | null
  currentPanelNo: string | null
  currentLotNo: string | null
  currentResult: string | null
  recentOperators: ToolRecentOperator[]
}

const baseUrl = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:8080'

export default function LineSearchPage() {
  const [floor, setFloor] = useState<FactoryFloor | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [selectedTool, setSelectedTool] = useState<ToolDetail | null>(null)
  const [toolLoading, setToolLoading] = useState(false)
  const [toolError, setToolError] = useState<string | null>(null)

  useEffect(() => {
    const ctrl = new AbortController()
    setLoading(true)
    setError(null)
    void (async () => {
      try {
        const res = await fetch(`${baseUrl}/api/factory/floor`, { signal: ctrl.signal })
        if (!res.ok) throw new Error(`factory floor failed: ${res.status}`)
        const json = (await res.json()) as FactoryFloor
        setFloor(json)
      } catch (e) {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setError(e instanceof Error ? e.message : String(e))
        setFloor(null)
      } finally {
        setLoading(false)
      }
    })()
    return () => ctrl.abort()
  }, [])

  const stations = useMemo(() => floor?.stations ?? [], [floor?.stations])
  const gridLines = useMemo(() => floor?.lines ?? [], [floor?.lines])

  const openTool = (toolCode: string) => {
    const ctrl = new AbortController()
    setToolLoading(true)
    setToolError(null)
    setSelectedTool(null)
    void (async () => {
      try {
        const res = await fetch(`${baseUrl}/api/factory/tools/${encodeURIComponent(toolCode)}`, { signal: ctrl.signal })
        if (!res.ok) throw new Error(`tool detail failed: ${res.status}`)
        const json = (await res.json()) as ToolDetail
        setSelectedTool(json)
      } catch (e) {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setToolError(e instanceof Error ? e.message : String(e))
        setSelectedTool(null)
      } finally {
        setToolLoading(false)
      }
    })()
  }

  return (
    <div style={pageStyle}>
      <div style={headerStyle}>
        <div>
          <h1 style={{ margin: 0, fontSize: 18, fontWeight: 700 }}>產線查詢</h1>
          <div style={{ marginTop: 4, fontSize: 12, color: '#9ca3af' }}>
            2D 平面圖：產線（line_code）→ 機台（tool_code）→ 作業員/在製板（panel_station_log）
          </div>
        </div>
        <button
          onClick={() => window.location.reload()}
          style={refreshBtnStyle}
        >
          重新整理
        </button>
      </div>

      {loading && <div style={{ color: '#9ca3af', fontSize: 12 }}>載入中…</div>}
      {error && <div style={errorStyle}>載入失敗：{error}</div>}

      {!loading && !error && gridLines.length === 0 && (
        <div style={emptyStyle}>目前沒有任何產線資料。</div>
      )}

      <div style={floorGridStyle}>
        {gridLines.map((line) => (
          <section key={line.lineCode} style={lineCardStyle}>
            <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 10 }}>
              <div>
                <div style={{ fontSize: 13, fontWeight: 700, color: '#e5e7eb' }}>{line.lineName}</div>
              </div>
              <div style={{ fontSize: 12, color: '#9ca3af' }}>機台 {line.tools.length}</div>
            </div>

            {/* 依 stations.seq 做流程線排列（像 MES 線體從頭到尾）
                若 stations 主檔沒資料，退回舊版 tool grid，避免全部顯示空白造成誤解。 */}
            {stations.length === 0 ? (
              <div style={toolGridStyle}>
                {line.tools.map((t) => (
                  <button
                    key={t.toolCode}
                    onClick={() => openTool(t.toolCode)}
                    style={{
                      ...toolBtnStyle,
                      borderColor: tileBorder(t),
                      background: tileBg(t),
                    }}
                    title={t.toolName}
                  >
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8 }}>
                      <div style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 12, fontWeight: 700 }}>
                        {t.toolCode}
                      </div>
                      <span style={mesBadgeStyle(t.mesState)}>{mesLabel(t.mesState)}</span>
                    </div>
                    <div style={{ marginTop: 6, fontSize: 12, color: '#cbd5e1', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {t.toolName}
                    </div>
                    <div style={{ marginTop: 6, fontSize: 11, color: '#9ca3af', fontFamily: 'JetBrains Mono, monospace' }}>
                      {t.currentOperatorCode ? `OP=${t.currentOperatorCode}${t.currentOperatorName ? ` ${t.currentOperatorName}` : ''}` : 'OP=—'}
                    </div>
                    <div style={{ marginTop: 4, fontSize: 11, color: '#9ca3af', fontFamily: 'JetBrains Mono, monospace' }}>
                      {t.currentPanelNo ? `P=${t.currentPanelNo}` : 'P=—'}
                    </div>
                    <div style={{ marginTop: 6, display: 'flex', gap: 10, fontSize: 11, color: '#9ca3af', fontFamily: 'JetBrains Mono, monospace' }}>
                      <span>1h={t.recentTotalCount}</span>
                      <span style={{ color: (t.recentTotalCount - t.recentWarnCount - t.recentFailCount) > 0 ? '#22c55e' : '#9ca3af' }}>
                        P={Math.max(0, t.recentTotalCount - t.recentWarnCount - t.recentFailCount)}
                      </span>
                      <span style={{ color: t.recentWarnCount > 0 ? '#f59e0b' : '#9ca3af' }}>W={t.recentWarnCount}</span>
                      <span style={{ color: t.recentFailCount > 0 ? '#ef4444' : '#9ca3af' }}>F={t.recentFailCount}</span>
                    </div>
                  </button>
                ))}
              </div>
            ) : (
              <div style={flowLaneStyle}>
                <div
                  style={{
                    display: 'grid',
                    gridTemplateColumns: `repeat(${stations.length}, minmax(220px, 1fr))`,
                    gap: 10,
                    minWidth: stations.length * 220,
                  }}
                >
                  {stations.map((s) => {
                    const bucket = line.tools.filter((t) => (t.toolType ?? '').toUpperCase() === s.stationCode.toUpperCase())
                    return (
                      <div key={s.stationCode} style={stationColStyle}>
                        <div style={stationTitleStyle}>
                          <span style={{ fontFamily: 'JetBrains Mono, monospace' }}>{s.stationCode}</span>
                          <span style={{ color: '#9ca3af' }}>{s.stationName}</span>
                        </div>
                        {bucket.length === 0 ? (
                          <div style={{ color: '#6b7280', fontSize: 11, padding: '6px 0' }}>—</div>
                        ) : (
                          <div style={{ display: 'grid', gridTemplateColumns: '1fr', gap: 8 }}>
                            {bucket.map((t) => (
                              <button
                                key={t.toolCode}
                                onClick={() => openTool(t.toolCode)}
                                style={{
                                  ...toolBtnStyle,
                                  borderColor: tileBorder(t),
                                  background: tileBg(t),
                                }}
                                title={t.toolName}
                              >
                                <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8 }}>
                                  <div style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 12, fontWeight: 700 }}>
                                    {t.toolCode}
                                  </div>
                                  <span style={mesBadgeStyle(t.mesState)}>{mesLabel(t.mesState)}</span>
                                </div>
                                <div style={{ marginTop: 6, fontSize: 12, color: '#cbd5e1', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                  {t.toolName}
                                </div>
                                <div style={{ marginTop: 6, fontSize: 11, color: '#9ca3af', fontFamily: 'JetBrains Mono, monospace' }}>
                                  {t.currentOperatorCode ? `OP=${t.currentOperatorCode}${t.currentOperatorName ? ` ${t.currentOperatorName}` : ''}` : 'OP=—'}
                                </div>
                                <div style={{ marginTop: 4, fontSize: 11, color: '#9ca3af', fontFamily: 'JetBrains Mono, monospace' }}>
                                  {t.currentPanelNo ? `P=${t.currentPanelNo}` : 'P=—'}
                                </div>
                                <div style={{ marginTop: 6, display: 'flex', gap: 10, fontSize: 11, color: '#9ca3af', fontFamily: 'JetBrains Mono, monospace' }}>
                                  <span>1h={t.recentTotalCount}</span>
                                  <span style={{ color: (t.recentTotalCount - t.recentWarnCount - t.recentFailCount) > 0 ? '#22c55e' : '#9ca3af' }}>
                                    P={Math.max(0, t.recentTotalCount - t.recentWarnCount - t.recentFailCount)}
                                  </span>
                                  <span style={{ color: t.recentWarnCount > 0 ? '#f59e0b' : '#9ca3af' }}>W={t.recentWarnCount}</span>
                                  <span style={{ color: t.recentFailCount > 0 ? '#ef4444' : '#9ca3af' }}>F={t.recentFailCount}</span>
                                </div>
                              </button>
                            ))}
                          </div>
                        )}
                      </div>
                    )
                  })}
                </div>
              </div>
            )}
          </section>
        ))}
      </div>

      {(toolLoading || toolError || selectedTool) && (
        <ToolModal
          loading={toolLoading}
          error={toolError}
          tool={selectedTool}
          onClose={() => {
            setSelectedTool(null)
            setToolError(null)
            setToolLoading(false)
          }}
        />
      )}
    </div>
  )
}

function ToolModal({
  loading,
  error,
  tool,
  onClose,
}: {
  loading: boolean
  error: string | null
  tool: ToolDetail | null
  onClose: () => void
}) {
  return (
    <div style={modalOverlayStyle} onClick={onClose}>
      <div style={modalStyle} onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12 }}>
          <div>
            <div style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 14, fontWeight: 800, color: '#e5e7eb' }}>
              {tool?.toolCode ?? '機台'}
            </div>
            <div style={{ marginTop: 4, fontSize: 12, color: '#9ca3af' }}>{tool?.toolName ?? '—'}</div>
          </div>
          <button onClick={onClose} style={closeBtnStyle}>關閉</button>
        </div>

        {loading && <div style={{ marginTop: 14, fontSize: 12, color: '#9ca3af' }}>載入中…</div>}
        {error && <div style={{ marginTop: 14, ...errorStyle }}>載入失敗：{error}</div>}

        {!loading && !error && tool && (
          <>
            <div style={{ marginTop: 10, fontSize: 11, color: '#9ca3af', lineHeight: 1.5 }}>
              {/* 為什麼要在 modal 補一句定義：
                  - 使用者常把「閒置」誤解成壞掉；其實在這個 demo 裡它代表「最新站別結果不是 in_process」；
                  - 「稼動」則代表「最新站別結果是 in_process（或 exited_at 仍為 NULL 的在製狀態）」。 */}
              <div>
                <span style={{ color: '#e5e7eb', fontWeight: 700 }}>MES狀態定義（簡版）</span>：
                <span style={{ marginLeft: 6 }}>稼動＝最新 log 為在製（in_process）</span>；
                <span style={{ marginLeft: 6 }}>閒置＝最新 log 已結案且結果不是 fail（常見 pass/warn）</span>；
                <span style={{ marginLeft: 6 }}>異常＝最新 log 為 fail/ng</span>；
                <span style={{ marginLeft: 6 }}>離線＝近期沒有任何 station log</span>。
              </div>
            </div>
            <div style={modalGridStyle}>
              <Info label="產線" value={tool.lineCode ?? '-'} mono />
              <Info label="站別" value={tool.stationCode ?? '-'} mono />
              <Info label="狀態" value={tool.status ?? '-'} mono />
              <Info label="MES狀態" value={mesLabel(tool.mesState)} mono />
              <Info label="最後更新" value={tool.lastSeenAt ? formatTime(tool.lastSeenAt) : '-'} mono />
              <Info
                label="作業員"
                value={tool.currentOperatorCode ? `${tool.currentOperatorCode}${tool.currentOperatorName ? ` ${tool.currentOperatorName}` : ''}` : '-'}
                mono
              />
              <Info label="在製板號" value={tool.currentPanelNo ?? '-'} mono />
              <Info label="批次" value={tool.currentLotNo ?? '-'} mono />
              <Info label="結果" value={tool.currentResult ?? '-'} mono />
              <Info label="1h總筆數" value={String(tool.recentTotalCount)} mono />
              <Info
                label="1h Pass（推估）"
                value={String(Math.max(0, tool.recentTotalCount - tool.recentWarnCount - tool.recentFailCount))}
                mono
              />
              <Info label="1h Warn" value={String(tool.recentWarnCount)} mono />
              <Info label="1h Fail" value={String(tool.recentFailCount)} mono />
            </div>

            <div style={{ marginTop: 14 }}>
              <div style={{ fontSize: 12, fontWeight: 700, color: '#e5e7eb', marginBottom: 8 }}>近期操作人員（station log）</div>
              {tool.recentOperators.length === 0 ? (
                <div style={emptyStyle}>尚無紀錄。</div>
              ) : (
                <table style={tableStyle}>
                  <thead>
                    <tr style={{ background: '#111827', color: '#9ca3af' }}>
                      <th style={thStyle}>時間</th>
                      <th style={thStyle}>OP</th>
                      <th style={thStyle}>站別</th>
                      <th style={thStyle}>板號</th>
                      <th style={thStyle}>結果</th>
                    </tr>
                  </thead>
                  <tbody>
                    {tool.recentOperators.map((r, idx) => (
                      <tr key={`${r.operatorCode}-${r.lastAt}-${idx}`} style={{ borderBottom: '1px solid #1f2937' }}>
                        <td style={tdMonoStyle}>{formatTime(r.lastAt)}</td>
                        <td style={tdMonoStyle}>{r.operatorCode}{r.operatorName ? ` ${r.operatorName}` : ''}</td>
                        <td style={tdMonoStyle}>{r.stationCode ?? '-'}</td>
                        <td style={tdMonoStyle}>{r.panelNo ?? '-'}</td>
                        <td style={tdMonoStyle}>{r.result ?? '-'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </>
        )}
      </div>
    </div>
  )
}

function Info({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
      <span style={{ fontSize: 11, color: '#9ca3af' }}>{label}</span>
      <span style={{ fontSize: 13, color: '#e5e7eb', fontFamily: mono ? 'JetBrains Mono, monospace' : undefined }}>
        {value}
      </span>
    </div>
  )
}

function tileBorder(t: ToolTile): string {
  // 為什麼優先用 mesState：
  // - mesState 是後端以 SQL 真實欄位（tools.status + panel_station_log）推導的單一狀態；
  // - 前端不要再靠猜測（例如只看 currentResult）避免與後端規則漂移。
  switch (t.mesState) {
    case 'abnormal':
      return '#ef4444'
    case 'running':
      return '#22c55e'
    case 'idle':
      return '#f59e0b'
    case 'offline':
      return '#374151'
    default:
      return '#243041'
  }
}

function tileBg(t: ToolTile): string {
  switch (t.mesState) {
    case 'abnormal':
      return 'rgba(239,68,68,0.10)'
    case 'running':
      return 'rgba(34,197,94,0.10)'
    case 'idle':
      return 'rgba(245,158,11,0.08)'
    case 'offline':
      return '#0b1220'
    default:
      return '#0b1220'
  }
}

function mesLabel(state: string): string {
  switch (state) {
    case 'running':
      return '稼動'
    case 'idle':
      return '閒置'
    case 'abnormal':
      return '異常'
    case 'offline':
      return '離線'
    default:
      return state || '未知'
  }
}

function mesBadgeStyle(state: string): React.CSSProperties {
  const base: React.CSSProperties = {
    fontSize: 11,
    padding: '1px 6px',
    borderRadius: 999,
    border: '1px solid transparent',
    fontWeight: 700,
    fontFamily: 'Noto Sans TC, system-ui, sans-serif',
  }
  switch (state) {
    case 'running':
      return { ...base, color: '#052e16', background: '#22c55e', borderColor: 'rgba(34,197,94,0.65)' }
    case 'idle':
      return { ...base, color: '#1f1300', background: '#f59e0b', borderColor: 'rgba(245,158,11,0.65)' }
    case 'abnormal':
      return { ...base, color: '#fff', background: '#ef4444', borderColor: 'rgba(239,68,68,0.65)' }
    case 'offline':
      return { ...base, color: '#cbd5e1', background: '#111827', borderColor: 'rgba(148,163,184,0.25)' }
    default:
      return { ...base, color: '#cbd5e1', background: '#111827', borderColor: 'rgba(148,163,184,0.25)' }
  }
}

function formatTime(iso: string): string {
  try {
    return new Date(iso).toLocaleString('zh-TW', { hour12: false })
  } catch {
    return iso
  }
}

const pageStyle: React.CSSProperties = {
  background: '#0d1117',
  color: '#e5e7eb',
  minHeight: 'calc(100vh - 48px)',
  padding: 24,
  fontFamily: 'Noto Sans TC, system-ui, sans-serif',
  fontSize: 16,
}

const headerStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'flex-start',
  justifyContent: 'space-between',
  gap: 12,
  marginBottom: 16,
}

const refreshBtnStyle: React.CSSProperties = {
  background: 'rgba(59,130,246,0.18)',
  border: '1px solid rgba(59,130,246,0.35)',
  color: '#93c5fd',
  borderRadius: 10,
  padding: '8px 12px',
  cursor: 'pointer',
  fontSize: 13,
  fontWeight: 700,
}

const floorGridStyle: React.CSSProperties = {
  display: 'grid',
  // 為什麼改成每條產線一整列：
  // - MES 平面圖/流程圖的視覺通常是一條線一條 lane，垂直堆疊方便比較；
  // - station 流程線需要足夠水平空間，避免擠到看不到機台資訊。
  gridTemplateColumns: '1fr',
  gap: 16,
}

const lineCardStyle: React.CSSProperties = {
  border: '1px solid #243041',
  background: 'rgba(17,24,39,0.55)',
  borderRadius: 14,
  padding: 14,
}

const toolGridStyle: React.CSSProperties = {
  marginTop: 12,
  display: 'grid',
  gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
  gap: 10,
}

const stationColStyle: React.CSSProperties = {
  border: '1px solid #243041',
  background: 'rgba(2,6,23,0.35)',
  borderRadius: 12,
  padding: 10,
  minHeight: 120,
}

const stationTitleStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'baseline',
  justifyContent: 'space-between',
  gap: 8,
  fontSize: 11,
  color: '#cbd5e1',
  paddingBottom: 8,
  borderBottom: '1px solid rgba(148,163,184,0.14)',
  marginBottom: 10,
}

const flowLaneStyle: React.CSSProperties = {
  marginTop: 12,
  overflowX: 'auto',
  paddingBottom: 6,
}

const toolBtnStyle: React.CSSProperties = {
  background: '#0b1220',
  border: '1px solid #243041',
  borderRadius: 12,
  padding: 10,
  cursor: 'pointer',
  color: '#e5e7eb',
}

const modalOverlayStyle: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  background: 'rgba(0,0,0,0.55)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 18,
  zIndex: 500,
}

const modalStyle: React.CSSProperties = {
  width: 'min(920px, 100%)',
  maxHeight: 'min(80vh, 860px)',
  overflow: 'auto',
  background: '#0b1220',
  border: '1px solid rgba(148,163,184,0.25)',
  borderRadius: 14,
  padding: 14,
  boxShadow: '0 18px 60px rgba(0,0,0,0.45)',
}

const closeBtnStyle: React.CSSProperties = {
  background: 'transparent',
  border: '1px solid rgba(148,163,184,0.25)',
  color: '#cbd5e1',
  borderRadius: 10,
  padding: '8px 12px',
  cursor: 'pointer',
  fontSize: 13,
}

const modalGridStyle: React.CSSProperties = {
  marginTop: 14,
  display: 'grid',
  gridTemplateColumns: 'repeat(4, minmax(0, 1fr))',
  gap: 12,
}

const tableStyle: React.CSSProperties = {
  width: '100%',
  borderCollapse: 'collapse',
  fontSize: 13,
}

const thStyle: React.CSSProperties = {
  textAlign: 'left',
  padding: '8px 10px',
  borderBottom: '1px solid #1f2937',
  fontSize: 11,
  fontWeight: 600,
}

const tdMonoStyle: React.CSSProperties = {
  padding: '8px 10px',
  fontFamily: 'JetBrains Mono, monospace',
  fontSize: 12,
}

const errorStyle: React.CSSProperties = {
  padding: 12,
  marginBottom: 12,
  background: '#1f1300',
  border: '1px solid #f59e0b',
  borderRadius: 10,
  color: '#f59e0b',
  fontSize: 12,
}

const emptyStyle: React.CSSProperties = {
  padding: 12,
  background: '#0b1220',
  border: '1px dashed #243041',
  borderRadius: 10,
  color: '#9ca3af',
  fontSize: 12,
  textAlign: 'center',
}

