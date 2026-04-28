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
      const json = (await res.json()) as unknown
      // 為什麼要做 profile 欄位正規化：
      // - 後端 Domain Profile JSON 來源是 `shared/domain-profiles/*.json`，欄位是 snake_case（label_zh / profile_id），
      //   但前端型別與元件統一使用 camelCase（labelZh / profileId）；
      // - 若直接 cast，UI 會出現「AOI-A undefined」、「undefined (%)」這種錯誤字樣。
      setProfile(normalizeProfile(json))
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

function normalizeProfile(raw: unknown): DomainProfile {
  // 為什麼做成「兼容兩種格式」：
  // - 未來後端若改成直接輸出 camelCase（labelZh），前端不需要再跟著改；
  // - 因此同時支援 labelZh 與 label_zh，擇一有值就用。
  const r = (raw ?? {}) as any

  const labelZh = (x: any): string => (x?.labelZh ?? x?.label_zh ?? '')
  const idPrefix = (x: any): string | null | undefined => (x?.idPrefix ?? x?.id_prefix)
  const goodThreshold = (x: any): number | null | undefined => (x?.goodThreshold ?? x?.good_threshold)
  const goodThresholdLt = (x: any): number | null | undefined => (x?.goodThresholdLt ?? x?.good_threshold_lt)
  /** 與後端 definition_zh 對齊，供 KPI 卡顯示指標定義／公式 */
  const definitionZh = (x: any): string | null | undefined =>
    x?.definitionZh ?? x?.definition_zh

  return {
    profileId: r.profileId ?? r.profile_id ?? 'pcb',
    displayName: r.displayName ?? r.display_name ?? 'AOI Ops Platform',
    factory: {
      name: r.factory?.name ?? '',
      site: r.factory?.site ?? '',
    },
    entities: {
      panel: {
        table: r.entities?.panel?.table ?? 'wafers',
        labelZh: labelZh(r.entities?.panel),
        idPrefix: idPrefix(r.entities?.panel) ?? null,
      },
      lot: {
        table: r.entities?.lot?.table ?? 'lots',
        labelZh: labelZh(r.entities?.lot),
        idPrefix: idPrefix(r.entities?.lot) ?? null,
      },
      tool: {
        table: r.entities?.tool?.table ?? 'tools',
        labelZh: labelZh(r.entities?.tool),
        idPrefix: idPrefix(r.entities?.tool) ?? null,
      },
    },
    stations: Array.isArray(r.stations)
      ? r.stations.map((s: any) => ({ code: s.code ?? '', labelZh: labelZh(s), seq: Number(s.seq ?? 0) }))
      : [],
    lines: Array.isArray(r.lines)
      ? r.lines.map((l: any) => ({ code: l.code ?? '', labelZh: labelZh(l) }))
      : [],
    parameters: Array.isArray(r.parameters)
      ? r.parameters.map((p: any) => ({
          code: p.code ?? '',
          labelZh: labelZh(p),
          unit: p.unit ?? '',
          usl: Number(p.usl ?? 0),
          lsl: Number(p.lsl ?? 0),
          target: Number(p.target ?? 0),
        }))
      : [],
    menus: Array.isArray(r.menus)
      ? r.menus.map((m: any) => ({ id: m.id ?? '', labelZh: labelZh(m) }))
      : [],
    kpi:
      r.kpi && typeof r.kpi === 'object'
        ? Object.fromEntries(
            Object.entries(r.kpi).map(([k, v]) => [
              k,
              {
                labelZh: labelZh(v),
                definitionZh: definitionZh(v) ?? null,
                goodThreshold: goodThreshold(v) ?? null,
                goodThresholdLt: goodThresholdLt(v) ?? null,
              },
            ])
          )
        : {},
    wording: (r.wording && typeof r.wording === 'object' ? r.wording : {}) as Record<string, string>,
  }
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
