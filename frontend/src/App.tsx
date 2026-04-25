import { useEffect, useMemo, useState } from 'react'
import './App.css'
import SpcDashboard from './pages/SpcDashboard'

type DbHealthResponse = {
  canConnect: boolean
  toolsTableExists: boolean
}

type LotListItem = {
  id: string
  lotNo: string
  productCode: string | null
  quantity: number | null
  startTime: string | null
  endTime: string | null
  status: string | null
  createdAt: string
}

type ToolListItem = {
  id: string
  toolCode: string
  toolName: string
  toolType: string | null
  status: string | null
  location: string | null
  createdAt: string
}

type DefectListItem = {
  id: string
  defectCode: string
  defectType: string | null
  severity: string | null
  detectedAt: string
  isFalseAlarm: boolean
  kafkaEventId: string | null
  toolId: string
  toolCode: string | null
  lotId: string
  lotNo: string | null
  waferId: string
  waferNo: string | null
}

type DefectDetail = {
  id: string
  defectCode: string
  defectType: string | null
  severity: string | null
  xCoord: string | null
  yCoord: string | null
  detectedAt: string
  isFalseAlarm: boolean
  kafkaEventId: string | null
  toolId: string
  toolCode: string | null
  toolName: string | null
  lotId: string
  lotNo: string | null
  waferId: string
  waferNo: string | null
  processRunId: string | null
  images: unknown[]
  reviews: unknown[]
}

// 頁面類型（使用簡單 state 切換，不引入 router 以保持最小依賴）
type PageId = 'health' | 'spc'

/**
 * App（前端導覽外殼）
 *
 * 為什麼用 state 切換頁面而不用 react-router：
 * - MVP 階段頁面少，不需要 URL 路由的複雜度。
 * - 未來要加 router，只需把這段 state 切換改成 <Routes>，改動量極小。
 *
 * 解決什麼問題：
 * - 讓 SPC Dashboard 和原有的 Health/API 驗收頁面共存，不需要重寫現有程式碼。
 */
