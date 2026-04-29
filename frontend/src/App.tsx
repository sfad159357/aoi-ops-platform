import { useState } from 'react'
import './App.css'
import SpcDashboard from './pages/SpcDashboard'
import AlarmsPage from './pages/AlarmsPage'
import WorkordersPage from './pages/WorkordersPage'
import TraceabilityPage from './pages/TraceabilityPage'
import RealtimeStatusBadge from './realtime/RealtimeStatusBadge'
import { useProfile } from './domain/useProfile'

// 頁面類型（使用簡單 state 切換，不引入 router 以保持最小依賴）
//
// 為什麼把 alarm / wo / trace 都放進來：
// - 對齊 4 大選單：SPC / 工單 / 異常 / 物料追溯；
//   仍維持單一 state，沒有 router 的學習成本。
type PageId = 'health' | 'spc' | 'alarm' | 'wo' | 'trace'

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
        <span style={{ color: '#60a5fa', fontWeight: 700, fontSize: 14 }}>
          {profile.displayName || 'AOI Ops Platform'}
        </span>
        {([
          { id: 'health', label: '首頁' },
          // 為什麼把 SPC / Alarm / Workorder 的標籤從 profile 取：
          // - profile.menus 由 domain profile JSON 提供，
          //   切到 semiconductor profile 時，UI 文案會自動跟著換。
          { id: 'wo',     label: profile.menus.find((m) => m.id === 'wo')?.labelZh ?? '工單管理' },
          { id: 'trace',  label: profile.menus.find((m) => m.id === 'trace')?.labelZh ?? '物料追溯查詢' },
          // 為什麼 SPC 也改成讀 profile：
          // - 使用者要求避免前端硬寫標籤；切換不同 domain profile 時，
          //   選單文字應由後端設定（profile JSON）決定，前端只負責渲染。
          { id: 'spc', label: profile.menus.find((m) => m.id === 'spc')?.labelZh ?? '品質監控' },
          // 為什麼把異常記錄移到最後：
          // - 使用者操作流程改為先看首頁/工單/追溯/品質，再看異常事件列表。
          { id: 'alarm',  label: profile.menus.find((m) => m.id === 'alarm')?.labelZh ?? '異常記錄' },
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
      {page === 'wo' && (
        <div style={{ background: '#0d1117', minHeight: 'calc(100vh - 48px)' }}>
          <WorkordersPage />
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
                  MES模擬PCB生產線 <span style={{ opacity: 0.9 }}>DEMO</span>
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
