// TraceabilityPage：板號/批次查詢（含物料追溯）頁。
//
// 為什麼這頁要做：
// - 對齊 HTML Tab 2 的「板資訊 + 站別時間軸 + 物料批號 + 同批次列表」；
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
  fetchMaterialTracking,
  fetchPanelTrace,
  fetchPanelsByLot,
  fetchRecentPanels,
  type MaterialTrackingItem,
  type PanelTrace,
  type RelatedPanel,
  type StationLog,
} from '../api/trace'

export default function TraceabilityPage() {
  const { profile } = useProfile()

  const [panelNoInput, setPanelNoInput] = useState<string>('')
  const [recent, setRecent] = useState<RelatedPanel[]>([])
  const [lotPanels, setLotPanels] = useState<RelatedPanel[]>([])
  const [lotPanelsError, setLotPanelsError] = useState<string | null>(null)
  const [dateFilter, setDateFilter] = useState<string>(() => todayYmd())
  const [materialTake, setMaterialTake] = useState<number>(20)
  const [recentQuery, setRecentQuery] = useState<string>('')
  const [trace, setTrace] = useState<PanelTrace | null>(null)
  const [materialRows, setMaterialRows] = useState<MaterialTrackingItem[]>([])
  const [materialLoading, setMaterialLoading] = useState(false)
  const [materialError, setMaterialError] = useState<string | null>(null)
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

  // 為什麼先做日期再做模糊搜尋：
  // - 物料追溯頁與工單管理頁都以「先看某一天，再縮小關鍵字」為主要操作節奏；
  // - 先過濾日期可降低候選數量，避免最近板很多時下拉選單難以定位。
  //
  // 解決什麼問題：
  // - 讓「板號/批次查詢」具備與「工單查詢」一致的日期篩選體驗。
  // - 使用者可直接切換某天資料，不會被跨日歷史板號干擾。
  // 為什麼要做模糊搜尋：
  // - 追溯時常見情境是「拿到一串板號/批號碎片」就想快速定位；
  // - 不強制精準匹配，能讓工程師用 key-in 立刻縮小候選清單。
  const filteredRecent = useMemo(() => {
    const byDate = dateFilter
      ? recent.filter((r) => toYmd(r.createdAt) === dateFilter)
      : recent
    const q = recentQuery.trim().toLowerCase()
    if (!q) return byDate
    return byDate.filter((r) => {
      const a = r.panelNo.toLowerCase()
      const b = (r.lotNo ?? '').toLowerCase()
      return a.includes(q) || b.includes(q)
    })
  }, [dateFilter, recent, recentQuery])

  // 為什麼輸入批次號要去後端列出板號：
  // - recent panels 只撈最近 N 張，批次若不在其中，前端純過濾永遠找不到；
  // - 追溯常見入口是 lot/工單，必須能用 lotNo 拉出同批次所有板號讓使用者點選。
  useEffect(() => {
    const q = recentQuery.trim()
    if (!q) {
      setLotPanels([])
      setLotPanelsError(null)
      return
    }
    // 取捨：以「看起來像批次號」才打 API，避免每輸入一個字就打爆後端
    const looksLikeLot = q.toUpperCase().includes('LOT') || q.toUpperCase().startsWith('WO-')
    if (!looksLikeLot) {
      setLotPanels([])
      setLotPanelsError(null)
      return
    }

    const ctrl = new AbortController()
    setLotPanelsError(null)
    void (async () => {
      try {
        const list = await fetchPanelsByLot(q, 200, ctrl.signal)
        setLotPanels(list)
      } catch (e) {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setLotPanels([])
        setLotPanelsError(e instanceof Error ? e.message : String(e))
      }
    })()
    return () => ctrl.abort()
  }, [recentQuery])

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

  // 為什麼依日期直接打 material-tracking API：
  // - 「物料追蹤查詢」要呈現 DB 真實交易資料，不能只靠 recent panel 在前端過濾；
  // - 每次切日期就重查，可讓畫面與 DBeaver 查詢結果一一對齊。
  useEffect(() => {
    if (!dateFilter) {
      setMaterialRows([])
      return
    }
    const ctrl = new AbortController()
    setMaterialLoading(true)
    setMaterialError(null)
    void (async () => {
      try {
        const rows = await fetchMaterialTracking(dateFilter, materialTake, ctrl.signal)
        setMaterialRows(rows)
      } catch (e) {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setMaterialError(e instanceof Error ? e.message : String(e))
        setMaterialRows([])
      } finally {
        setMaterialLoading(false)
      }
    })()
    return () => ctrl.abort()
  }, [dateFilter, materialTake])

  // 為什麼物料追蹤列表要吃「板號 / 模糊搜尋」即時過濾：
  // - 使用者在追溯時常會先輸入某張板或一段碎片字串（板號/批次/物料批號），希望同頁的所有表格同步縮小範圍；
  // - 物料追蹤 API 以日期為主查詢（真實交易資料），前端在「不改變 SQL 結果」前提下做純過濾，
  //   能保留單一事實來源，同時改善操作體驗。
  const filteredMaterialRows = useMemo(() => {
    const q = recentQuery.trim().toLowerCase()
    const panelQ = panelNoInput.trim().toLowerCase()
    return materialRows.filter((r) => {
      const panelNo = r.panelNo.toLowerCase()
      const lotNo = (r.lotNo ?? '').toLowerCase()
      const materialLotNo = (r.materialLotNo ?? '').toLowerCase()
      const materialType = (r.materialType ?? '').toLowerCase()

      if (panelQ && !panelNo.includes(panelQ)) return false
      if (!q) return true
      return panelNo.includes(q) || lotNo.includes(q) || materialLotNo.includes(q) || materialType.includes(q)
    })
  }, [materialRows, panelNoInput, recentQuery])

  // 為什麼 timeline 改成完全吃後端 trace.stations：
  // - 使用者要求不要前端硬寫站數（例如固定 6 站），改由後端回傳決定顯示內容；
  // - TraceController 已回傳 stationLabel/seq，前端只做排序渲染，避免雙邊規格漂移。
  //
  // 解決什麼問題：
  // - 當後端站別配置調整（增站/減站/改序）時，前端不必改碼即可正確呈現。
  const timeline = useMemo(() => {
    const rows = trace?.stations ?? []
    return [...rows].sort((a, b) => {
      if (a.seq !== b.seq) return a.seq - b.seq
      return a.enteredAt.localeCompare(b.enteredAt)
    })
  }, [trace?.stations])

  return (
    <div style={pageStyle}>
      <div style={headerRowStyle}>
        <div>
          <h1 style={{ margin: 0, fontSize: 18, fontWeight: 600 }}>
            {profile.menus.find((m) => m.id === 'trace')?.labelZh ?? '板號/批次查詢'}
          </h1>
          <div style={{ color: '#6b7280', fontSize: 12, marginTop: 4 }}>
            掃 QR Code 或選最近{profile.entities.panel.labelZh}：站別時間軸 + 物料批號 + 同批次{profile.entities.panel.labelZh}
          </div>
        </div>
      </div>

      {/* 查詢列 */}
      <div style={searchRowStyle}>
        <label style={{ fontSize: 12, color: '#9ca3af' }}>日期</label>
        <input
          type="date"
          value={dateFilter}
          onChange={(e) => setDateFilter(e.target.value)}
          style={dateInputStyle}
        />
        <label style={{ fontSize: 12, color: '#9ca3af' }}>物料筆數</label>
        <select
          value={String(materialTake)}
          onChange={(e) => setMaterialTake(Number(e.target.value))}
          style={selectStyle}
        >
          {/* 為什麼預設 20 筆：
              - 追溯頁操作節奏是先快速檢視，避免一次回太多列影響首屏速度；
              - 仍保留更多筆數選項給進階追查。 */}
          <option value="20">預設 20 筆</option>
          <option value="50">50 筆</option>
          <option value="100">100 筆</option>
          <option value="500">500 筆</option>
          <option value="1000">1000 筆</option>
        </select>
        <label style={{ fontSize: 12, color: '#9ca3af' }}>
          板號
        </label>
        <input
          value={panelNoInput}
          onChange={(e) => setPanelNoInput(e.target.value)}
          placeholder="例如 ABF-20240422-LOT-001-1"
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
            <option value="">— {dateFilter || '全部日期'} 共 {filteredRecent.length} 張 —</option>
            {filteredRecent.map((r) => (
              <option key={r.panelNo} value={r.panelNo}>
                {r.panelNo}（lot={r.lotNo}）
              </option>
            ))}
          </select>
        )}
        {lotPanelsError && <div style={{ ...errorStyle, marginBottom: 0 }}>批次查板失敗：{lotPanelsError}</div>}
        {lotPanels.length > 0 && (
          <select
            value={panelNoInput}
            onChange={(e) => setPanelNoInput(e.target.value)}
            style={selectStyle}
          >
            <option value="">— 批次 {recentQuery.trim()} 共 {lotPanels.length} 張 —</option>
            {lotPanels.map((r) => (
              <option key={r.panelNo} value={r.panelNo}>
                {r.panelNo}
              </option>
            ))}
          </select>
        )}
      </div>

      {loading && <div style={{ color: '#9ca3af', fontSize: 12 }}>載入中…</div>}
      {error && <div style={errorStyle}>查無資料或查詢失敗：{error}</div>}

      {/* 物料追蹤查詢（真資料） */}
      <section style={sectionStyle}>
        <h2 style={sectionTitleStyle}>物料追蹤查詢（{dateFilter || '未指定日期'}）</h2>
        {materialLoading && <div style={{ color: '#9ca3af', fontSize: 12 }}>載入中…</div>}
        {materialError && <div style={errorStyle}>物料追蹤查詢失敗：{materialError}</div>}
        {!materialLoading && !materialError && (
          <div style={{ fontSize: 12, color: '#9ca3af', marginBottom: 8 }}>
            當日真實筆數：{materialRows.length}（目前顯示：{filteredMaterialRows.length}）
          </div>
        )}
        {!materialLoading && !materialError && filteredMaterialRows.length === 0 ? (
          <div style={emptyStyle}>此日期沒有物料使用資料。</div>
        ) : (
          !materialLoading &&
          !materialError && (
            <table style={tableStyle}>
              <thead>
                <tr style={{ background: '#161b22', color: '#9ca3af' }}>
                  <th style={thStyle}>使用時間</th>
                  <th style={thStyle}>板號</th>
                  <th style={thStyle}>批次</th>
                  <th style={thStyle}>物料批號</th>
                  <th style={thStyle}>物料類型</th>
                  <th style={thStyle}>名稱</th>
                  <th style={thStyle}>供應商</th>
                  <th style={thStyle}>用量</th>
                </tr>
              </thead>
              <tbody>
                {filteredMaterialRows.map((r, idx) => (
                  <tr key={`${r.panelNo}-${r.materialLotNo}-${r.usedAt}-${idx}`} style={{ borderBottom: '1px solid #21262d' }}>
                    <td style={tdMonoStyle}>{formatTime(r.usedAt)}</td>
                    <td style={tdMonoStyle}>{r.panelNo}</td>
                    <td style={tdMonoStyle}>{r.lotNo}</td>
                    <td style={tdMonoStyle}>{r.materialLotNo}</td>
                    <td style={tdMonoStyle}>{r.materialType}</td>
                    <td style={tdStyle}>{r.materialName ?? '-'}</td>
                    <td style={tdStyle}>{r.supplier ?? '-'}</td>
                    <td style={tdMonoStyle}>{r.quantity ?? '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )
        )}
      </section>

      {trace && (
        <>
          {/* 板資訊 */}
          <section style={sectionStyle}>
            <h2 style={sectionTitleStyle}>{profile.entities.panel.labelZh} 資訊</h2>
            <div style={infoGridStyle}>
              <InfoCell label="板號" value={trace.panel.panelNo} mono />
              <InfoCell label={profile.entities.lot.labelZh} value={trace.panel.lotNo} mono />
              <InfoCell label="工單號" value={trace.panel.workOrderNo ?? '-'} mono />
              <InfoCell label="狀態" value={trace.panel.status ?? '-'} />
              <InfoCell label="建立時間" value={formatTime(trace.panel.createdAt)} mono />
            </div>
          </section>

          {/* 站別時間軸 */}
          <section style={sectionStyle}>
            <h2 style={sectionTitleStyle}>站別時間軸（{timeline.length} 站）</h2>
            <div style={timelineGridStyle}>
              {timeline.map((t, i) => (
                <StationCard key={`${t.stationCode}-${t.enteredAt}-${i}`} step={i + 1} code={t.stationCode} label={t.stationLabel} log={t} />
              ))}
            </div>
          </section>

          {/* 物料批號 */}
          <section style={sectionStyle}>
            <h2 style={sectionTitleStyle}>使用物料批號</h2>
            {sortedMaterials.length === 0 ? (
              <div style={emptyStyle}>
                沒有任何物料記錄（SQL：`panel_material_usage` 查無此板交易）。
              </div>
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
              <h2 style={sectionTitleStyle}>同物料 {profile.entities.panel.labelZh}（{trace.sameMaterialPanels.length} 張）</h2>
              {trace.materials.length === 0 && (
                <div style={{ fontSize: 12, color: '#9ca3af', marginBottom: 8 }}>
                  此板沒有任何用料批號，因此無法反查「同物料板」。
                </div>
              )}
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
  // 為什麼需要把 result 做正規化再上色：
  // - 板號/批次查詢要求「照 SQL 真實資料呈現」，所以 badge 文字要顯示 DB 原始 result；
  // - 但現場/歷史資料可能出現同義值（例如 OK/NG/SCRAP），若直接用字串比對會被誤判成未知狀態（藍色）。
  // - 取捨：顯示不改動原始值（可對照 SQL），僅把「上色邏輯」統一映射到 pass/fail/warn/skip。
  const rawResult = log?.result
  const normalized = normalizeStationResult(rawResult)
  const palette = resultPalette(normalized, !!log)
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
          {log ? (rawResult ?? '—') : '無資料'}
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
              - 板號/批次查詢頁需要看「誰在哪台機跑這站」才能對接責任歸屬，
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

function normalizeStationResult(raw: string | null | undefined): string {
  const v = (raw ?? '').trim().toLowerCase()
  if (!v) return ''

  // 為什麼要涵蓋 ok/ng/scrap：
  // - 有些來源（或歷史資料）用 OK/NG 表示站別結果；
  // - TraceController/DB 也可能出現 scrap（報廢）等結果，業務上應視為 fail。
  if (v === 'pass' || v === 'ok') return 'pass'
  if (v === 'fail' || v === 'ng' || v === 'reject' || v === 'scrap') return 'fail'
  if (v === 'warn' || v === 'warning') return 'warn'
  if (v === 'skip' || v === 'bypass') return 'skip'
  if (v === 'in_process' || v === 'in_progress' || v === 'processing') return 'in_process'

  return v
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
    case 'in_process':
      return { border: '#6b7280', badgeBg: '#111827', badgeFg: '#9ca3af' }
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

function toYmd(iso: string): string {
  try {
    const d = new Date(iso)
    return d.toLocaleDateString('sv-SE')
  } catch {
    return ''
  }
}

function todayYmd(): string {
  return new Date().toLocaleDateString('sv-SE')
}

const pageStyle: React.CSSProperties = {
  background: '#0d1117',
  color: '#e5e7eb',
  minHeight: 'calc(100vh - 48px)',
  padding: 24,
  fontFamily: 'Noto Sans TC, system-ui, sans-serif',
  // 為什麼整體字體放大 2px：
  // - 追溯頁是現場工程師/主管在產線快速掃一眼的場景，小字容易看不清楚；
  // - 放大基礎字級可讓表格與卡片在不改 layout 的前提下更易讀。
  fontSize: 18,
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

const dateInputStyle: React.CSSProperties = {
  background: '#161b22',
  color: '#e5e7eb',
  border: '1px solid #21262d',
  padding: '6px 8px',
  borderRadius: 6,
  fontFamily: 'JetBrains Mono, monospace',
  fontSize: 12,
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