function App() {
  const [page, setPage] = useState<PageId>('health')
  const apiBaseUrl = useMemo(() => {
    // 為什麼從 env 讀：
    // - 本機開發、Docker container、未來部署的後端網址可能不同，用環境變數切換最安全也最不容易搞混。
    // - 你的 docker-compose 已經提供 VITE_API_BASE_URL=http://localhost:8080。
    return (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:8080'
  }, [])

  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [data, setData] = useState<DbHealthResponse | null>(null)

  const [lotsLoading, setLotsLoading] = useState(false)
  const [lotsError, setLotsError] = useState<string | null>(null)
  const [lots, setLots] = useState<LotListItem[] | null>(null)

  const [toolsLoading, setToolsLoading] = useState(false)
  const [toolsError, setToolsError] = useState<string | null>(null)
  const [tools, setTools] = useState<ToolListItem[] | null>(null)

  const [defectsLoading, setDefectsLoading] = useState(false)
  const [defectsError, setDefectsError] = useState<string | null>(null)
  const [defects, setDefects] = useState<DefectListItem[] | null>(null)

  const [selectedDefectId, setSelectedDefectId] = useState<string>('')
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailError, setDetailError] = useState<string | null>(null)
  const [detail, setDetail] = useState<DefectDetail | null>(null)

  async function fetchDbHealth(signal?: AbortSignal) {
    // 為什麼用獨立 function：
    // - 之後你要改成打 /api/lots 或 /api/defects，也只要換 URL，不用重寫整個頁面結構。
    // - 同時把錯誤處理集中，畫面會更穩定（新手也比較容易理解）。
    const res = await fetch(`${apiBaseUrl}/api/health/db`, { signal })
    if (!res.ok) {
      throw new Error(`Health API failed: ${res.status} ${res.statusText}`)
    }
    return (await res.json()) as DbHealthResponse
  }

  async function fetchLots(signal?: AbortSignal) {
    // 為什麼要補這支 fetch：
    // - W02 的第一個可交付 API 是 GET /api/lots。
    // - 對新手來說，「前端真的能把資料列出來」比看 Swagger 更有感，也更好驗收。
    const res = await fetch(`${apiBaseUrl}/api/lots`, { signal })
    if (!res.ok) {
      throw new Error(`Lots API failed: ${res.status} ${res.statusText}`)
    }
    return (await res.json()) as LotListItem[]
  }

  async function fetchTools(signal?: AbortSignal) {
    // 為什麼這週就加 tools：
    // - tools 是 dashboard 最常用的 filter，早點把 API/前端打通，後面做趨勢圖會更順。
    const res = await fetch(`${apiBaseUrl}/api/tools`, { signal })
    if (!res.ok) {
      throw new Error(`Tools API failed: ${res.status} ${res.statusText}`)
    }
    return (await res.json()) as ToolListItem[]
  }

  async function fetchDefects(signal?: AbortSignal) {
    // 為什麼要做 defects：
    // - AOI 平台的核心就是缺陷清單；把這支打通，W05/W06 才能做 detail/review。
    const res = await fetch(`${apiBaseUrl}/api/defects`, { signal })
    if (!res.ok) {
      throw new Error(`Defects API failed: ${res.status} ${res.statusText}`)
    }
    return (await res.json()) as DefectListItem[]
  }

  async function fetchDefectDetail(id: string, signal?: AbortSignal) {
    // 為什麼要用「輸入 id 查 detail」：
    // - 新手先不用做 router / page 切換，先確認 detail API 的資料形狀是對的。
    // - 等你之後做真正的 defects page，再把這段搬過去即可。
    const res = await fetch(`${apiBaseUrl}/api/defects/${id}`, { signal })
    if (!res.ok) {
      throw new Error(`Defect detail failed: ${res.status} ${res.statusText}`)
    }
    return (await res.json()) as DefectDetail
  }

  useEffect(() => {
    const controller = new AbortController()
    setLoading(true)
    setError(null)

    fetchDbHealth(controller.signal)
      .then((json) => setData(json))
      .catch((e: unknown) => {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setError(e instanceof Error ? e.message : String(e))
      })
      .finally(() => setLoading(false))

    return () => controller.abort()
  }, [apiBaseUrl])

  useEffect(() => {
    const controller = new AbortController()
    setLotsLoading(true)
    setLotsError(null)

    fetchLots(controller.signal)
      .then((json) => setLots(json))
      .catch((e: unknown) => {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setLotsError(e instanceof Error ? e.message : String(e))
      })
      .finally(() => setLotsLoading(false))

    return () => controller.abort()
  }, [apiBaseUrl])

  useEffect(() => {
    const controller = new AbortController()
    setToolsLoading(true)
    setToolsError(null)

    fetchTools(controller.signal)
      .then((json) => setTools(json))
      .catch((e: unknown) => {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setToolsError(e instanceof Error ? e.message : String(e))
      })
      .finally(() => setToolsLoading(false))

    return () => controller.abort()
  }, [apiBaseUrl])

  useEffect(() => {
    const controller = new AbortController()
    setDefectsLoading(true)
    setDefectsError(null)

    fetchDefects(controller.signal)
      .then((json) => setDefects(json))
      .catch((e: unknown) => {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setDefectsError(e instanceof Error ? e.message : String(e))
      })
      .finally(() => setDefectsLoading(false))

    return () => controller.abort()
  }, [apiBaseUrl])

  useEffect(() => {
    // 為什麼用 defects 的第一筆當預設：
    // - 讓新手打開頁面就能看到「detail 真的有資料」，不必先手動複製貼上 id。
    if (!defects || defects.length === 0) return
    if (selectedDefectId) return
    setSelectedDefectId(defects[0].id)
  }, [defects, selectedDefectId])

  useEffect(() => {
    if (!selectedDefectId) return

    const controller = new AbortController()
    setDetailLoading(true)
    setDetailError(null)
    setDetail(null)

    fetchDefectDetail(selectedDefectId, controller.signal)
      .then((json) => setDetail(json))
      .catch((e: unknown) => {
        if (e instanceof DOMException && e.name === 'AbortError') return
        setDetailError(e instanceof Error ? e.message : String(e))
      })
      .finally(() => setDetailLoading(false))

    return () => controller.abort()
  }, [apiBaseUrl, selectedDefectId])

  return (
    <>
      {/* 頂部導覽列 */}
      <nav style={{
        background: '#111827',
        borderBottom: '1px solid #374151',
        padding: '0 24px',
        display: 'flex',
        alignItems: 'center',
        gap: 24,
        height: 48,
        position: 'sticky',
        top: 0,
        zIndex: 100,
      }}>
        <span style={{ color: '#60a5fa', fontWeight: 700, fontSize: 14 }}>AOI Ops Platform</span>
        {([
          { id: 'health', label: '系統狀態 / API 驗收' },
          { id: 'spc',    label: '📊 SPC 統計製程管制' },
        ] as { id: PageId; label: string }[]).map((nav) => (
          <button
            key={nav.id}
            onClick={() => setPage(nav.id)}
            style={{
              background: 'transparent',
              border: 'none',
              borderBottom: page === nav.id ? '2px solid #3b82f6' : '2px solid transparent',
              color: page === nav.id ? '#60a5fa' : '#9ca3af',
              fontSize: 13,
              fontWeight: page === nav.id ? 600 : 400,
              cursor: 'pointer',
              padding: '0 4px',
              height: '100%',
            }}
          >
            {nav.label}
          </button>
        ))}
      </nav>

      {/* SPC Dashboard 頁面 */}
      {page === 'spc' && (
        <div style={{ background: '#0d1117', minHeight: 'calc(100vh - 48px)' }}>
          <SpcDashboard />
        </div>
      )}

      {/* 原有系統狀態 / API 驗收頁（只在選到 health 時顯示） */}
      {page === 'health' && (
      <section id="center">
        <div>
          <h1>AOI Ops Platform — 前端最小驗收</h1>
          <p>
            這頁只做一件事：打後端 <code>/api/health/db</code>，確認後端與 DB 真的有通。
          </p>
          <p>
            API Base URL（可用 <code>VITE_API_BASE_URL</code> 調整）：<code>{apiBaseUrl}</code>
          </p>

          {loading && <p>載入中…</p>}
          {!loading && error && (
            <div style={{ textAlign: 'left', maxWidth: 720, margin: '0 auto' }}>
              <h2 style={{ marginBottom: 8 }}>連線失敗</h2>
              <pre style={{ whiteSpace: 'pre-wrap' }}>{error}</pre>
              <p style={{ marginTop: 12 }}>
                新手排查順序建議：先確認 backend 是否在 <code>http://localhost:8080</code>，
                再確認 docker compose 的服務是否都起來。
              </p>
            </div>
          )}

          {!loading && !error && data && (
            <div style={{ textAlign: 'left', maxWidth: 720, margin: '0 auto' }}>
              <h2 style={{ marginBottom: 8 }}>連線成功</h2>
              <ul>
                <li>
                  後端是否能連 DB：<strong>{String(data.canConnect)}</strong>
                </li>
                <li>
                  <code>tools</code> 資料表是否存在（用來判斷 schema 是否已建立）：
                  <strong> {String(data.toolsTableExists)}</strong>
                </li>
              </ul>
              <p style={{ marginTop: 12 }}>
                下一步（W02）：你應該也能打得通 <code>/api/lots</code>，並在下方看到 lot 清單。
              </p>
            </div>
          )}

          <div style={{ textAlign: 'left', maxWidth: 720, margin: '20px auto 0' }}>
            <h2 style={{ marginBottom: 8 }}>W02 驗收：Lots 清單（GET /api/lots）</h2>
            {lotsLoading && <p>載入 lots 中…</p>}
            {!lotsLoading && lotsError && (
              <pre style={{ whiteSpace: 'pre-wrap' }}>{lotsError}</pre>
            )}
            {!lotsLoading && !lotsError && lots && (
              <>
                <p style={{ marginBottom: 8 }}>共 {lots.length} 筆（seed 應該至少有 5 筆）</p>
                {/* 為什麼用 <pre>：
                    - 新手先求看得到資料；等你熟了再做表格與分頁。 */}
                <pre style={{ whiteSpace: 'pre-wrap', fontSize: 12 }}>
                  {JSON.stringify(lots, null, 2)}
                </pre>
              </>
            )}
          </div>

          <div style={{ textAlign: 'left', maxWidth: 720, margin: '20px auto 0' }}>
            <h2 style={{ marginBottom: 8 }}>W02 擴充：Tools 清單（GET /api/tools）</h2>
            {toolsLoading && <p>載入 tools 中…</p>}
            {!toolsLoading && toolsError && (
              <pre style={{ whiteSpace: 'pre-wrap' }}>{toolsError}</pre>
            )}
            {!toolsLoading && !toolsError && tools && (
              <>
                <p style={{ marginBottom: 8 }}>共 {tools.length} 筆（seed 應該至少有 2 筆）</p>
                <pre style={{ whiteSpace: 'pre-wrap', fontSize: 12 }}>
                  {JSON.stringify(tools, null, 2)}
                </pre>
              </>
            )}
          </div>

          <div style={{ textAlign: 'left', maxWidth: 720, margin: '20px auto 0' }}>
            <h2 style={{ marginBottom: 8 }}>W02 擴充：Defects 清單（GET /api/defects）</h2>
            {defectsLoading && <p>載入 defects 中…</p>}
            {!defectsLoading && defectsError && (
              <pre style={{ whiteSpace: 'pre-wrap' }}>{defectsError}</pre>
            )}
            {!defectsLoading && !defectsError && defects && (
              <>
                <p style={{ marginBottom: 8 }}>共 {defects.length} 筆（seed 應該至少有 1 筆）</p>
                <pre style={{ whiteSpace: 'pre-wrap', fontSize: 12 }}>
                  {JSON.stringify(defects, null, 2)}
                </pre>
              </>
            )}
          </div>

          <div style={{ textAlign: 'left', maxWidth: 720, margin: '20px auto 0' }}>
            <h2 style={{ marginBottom: 8 }}>W05 預備：Defect Detail（GET /api/defects/{'{id}'})</h2>
            <p style={{ marginBottom: 8 }}>
              先輸入 defect id 來驗收 detail API（之後做正式頁面再搬過去）。
            </p>
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <label>
                Defect Id：
                <input
                  value={selectedDefectId}
                  onChange={(e) => setSelectedDefectId(e.target.value)}
                  style={{ marginLeft: 8, width: 360 }}
                  placeholder="貼上 /api/defects 清單中的 id"
                />
              </label>
              <button
                type="button"
                onClick={() => {
                  // 這個按鈕只是讓新手「手動重抓」更直覺；
                  // 實際上 useEffect 也會自動抓，不會影響功能。
                  setSelectedDefectId((x) => x.trim())
                }}
              >
                重新讀取
              </button>
            </div>

            {detailLoading && <p>載入 detail 中…</p>}
            {!detailLoading && detailError && <pre style={{ whiteSpace: 'pre-wrap' }}>{detailError}</pre>}
            {!detailLoading && !detailError && detail && (
              <pre style={{ whiteSpace: 'pre-wrap', fontSize: 12 }}>
                {JSON.stringify(detail, null, 2)}
              </pre>
            )}
          </div>
        </div>
      </section>
      )}
    </>
  )
}

export default App
