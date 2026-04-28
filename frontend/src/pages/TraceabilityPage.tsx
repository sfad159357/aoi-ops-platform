// TraceabilityPage：物料追溯頁。
//
// 為什麼這頁要做：
// - 對齊 HTML Tab 2 的「板資訊 + 6 站時間軸 + 物料批號 + 同批次列表」；
//   工程師最常追溯：「這張板出問題 → 哪一站異常 → 用了哪批物料 → 同批還有哪些板」。
//
// 為什麼整頁打一支 API：
// - 後端 TraceController 已經把 4 段資料 join 完一次回傳，
//   前端不需要管多載入狀態與 race condition。
//
// 解決什麼問題：
// - 把問題定位 → 影響評估的 workflow 收進單一頁面，
//   面試 demo 與真實品質工程師日常都能立刻使用。

import { useEffect, useMemo, useState } from 'react'
import { useProfile } from '../domain/useProfile'
import {
  fetchPanelTrace,
  fetchRecentPanels,
  type PanelTrace,
  type RelatedPanel,
  type StationLog,
} from '../api/trace'

export default function TraceabilityPage() {
  const { profile } = useProfile()

  const [panelNoInput, setPanelNoInput] = useState<string>('')
  const [recent, setRecent] = useState<RelatedPanel[]>([])
  const [recentQuery, setRecentQuery] = useState<string>('')
  const [trace, setTrace] = useState<PanelTrace | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // 為什麼一進頁面就先撈 recent panels：
  // - demo 觀眾不知道 panelNo 長什麼樣，把最近 20 張板列出來，
  //   點一下就能看到完整 timeline，比要求他們手動輸入更友善。
  useEffect(() => {
    const ctrl = new AbortController()
    void (async () => {
      try {
        const list = await fetchRecentPanels(20, ctrl.signal)
        setRecent(list)
        if (!panelNoInput && list[0]) {
          setPanelNoInput(list[0].panelNo)
        }
      } catch (e) {
        if (e instanceof DOMException && e.name === 'AbortError') return
        console.warn('[TraceabilityPage] 撈最近板失敗：', e)
      }
    })()
    return () => ctrl.abort()
    // 為什麼依賴空陣列：
    // - 最近板列表只在頁面初次載入時撈一次；之後 input 變動不應重抓。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // 為什麼要做模糊搜尋：
  // - 追溯時常見情境是「拿到一串板號/批號碎片」就想快速定位；
  // - 不強制精準匹配，能讓工程師用 key-in 立刻縮小候選清單。
  const filteredRecent = useMemo(() => {
    const q = recentQuery.trim().toLowerCase()
    if (!q) return recent
    return recent.filter((r) => {
      const a = r.panelNo.toLowerCase()
      const b = (r.lotNo ?? '').toLowerCase()
      return a.includes(q) || b.includes(q)
    })
  }, [recent, recentQuery])

  // 為什麼物料預設最新在上：
  // - 真實追溯通常先看「最近一筆用料/到貨」來判斷是否是同一批異常擴散；
  // - 新 → 舊排列可讓使用者第一眼看到最新批號。
  const sortedMaterials = useMemo(() => {
    const list = trace?.materials ?? []
    return [...list].sort((a, b) => {
      const ta = a.receivedAt ? new Date(a.receivedAt).getTime() : 0
      const tb = b.receivedAt ? new Date(b.receivedAt).getTime() : 0
      if (ta !== tb) return tb - ta
      return (b.materialLotNo ?? '').localeCompare(a.materialLotNo ?? '')
    })
  }, [trace?.materials])

  // 為什麼把 fetch trace 拆成 useEffect 依 panelNoInput：
  // - 當使用者點選 recent panel 或敲 Enter 觸發查詢，效果一致；
  //   不必再多寫一個 onSubmit 流程。
  useEffect(() => {
    if (!panelNoInput.trim()) {
      setTrace(null)
      return
    }
    const ctrl = new AbortController()
    setLoading(true)
    setError(null)
    void (async () => {
      try {
        const data = await fetchPanelTrace(panelNoInput.trim(), ctrl.signal)
        setTrace(data)
      } catch (e) {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setError(e instanceof Error ? e.message : String(e))
        setTrace(null)
      } finally {
        setLoading(false)
      }
    })()
    return () => ctrl.abort()
  }, [panelNoInput])

  // 為什麼 timeline 以 profile.stations 為主：
  // - 後端可能還沒寫齊 6 站事件，但 UI 應該總是顯示完整 6 個格子（缺資料的灰底）；
  //   先列 profile.stations 全表，再把後端事件 map 上去。
  const timeline = useMemo(() => {
    const stationsByCode = new Map(
      (trace?.stations ?? []).map((s) => [s.stationCode, s])
    )
    return profile.stations.map((s) => ({
      code: s.code,
      label: s.labelZh,
      seq: s.seq,
      log: stationsByCode.get(s.code) ?? null,
    }))
  }, [profile.stations, trace?.stations])

  return (
    <div style={pageStyle}>
      <div style={headerRowStyle}>
        <div>
          <h1 style={{ margin: 0, fontSize: 18, fontWeight: 600 }}>
            {profile.menus.find((m) => m.id === 'trace')?.labelZh ?? '物料追溯查詢'}
          </h1>
          <div style={{ color: '#6b7280', fontSize: 12, marginTop: 4 }}>
            掃 QR Code 或選最近{profile.entities.panel.labelZh}：6 站時間軸 + 物料批號 + 同批次{profile.entities.panel.labelZh}
          </div>
        </div>
      </div>

      {/* 查詢列 */}
      <div style={searchRowStyle}>
        <label style={{ fontSize: 12, color: '#9ca3af' }}>
          {profile.entities.panel.labelZh} No
        </label>
        <input
          value={panelNoInput}
          onChange={(e) => setPanelNoInput(e.target.value)}
          placeholder="例如 PCB-20240422-LOT-001-1"
          style={inputStyle}
        />
        <input
          value={recentQuery}
          onChange={(e) => setRecentQuery(e.target.value)}
          placeholder="模糊搜尋（板號 / 批次）"
          style={searchInputStyle}
        />
        {recent.length > 0 && (
          <select
            value={panelNoInput}
            onChange={(e) => setPanelNoInput(e.target.value)}
            style={selectStyle}
          >
            <option value="">— 最近 {recent.length} 張 —</option>
            {filteredRecent.map((r) => (
              <option key={r.panelNo} value={r.panelNo}>
                {r.panelNo}（lot={r.lotNo}）
              </option>
            ))}
          </select>
        )}
      </div>

      {loading && <div style={{ color: '#9ca3af', fontSize: 12 }}>載入中…</div>}
      {error && <div style={errorStyle}>查無資料或查詢失敗：{error}</div>}

      {trace && (
        <>
          {/* 板資訊 */}
          <section style={sectionStyle}>
            <h2 style={sectionTitleStyle}>{profile.entities.panel.labelZh} 資訊</h2>
            <div style={infoGridStyle}>
              <InfoCell label={`${profile.entities.panel.labelZh} No`} value={trace.panel.panelNo} mono />
              <InfoCell label={profile.entities.lot.labelZh} value={trace.panel.lotNo} mono />
              <InfoCell label="狀態" value={trace.panel.status ?? '-'} />
              <InfoCell label="建立時間" value={formatTime(trace.panel.createdAt)} mono />
            </div>
          </section>

          {/* 6 站時間軸 */}
          <section style={sectionStyle}>
            <h2 style={sectionTitleStyle}>站別時間軸（{profile.stations.length} 站）</h2>
            <div style={timelineGridStyle}>
              {timeline.map((t, i) => (
                <StationCard key={t.code} step={i + 1} code={t.code} label={t.label} log={t.log} />
              ))}
            </div>
          </section>

          {/* 物料批號 */}
          <section style={sectionStyle}>
            <h2 style={sectionTitleStyle}>使用物料批號</h2>
            {sortedMaterials.length === 0 ? (
              <div style={emptyStyle}>沒有任何物料記錄。</div>
            ) : (
              <table style={tableStyle}>
                <thead>
                  <tr style={{ background: '#161b22', color: '#9ca3af' }}>
                    <th style={thStyle}>物料類型</th>
                    <th style={thStyle}>批號</th>
                    <th style={thStyle}>名稱</th>
                    <th style={thStyle}>供應商</th>
                    <th style={thStyle}>到貨日</th>
                    <th style={thStyle}>用量</th>
                  </tr>
                </thead>
                <tbody>
                  {sortedMaterials.map((m) => (
                    <tr key={m.materialLotNo} style={{ borderBottom: '1px solid #21262d' }}>
                      <td style={tdMonoStyle}>{m.materialType}</td>
                      <td style={tdMonoStyle}>{m.materialLotNo}</td>
                      <td style={tdStyle}>{m.materialName ?? '-'}</td>
                      <td style={tdStyle}>{m.supplier ?? '-'}</td>
                      <td style={tdMonoStyle}>{m.receivedAt ? formatTime(m.receivedAt) : '-'}</td>
                      <td style={tdMonoStyle}>{m.quantity ?? '-'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>

          {/* 同批次 / 同物料 */}
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
            <section style={sectionStyle}>
              <h2 style={sectionTitleStyle}>同 {profile.entities.lot.labelZh} {profile.entities.panel.labelZh}（{trace.sameLotPanels.length} 張）</h2>
              <RelatedTable rows={trace.sameLotPanels} onSelect={setPanelNoInput} />
            </section>
            <section style={sectionStyle}>
              <h2 style={sectionTitleStyle}>同物料 {profile.entities.panel.labelZh}（{trace.sameMaterialPanels.length} 張，最多 50 筆）</h2>
              <RelatedTable rows={trace.sameMaterialPanels} onSelect={setPanelNoInput} />
            </section>
          </div>
        </>
      )}

      {!loading && !trace && !error && (
        <div style={emptyStyle}>請輸入 / 選擇{profile.entities.panel.labelZh} No 開始查詢。</div>
      )}
    </div>
  )
}

/**
 * 站別卡片：對齊 HTML 設計圖的 6 個 step。
 *
 * 為什麼把卡片獨立：
 * - timeline 一格一格的視覺包含「step 編號 + 站別中文 + 結果 badge + 時間」，
 *   抽成元件後，未來要加圖示（例如「進行中」icon）只改一處。
 */
function StationCard({
  step,
  code,
  label,
  log,
}: {
  step: number
  code: string
  label: string
  log: StationLog | null
}) {
  const result = (log?.result ?? '').toLowerCase()
  const palette = resultPalette(result, !!log)
  return (
    <div
      style={{
        background: '#161b22',
        border: `1px solid ${palette.border}`,
        borderRadius: 8,
        padding: 12,
        display: 'flex',
        flexDirection: 'column',
        gap: 6,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <span style={{ color: '#6b7280', fontSize: 11, fontFamily: 'JetBrains Mono, monospace' }}>
          STEP {step}
        </span>
        <span
          style={{
            background: palette.badgeBg,
            color: palette.badgeFg,
            fontSize: 10,
            padding: '1px 6px',
            borderRadius: 4,
            fontFamily: 'JetBrains Mono, monospace',
            fontWeight: 600,
            textTransform: 'uppercase',
          }}
        >
          {log?.result ?? '無資料'}
        </span>
      </div>
      <div style={{ fontSize: 14, fontWeight: 600, color: '#e5e7eb' }}>{label}</div>
      <div style={{ fontSize: 11, color: '#6b7280', fontFamily: 'JetBrains Mono, monospace' }}>{code}</div>
      {log && (
        <>
          <div style={{ fontSize: 11, color: '#9ca3af', fontFamily: 'JetBrains Mono, monospace' }}>
            進站：{formatTime(log.enteredAt)}
          </div>
          <div style={{ fontSize: 11, color: '#9ca3af', fontFamily: 'JetBrains Mono, monospace' }}>
            出站：{log.exitedAt ? formatTime(log.exitedAt) : '進行中'}
          </div>
          {/* 為什麼把作業員 / 機台補在卡片下半：
              - 物料追溯查詢頁需要看「誰在哪台機跑這站」才能對接責任歸屬，
                而 panel_station_log 已冗餘 operatorName / toolCode。 */}
          {(log.operator || log.operatorName) && (
            <div style={{ fontSize: 11, color: '#9ca3af', fontFamily: 'JetBrains Mono, monospace' }}>
              作業員：{log.operator ?? '-'}{log.operatorName ? ` ${log.operatorName}` : ''}
            </div>
          )}
          {log.toolCode && (
            <div style={{ fontSize: 11, color: '#9ca3af', fontFamily: 'JetBrains Mono, monospace' }}>
              機台：{log.toolCode}
            </div>
          )}
          {log.note && (
            <div style={{ fontSize: 11, color: '#f0b429', marginTop: 4 }}>{log.note}</div>
          )}
        </>
      )}
    </div>
  )
}

function RelatedTable({ rows, onSelect }: { rows: RelatedPanel[]; onSelect: (panelNo: string) => void }) {
  if (rows.length === 0) return <div style={emptyStyle}>無</div>
  return (
    <table style={tableStyle}>
      <thead>
        <tr style={{ background: '#161b22', color: '#9ca3af' }}>
          <th style={thStyle}>板號</th>
          <th style={thStyle}>批次</th>
          <th style={thStyle}>狀態</th>
          <th style={thStyle}></th>
        </tr>
      </thead>
      <tbody>
        {rows.map((r) => (
          <tr key={r.panelNo} style={{ borderBottom: '1px solid #21262d' }}>
            <td style={tdMonoStyle}>{r.panelNo}</td>
            <td style={tdMonoStyle}>{r.lotNo}</td>
            <td style={tdStyle}>{r.status ?? '-'}</td>
            <td style={tdStyle}>
              <button
                onClick={() => onSelect(r.panelNo)}
                style={linkBtnStyle}
              >
                查看 →
              </button>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function InfoCell({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
      <span style={{ fontSize: 11, color: '#6b7280' }}>{label}</span>
      <span style={{ fontSize: 13, color: '#e5e7eb', fontFamily: mono ? 'JetBrains Mono, monospace' : undefined }}>
        {value}
      </span>
    </div>
  )
}

function resultPalette(result: string, hasLog: boolean): { border: string; badgeBg: string; badgeFg: string } {
  if (!hasLog) return { border: '#21262d', badgeBg: '#374151', badgeFg: '#9ca3af' }
  switch (result) {
    case 'pass':
      return { border: '#3fb950', badgeBg: '#3fb950', badgeFg: '#0d1117' }
    case 'warn':
      return { border: '#f0b429', badgeBg: '#f0b429', badgeFg: '#1f1300' }
    case 'fail':
      return { border: '#f85149', badgeBg: '#f85149', badgeFg: '#fff' }
    case 'skip':
      return { border: '#6b7280', badgeBg: '#374151', badgeFg: '#e5e7eb' }
    default:
      return { border: '#58a6ff', badgeBg: '#58a6ff', badgeFg: '#0d1117' }
  }
}

function formatTime(iso: string): string {
  try {
    const d = new Date(iso)
    return d.toLocaleString('zh-TW', { hour12: false })
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
}

const headerRowStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'baseline',
  justifyContent: 'space-between',
  marginBottom: 16,
}

const searchRowStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  marginBottom: 16,
}

const inputStyle: React.CSSProperties = {
  background: '#161b22',
  color: '#e5e7eb',
  border: '1px solid #21262d',
  padding: '6px 8px',
  borderRadius: 6,
  fontFamily: 'JetBrains Mono, monospace',
  fontSize: 13,
  width: 280,
}

const selectStyle: React.CSSProperties = {
  background: '#161b22',
  color: '#e5e7eb',
  border: '1px solid #21262d',
  padding: '6px 8px',
  borderRadius: 6,
  fontFamily: 'JetBrains Mono, monospace',
  fontSize: 13,
}

const searchInputStyle: React.CSSProperties = {
  background: '#161b22',
  color: '#e5e7eb',
  border: '1px solid #21262d',
  padding: '6px 8px',
  borderRadius: 6,
  fontFamily: 'JetBrains Mono, monospace',
  fontSize: 13,
  width: 220,
}

const sectionStyle: React.CSSProperties = {
  background: 'transparent',
  border: '1px solid #21262d',
  borderRadius: 8,
  padding: 16,
  marginTop: 16,
}

const sectionTitleStyle: React.CSSProperties = {
  margin: 0,
  marginBottom: 12,
  fontSize: 14,
  fontWeight: 600,
  color: '#e5e7eb',
}

const infoGridStyle: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(4, 1fr)',
  gap: 16,
}

const timelineGridStyle: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(6, 1fr)',
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
  borderBottom: '1px solid #21262d',
  fontSize: 11,
  fontWeight: 500,
}

const tdStyle: React.CSSProperties = {
  padding: '8px 10px',
}

const tdMonoStyle: React.CSSProperties = {
  ...tdStyle,
  fontFamily: 'JetBrains Mono, monospace',
}

const linkBtnStyle: React.CSSProperties = {
  background: 'transparent',
  color: '#58a6ff',
  border: 'none',
  cursor: 'pointer',
  fontSize: 12,
  fontFamily: 'JetBrains Mono, monospace',
  padding: 0,
}

const emptyStyle: React.CSSProperties = {
  padding: 12,
  background: '#161b22',
  border: '1px dashed #21262d',
  borderRadius: 6,
  color: '#6b7280',
  fontSize: 12,
  textAlign: 'center',
}

const errorStyle: React.CSSProperties = {
  padding: 12,
  marginBottom: 12,
  background: '#1f1300',
  border: '1px solid #f0b429',
  borderRadius: 6,
  color: '#f0b429',
  fontSize: 12,
}
