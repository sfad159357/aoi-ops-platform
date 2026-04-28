// spcKpiContext：SPC 儀表板 KPI 用的時間窗、累積量、速率、良率與「今日違規」計數。
//
// 為什麼不叫「子組」：
// - 教科書的「子組」通常是「一次抽樣裡含多個量測」，再算 X̄／R；
// - 本專案管線是「一則 Kafka 檢驗事件 → 管制圖上一個點」（單點／n=1），
//   用「觀測點」較準；若 Kafka 帶 inspected_qty&gt;1，累積產出改為 Σ 件數而非單純點數。
//
// 解決什麼問題：「累積」與「件/秒」必須可由同一時間窗復算（Σ÷Δt），避免看起來像隨機 demo 數字。
// 「今日違規」必須與觀測點同一母體，不可再取另一條 spcViolation 緩衝，否則會大於累積。

import type { SpcPointPayload } from '../realtime/useSpcStream'

/** 與視窗首末觀測點對齊之時間跨度統計，供 KPI 與頁面驗算列使用 */
export type ObservationWindowStats = {
  /** 管制圖上點個數 N（觀測點數） */
  pointCount: number
  /** 各點 inspected_qty 加總；未傳欄位時每點視為 1 */
  totalInspectedQty: number
  /** 首末點時間差（小時），至少 1/60 以免除零 */
  hours: number
  /** totalInspectedQty / hours */
  qtyPerHour: number
  /** totalInspectedQty / (秒)；與 qtyPerHour/3600 同值（同一 Δt） */
  qtyPerSecond: number
}

/**
 * 單點檢驗件數：SignalR 未帶欄位時（舊連線）預設 1。
 */
export function inspectedQtyOf(p: SpcPointPayload): number {
  const q = p.inspectedQty
  if (q == null || Number.isNaN(q) || q < 1) return 1
  return Math.floor(q)
}

/**
 * 是否為「後端判定的違規點」。
 *
 * 規則（以後端為權威來源）：
 * - `violations` 由後端 SpcRulesEngine 產生，代表該點觸發了 SPC 規則（包含 OutOfSpec ruleId=0 與 Nelson ruleId≥1）
 * - `ucl/lcl` 由後端視窗計算（與 value 同刻度），可用來補強「超出管制線」這個最直覺的違規定義
 *
 * 為什麼仍保留 ucl/lcl 檢查：
 * - demo/開發階段可能會遇到「後端 violations 沒列出，但 value 已超出 ucl/lcl」的 edge case；
 *   既然 ucl/lcl 也是後端算出來的，前端採用它仍符合「後端為權威」原則。
 */
export function isBackendViolationPoint(p: SpcPointPayload): boolean {
  const v = p.violations
  if (Array.isArray(v) && v.length > 0) return true
  if (p.ucl != null && p.lcl != null && isFinite(p.ucl) && isFinite(p.lcl)) {
    if (p.value > p.ucl || p.value < p.lcl) return true
  }
  return false
}

/**
 * 視窗內累積檢驗量（件）：Σ inspected_qty，與圖上「點數 N」在倍數上可對帳（每點多件時 N&lt;Σ）。
 */
export function sumInspectedQty(points: SpcPointPayload[]): number {
  if (points.length === 0) return 0
  let s = 0
  for (const p of points) s += inspectedQtyOf(p)
  return s
}

/**
 * 由同一組觀測點計算 Δt 與件/小時；速率與累積必須由此路徑導出，避免與點數分開各算各的。
 */
export function computeObservationWindow(points: SpcPointPayload[]): ObservationWindowStats | null {
  if (points.length < 1) return null
  const totalInspectedQty = sumInspectedQty(points)
  if (points.length < 2) {
    return {
      pointCount: points.length,
      totalInspectedQty,
      hours: 0,
      qtyPerHour: 0,
      qtyPerSecond: 0,
    }
  }
  const first = new Date(points[0].timestamp).getTime()
  const last = new Date(points[points.length - 1].timestamp).getTime()
  const hoursRaw = (last - first) / 3_600_000
  const hours = Math.max(hoursRaw, 1 / 60)
  const qtyPerHour = totalInspectedQty / hours
  return {
    pointCount: points.length,
    totalInspectedQty,
    hours,
    qtyPerHour,
    qtyPerSecond: qtyPerHour / 3600,
  }
}

/**
 * 與圖、累積同一觀測窗：本機「今日」且該觀測點**真正超出管制線**的件數（≤ N）。
 *
 * 為什麼改用 value vs ucl/lcl 比較：
 * - 後端 violations 清單包含 OutOfSpec（ruleId=0），它是用 profile usl/lsl（0~1 比例）判斷；
 *   若後端容器尚未重建、value 仍在 0~100 刻度，每點都會被標 OutOfSpec，導致今日違規 = 累積產出。
 * - ucl/lcl 是後端從資料視窗算出、與 value 同一刻度（不受 profile 刻度影響），
 *   直接比較 value 是否超過 ucl/lcl 才能反映「管制圖上真正偏離」的點數。
 * - Nelson Rule（ruleId ≥ 1）同樣以 cl±sigma 判定，也與資料刻度一致，同計。
 */
