// profile.ts：對齊後端 DomainProfile 的 TypeScript 型別。
//
// 為什麼前端也定義一份型別：
// - 後端送過來的是 JSON，沒有型別輔助；自己定一份對齊的 type，
//   IDE 可以幫我們檢查 menu.label_zh 寫對沒。
// - 後端 record 的欄位用 PascalCase，但 ASP.NET Core 預設 camelCase serializer，
//   結果 JSON 仍然是 camelCase；型別也跟著用 camelCase。
//
// 解決什麼問題：
// - 前端統一一份 profile 型別，所有頁面都從 useProfile() 拿 typed data，
//   未來新增欄位只改一個地方。

export type DomainEntity = {
  table: string
  labelZh: string
  idPrefix?: string | null
}

export type DomainStation = {
  code: string
  labelZh: string
  seq: number
}

export type DomainLine = {
  code: string
  labelZh: string
}

export type DomainParameter = {
  code: string
  labelZh: string
  unit: string
  usl: number
  lsl: number
  target: number
}

export type DomainMenu = {
  id: string
  labelZh: string
}

export type DomainKpi = {
  labelZh: string
  goodThreshold?: number | null
  goodThresholdLt?: number | null
}

export type DomainProfile = {
  profileId: string
  displayName: string
  factory: { name: string; site: string }
  entities: { panel: DomainEntity; lot: DomainEntity; tool: DomainEntity }
  stations: DomainStation[]
  lines: DomainLine[]
  parameters: DomainParameter[]
  menus: DomainMenu[]
  kpi: Record<string, DomainKpi>
  wording: Record<string, string>
}
