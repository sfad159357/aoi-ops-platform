import { useState } from 'react'
import './App.css'
import SpcDashboard from './pages/SpcDashboard'
import AlarmsPage from './pages/AlarmsPage'
import NcrsPage from './pages/WorkordersPage'
import TraceabilityPage from './pages/TraceabilityPage'
import ProductionWorkOrdersPage from './pages/ProductionWorkOrdersPage'
import LineSearchPage from './pages/LineSearchPage'
import RealtimeStatusBadge from './realtime/RealtimeStatusBadge'
import { useProfile } from './domain/useProfile'

// 頁面類型（使用簡單 state 切換，不引入 router 以保持最小依賴）
//
// 為什麼把 alarm / wo / trace 都放進來：
// - 對齊 4 大選單：SPC / 工單 / 異常 / 物料追溯；
//   仍維持單一 state，沒有 router 的學習成本。
type PageId = 'health' | 'spc' | 'pwo' | 'line' | 'alarm' | 'ncr' | 'trace'
type MainMenuId = 'home' | 'realtime' | 'biz'

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
  const { profile } = useProfile()
  const [hoverMain, setHoverMain] = useState<MainMenuId | null>(null)

  // 為什麼把導覽改成「主選單 + 子選單」：
  // - 使用者希望把 4 大頁重新分群：即時監控（事件流）與業務查詢（查詢/追溯）分開；
  // - 仍維持單一 page state，避免在沒有 router 的前提下引入更多狀態同步問題。
  const mainMenu: MainMenuId =
    page === 'health' ? 'home'
    : page === 'ncr' || page === 'alarm' || page === 'spc' ? 'realtime'
    : 'biz'

  const mainMenus: { id: MainMenuId; label: string }[] = [
    { id: 'home', label: '首頁' },
    { id: 'realtime', label: '即時監控' },
    { id: 'biz', label: '業務查詢' },
  ]

  const subMenus: Record<MainMenuId, { id: PageId; label: string }[]> = {
    home: [],
    realtime: [
      { id: 'ncr', label: profile.menus.find((m) => m.id === 'ncr')?.labelZh ?? '不良單' },
      { id: 'alarm', label: profile.menus.find((m) => m.id === 'alarm')?.labelZh ?? '異常紀錄' },
      { id: 'spc', label: profile.menus.find((m) => m.id === 'spc')?.labelZh ?? '品質管制' },
    ],
    biz: [
      { id: 'pwo', label: profile.menus.find((m) => m.id === 'pwo')?.labelZh ?? '工單查詢' },
      { id: 'line', label: '產線查詢' },
      { id: 'trace', label: profile.menus.find((m) => m.id === 'trace')?.labelZh ?? '板號/批次查詢' },
    ],
  }

  const gotoMain = (id: MainMenuId) => {
    // 為什麼主選單切換要有預設子頁：
    // - 讓使用者點主選單就能立刻看到內容，不必再多點一次；
    // - 同時避免子選單空值導致 UI 沒有焦點頁。
    if (id === 'home') setPage('health')
    else if (id === 'realtime') setPage('ncr')
    else setPage('pwo')
  }

  return (
    <>
      {/* 頂部導覽列 */}
      <nav
        style={{
        background: '#111827',
        borderBottom: '1px solid #374151',
        padding: '0 24px',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        height: 48,
        position: 'sticky',
        top: 0,
        zIndex: 100,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
          <span style={{ color: '#60a5fa', fontWeight: 700, fontSize: 14 }}>
            {profile.displayName || 'AOI Ops Platform'}
          </span>
          <div style={{ display: 'flex', alignItems: 'center', gap: 16, height: 48 }}>
            {mainMenus.map((m) => (
              <div
                key={m.id}
                onMouseEnter={() => setHoverMain(m.id)}
                onMouseLeave={() => setHoverMain(null)}
                style={{ position: 'relative', height: '100%', display: 'flex', alignItems: 'center' }}
              >
                <button
                  onClick={() => gotoMain(m.id)}
                  style={{
                    background: 'transparent',
                    border: 'none',
                    borderBottom: mainMenu === m.id ? '2px solid #3b82f6' : '2px solid transparent',
                    color: mainMenu === m.id ? '#60a5fa' : '#9ca3af',
                    fontSize: 13,
                    fontWeight: mainMenu === m.id ? 600 : 400,
                    cursor: 'pointer',
                    padding: '0 4px',
                    height: '100%',
                  }}
                >
                  {m.label}
                </button>

                {/* 為什麼副選單改成「垂直下拉」：
                    - 使用者希望參考 Bootstrap menu：hover 後在主選單下方垂直展開；
                    - 不使用第二列水平 bar，避免佔用垂直高度。 */}
                {hoverMain === m.id && m.id !== 'home' && (
                  <div
                    style={{
                      position: 'absolute',
                      top: 46,
                      left: 0,
                      minWidth: 200,
                      background: '#0b1220',
                      border: '1px solid rgba(148,163,184,0.25)',
                      borderRadius: 10,
                      padding: 6,
                      boxShadow: '0 12px 30px rgba(0,0,0,0.35)',
                      display: 'flex',
                      flexDirection: 'column',
                      gap: 4,
                      zIndex: 200,
                    }}
                  >
                    {subMenus[m.id].map((nav) => (
                      <button
                        key={nav.id}
                        onClick={() => {
                          setPage(nav.id)
                          setHoverMain(null)
                        }}
                        style={{
                          textAlign: 'left',
                          background: page === nav.id ? 'rgba(59,130,246,0.16)' : 'transparent',
                          border: page === nav.id ? '1px solid rgba(59,130,246,0.35)' : '1px solid transparent',
                          borderRadius: 8,
                          color: page === nav.id ? '#93c5fd' : '#cbd5e1',
                          fontSize: 13,
                          fontWeight: page === nav.id ? 600 : 400,
                          cursor: 'pointer',
                          padding: '8px 10px',
                        }}
                      >
                        {nav.label}
                      </button>
                    ))}
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
        <RealtimeStatusBadge hub="spc" />
      </nav>

      {/* SPC Dashboard 頁面 */}
      {page === 'spc' && (
        <div style={{ background: '#0d1117', minHeight: 'calc(100vh - 48px)' }}>
          <SpcDashboard />
        </div>
      )}

      {/* 異常記錄 / 工單管理：W07 新增 */}
      {page === 'alarm' && (
        <div style={{ background: '#0d1117', minHeight: 'calc(100vh - 48px)' }}>
          <AlarmsPage />
        </div>
      )}
      {page === 'ncr' && (
        <div style={{ background: '#0d1117', minHeight: 'calc(100vh - 48px)' }}>
          <NcrsPage />
        </div>
      )}
      {page === 'pwo' && (
        <div style={{ background: '#0d1117', minHeight: 'calc(100vh - 48px)' }}>
          <ProductionWorkOrdersPage />
        </div>
      )}
      {page === 'line' && (
        <div style={{ background: '#0d1117', minHeight: 'calc(100vh - 48px)' }}>
          <LineSearchPage />
        </div>
      )}
      {page === 'trace' && (
        <div style={{ background: '#0d1117', minHeight: 'calc(100vh - 48px)' }}>
          <TraceabilityPage />
        </div>
      )}

      {/* 首頁（只保留落地驗收說明卡） */}
      {page === 'health' && (
        <section id="center">
          <div style={{ width: '100%', maxWidth: 960, margin: '0 auto', padding: '28px 16px 40px' }}>
            {/* 為什麼首頁只留單一卡片：
                - 使用者要首頁當「導覽＋驗收定位」而非資料明細頁，
                - 所以移除長清單與測試輸出，避免第一眼資訊過載。 */}
            <div
              style={{
                background: 'linear-gradient(135deg, rgba(59,130,246,0.18), rgba(16,185,129,0.10))',
                border: '1px solid rgba(148,163,184,0.25)',
                borderRadius: 16,
                padding: '18px 18px 16px',
                marginBottom: 18,
                boxShadow: '0 10px 30px rgba(0,0,0,0.18)',
              }}
            >
              
              <div style={{ display: 'flex', justifyContent: 'center', flexWrap: 'wrap', alignItems: 'center', gap: 10 }}>
                <h1 style={{ margin: 0, fontSize: 28, letterSpacing: 0.2 }}>
                  MES模擬ABF生產線 <span style={{ opacity: 0.9 }}>DEMO</span>
                </h1>
            
              </div>
              {/* <p style={{ margin: '10px 0 0', color: '#cbd5e1', lineHeight: 1.6 }}>
                這個首頁用來做<strong>真實落地驗收</strong>：直接打後端 <code>/api/health/db</code> 與各 API，確保資料流暢、欄位關聯完整（非 mock）。
              </p>
              <p style={{ margin: '8px 0 0', color: '#94a3b8', lineHeight: 1.6 }}>
                API Base URL（可用 <code>VITE_API_BASE_URL</code> 調整）
              </p> */}
            </div>
          </div>
        </section>
      )}
    </>
  )
}

export default App
