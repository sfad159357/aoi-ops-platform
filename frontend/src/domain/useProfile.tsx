// useProfile.tsx：fetch /api/meta/profile + React Context 提供 hook。
//
// 為什麼用 Context + Provider：
// - profile 全站共用，每個頁面都 fetch 一次太浪費；
//   Context 只 fetch 一次後所有 component 共享。
// - Provider 包在 App.tsx 最外層，未來頁面再多也不需要改 hook。
//
// 解決什麼問題：
// - 切換產業 demo 不需要改任何頁面 code，所有文案 / 規格自動跟著 profile 走。

import { createContext, useContext, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import type { DomainProfile } from './profile'
import fallbackPcb from './fallback/pcb.json'

type ProfileState = {
  profile: DomainProfile
  loading: boolean
  error: string | null
  refresh: () => Promise<void>
}

const ProfileContext = createContext<ProfileState | null>(null)

/**
 * 從 backend 取 profile 的 base URL。
 *
 * 為什麼跟 SignalR 共用 VITE_API_BASE_URL：
 * - profile 跟 SignalR Hub 都在同一個 backend，同一個 env var 對使用者最不混亂。
 */
function getApiBaseUrl(): string {
  return (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:8080'
}

/**
 * Profile Context Provider；包在 App 最外層。
 *
 * 為什麼即使 fetch 失敗也不擋 UI：
 * - demo 階段後端尚未啟動時，前端仍要能渲染（顯示 loading / fallback）；
 *   把 fallback json 當保底，能把使用者送進 SPC 頁，再由 retry 自我恢復。
 */
export function ProfileProvider({ children }: { children: ReactNode }) {
  const [profile, setProfile] = useState<DomainProfile>(() => fallbackPcb as DomainProfile)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  async function refresh() {
    setLoading(true)
    setError(null)
    try {
      const res = await fetch(`${getApiBaseUrl()}/api/meta/profile`)
      if (!res.ok) throw new Error(`profile API ${res.status}`)
      const json = (await res.json()) as DomainProfile
      setProfile(json)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      // 為什麼不把 profile 清掉：
      // - fallback 存在的目的就是「失敗時繼續可用」，重設為 null 反而讓所有頁面都壞。
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void refresh()
  }, [])

  const value = useMemo<ProfileState>(
    () => ({ profile, loading, error, refresh }),
    [profile, loading, error]
  )

  return <ProfileContext.Provider value={value}>{children}</ProfileContext.Provider>
}

/**
 * 取當前 profile；尚未包在 Provider 內時會 fallback 為 PCB profile。
 *
 * 為什麼提供 fallback：
 * - 元件單獨渲染（例如 Storybook / 測試）也能跑，不需要強制 wrap Provider。
 */
export function useProfile(): ProfileState {
  const ctx = useContext(ProfileContext)
  if (ctx) return ctx
  return {
    profile: fallbackPcb as DomainProfile,
    loading: false,
    error: null,
    refresh: async () => {},
  }
}
