import { useEffect, useMemo, useState } from 'react'
import './App.css'

type DbHealthResponse = {
  canConnect: boolean
  toolsTableExists: boolean
}

/**
 * App（前端最小驗收頁）
 *
 * 為什麼要寫這個頁面：
 * - 新手最常卡在「前端跑了，但不知道後端有沒有通、DB 有沒有通」。
 * - 這個頁面只做一件事：呼叫後端 Health API，把結果直接顯示出來，讓你一眼判斷哪裡出問題。
 *
 * 解決什麼問題：
 * - 避免你還沒做任何業務功能，就被環境、連線、CORS、base url 之類的問題卡住。
 */
function App() {
  const apiBaseUrl = useMemo(() => {
    // 為什麼從 env 讀：
    // - 本機開發、Docker container、未來部署的後端網址可能不同，用環境變數切換最安全也最不容易搞混。
    // - 你的 docker-compose 已經提供 VITE_API_BASE_URL=http://localhost:8080。
    return (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:8080'
  }, [])

  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [data, setData] = useState<DbHealthResponse | null>(null)

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

  return (
    <>
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
                下一步（W02）：把 schema 建好並 seed，讓 <code>toolsTableExists</code> 變成 <code>true</code>。
              </p>
            </div>
          )}
        </div>
      </section>
    </>
  )
}

export default App