export function countTodayViolationPointsInWindow(points: SpcPointPayload[]): number {
  const day = new Date()
  return points.filter((p) => {
    if (!isTimestampLocalDay(p.timestamp, day)) return false
    return isBackendViolationPoint(p)
  }).length
}

/**
 * 今日違規件數（與累積產出同量綱）：把「違規的觀測點」換算成件數 Σ inspected_qty。
 *
 * 為什麼需要這個函式：
 * - `countTodayViolationPointsInWindow` 回傳的是「點數」，而累積/速率用的是「件數」；
 *   一旦 ingestion 將 inspected_qty 提高（例如一點代表 10 panels），
 *   直接用點數去扣件數就會造成良率與今日違規數對不上。
 * - 這裡明確把違規轉為件數，讓：
 *   今日良率 = (今日累積件 - 今日違規件) / 今日累積件
 *   能與「今日違規數（件）」KPI 完整對帳。
 */
export function countTodayViolationQtyInWindow(points: SpcPointPayload[]): number {
  const day = new Date()
  let s = 0
  for (const p of points) {
    if (!isTimestampLocalDay(p.timestamp, day)) continue
    if (!isBackendViolationPoint(p)) continue
    s += inspectedQtyOf(p)
  }
  return s
}

/**
 * 違規專用緩衝列中、時間戳屬今日者筆數（僅供表格／除錯；不宜與累積對帳）
 */
export function countViolationsLocalToday(rows: { timestamp: string }[]): number {
  return rows.filter((r) => isTimestampLocalDay(r.timestamp, new Date())).length
}

/**
 * 今日良率 = (今日累積件 - 今日違規件) / 今日累積件，結果為 0~1 比例。
 *
 * 為什麼不用最後一點的 value：
 * - 設備回報的 yield_rate 是「那一則事件自己的量測值」，不代表今日整體通過率。
 * - 用「(累積件 − 違規件) ÷ 累積件」才能反映整條產線今天實際的良率表現，
 *   且與「今日違規數」KPI 使用同一分子/分母，數字互相可以對帳。
 * - 篩選條件：今日 & filteredPoints 視窗（與累積、違規完全同一母體）。
 */
export function computeTodayYieldRate(points: SpcPointPayload[]): number | null {
  const day = new Date()
  const todayPoints = points.filter((p) => isTimestampLocalDay(p.timestamp, day))
  if (todayPoints.length === 0) return null
  const totalQty = sumInspectedQty(todayPoints)
  if (totalQty === 0) return null
  // 使用「件數」違規，確保今日良率與「今日違規數（件）」同一量綱，可互相對帳
  const violationQty = countTodayViolationQtyInWindow(points)
  const goodQty = Math.max(0, totalQty - violationQty)
  return goodQty / totalQty
}

/**
 * 前端重算 Cpk，解決後端因刻度不一致（value 在 0~100、usl/lsl 在 0~1）導致 Cpk 極端負值的問題。
 *
 * 為什麼在前端重算而不只靠後端值：
 * - 後端 ProcessCapability 使用 profile 的 usl/lsl（0~1 比例）對 0~100 的 value 做計算，
 *   產生 cpu=(1−97)/(3×0.8)≈−40，Cpk 必然極端負。
 * - 後端送來的 cl（= mean）與 sigma 都是從視窗資料計算，與 value 同刻度，可以信任；
 *   只需把 usl/lsl 換算到相同刻度（dataIsPercent 時乘 100）再算 Cpk 即正確。
 * - 後端容器重建後 value 會正規化到 0~1，此函式同樣有效（乘 1 = 不變）。
 */
export function computeCorrectedCpk(
  lastPoint: SpcPointPayload | undefined,
  usl: number,
  lsl: number,
): number | null {
  if (!lastPoint) return null
  const { sigma, cl: mean, value } = lastPoint
  if (!sigma || sigma <= 0 || !isFinite(sigma)) return null
  // 若最新 value > 1.5，視為百分比刻度（0~100）；把 profile 的 0~1 usl/lsl 等比放大
  const dataIsPercent = value > 1.5
  const chartUsl = dataIsPercent && usl <= 1.5 ? usl * 100 : usl
  const chartLsl = dataIsPercent && lsl <= 1.5 ? lsl * 100 : lsl
  const cpu = (chartUsl - mean) / (3 * sigma)
  const cpl = (mean - chartLsl) / (3 * sigma)
  return Math.min(cpu, cpl)
}

function isTimestampLocalDay(iso: string, referenceDay: Date): boolean {
  const d = new Date(iso)
  return (
    d.getFullYear() === referenceDay.getFullYear() &&
    d.getMonth() === referenceDay.getMonth() &&
    d.getDate() === referenceDay.getDate()
  )
}
