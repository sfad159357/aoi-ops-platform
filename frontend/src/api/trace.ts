// trace.ts：物料追溯 REST 客戶端。
//
// 為什麼把 fetch 抽成獨立模組：
// - 頁面只關心「資料長什麼樣」，network / URL 細節集中在這裡；
//   未來換 OpenAPI generated client 也只動這支檔。
//
// 解決什麼問題：
// - 把後端 TraceController.PanelTraceDto 對齊成前端 type，
//   避免每個 component 重複手刻型別。

const baseUrl = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:8080'

export type PanelInfo = {
  panelNo: string
  lotNo: string
  status: string | null
  createdAt: string
}

export type StationLog = {
  stationCode: string
  stationLabel: string
  seq: number
  enteredAt: string
  exitedAt: string | null
  result: string | null
  operator: string | null
  // 為什麼補 operatorName / toolCode：
  // - 後端 StationLogDto 已新增這兩個欄位（panel_station_log 已冗餘儲存），
  //   前端時間軸卡片要顯示「OP-001 王小明 / SMT-A01」這種人機並列。
  operatorName: string | null
  toolCode: string | null
  note: string | null
}

export type MaterialLot = {
  materialLotNo: string
  materialType: string
  materialName: string | null
  supplier: string | null
  receivedAt: string | null
  quantity: number | null
}

export type RelatedPanel = {
  panelNo: string
  lotNo: string
  status: string | null
  createdAt: string
}

export type PanelTrace = {
  panel: PanelInfo
  stations: StationLog[]
  materials: MaterialLot[]
  sameLotPanels: RelatedPanel[]
  sameMaterialPanels: RelatedPanel[]
}

export type MaterialTrackingItem = {
  panelNo: string
  lotNo: string
  materialLotNo: string
  materialType: string
  materialName: string | null
  supplier: string | null
  quantity: number | null
  usedAt: string
}

export async function fetchPanelTrace(panelNo: string, signal?: AbortSignal): Promise<PanelTrace> {
  const res = await fetch(`${baseUrl}/api/trace/panel/${encodeURIComponent(panelNo)}`, { signal })
  if (!res.ok) {
    const text = await safeText(res)
    throw new Error(`trace API failed: ${res.status} ${text}`)
  }
  return (await res.json()) as PanelTrace
}

export async function fetchRecentPanels(take = 20, signal?: AbortSignal): Promise<RelatedPanel[]> {
  const res = await fetch(`${baseUrl}/api/trace/panels/recent?take=${take}`, { signal })
  if (!res.ok) throw new Error(`recent panels failed: ${res.status}`)
  return (await res.json()) as RelatedPanel[]
}

// 為什麼補這支 API：
// - 物料追蹤查詢要直接吃 panel_material_usage 真資料；
// - 依日期由後端查詢可避免前端拿 recent panel 再二次過濾造成誤差。
export async function fetchMaterialTracking(dateYmd: string, take = 500, signal?: AbortSignal): Promise<MaterialTrackingItem[]> {
  const res = await fetch(
    `${baseUrl}/api/trace/material-tracking?date=${encodeURIComponent(dateYmd)}&take=${take}`,
    { signal },
  )
  if (!res.ok) {
    const text = await safeText(res)
    throw new Error(`material tracking failed: ${res.status} ${text}`)
  }
  return (await res.json()) as MaterialTrackingItem[]
}

async function safeText(res: Response): Promise<string> {
  try {
    return await res.text()
  } catch {
    return ''
  }
}
