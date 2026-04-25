/**
 * SPC API 客戶端
 *
 * 為什麼獨立一個 api/spc.ts：
 * - 把所有打 SPC 後端的 fetch 邏輯集中管理，前端元件不需要知道 URL 細節。
 * - 型別定義跟後端 Pydantic 模型對齊，編譯期就能發現欄位不符的問題。
 * - 未來要換 API endpoint 或加 auth token，只改這一個檔案。
 *
 * 解決什麼問題：
 * - SPC Service 跑在 port 8001（C# 後端是 8080），需要獨立的 base URL。
 * - Demo 模式：前端直接使用 DEMO_DATA 常數，不需要打後端，讓頁面在後端未啟動時也能展示。
 */

// ─── 型別定義（對齊後端 Pydantic 模型）───────────────────────────────────

export type ControlLimits = {
  ucl: number
  cl: number
  lcl: number
  sigma1?: number | null
  sigma2?: number | null
}

export type ChartPoint = {
  index: number
  value: number
  violation_rules: number[]
}

export type RuleViolation = {
  rule_id: number
  rule_name: string
  points: number[]
  severity: 'red' | 'yellow' | 'green'
}

export type ProcessCapability = {
  ca: number | null
  cp: number
  cpk: number
  cpu: number
  cpl: number
  mean: number
  std: number
  usl: number
  lsl: number
  target: number | null
  grade: 'A+' | 'A' | 'B' | 'C' | 'D'
}

export type XbarRResult = {
  xbar_points: ChartPoint[]
  r_points: ChartPoint[]
  xbar_limits: ControlLimits
  r_limits: ControlLimits
  violations: RuleViolation[]
  capability: ProcessCapability | null
  subgroup_size: number
  total_points: number
}

export type IMRResult = {
  x_points: ChartPoint[]
  mr_points: ChartPoint[]
  x_limits: ControlLimits
  mr_limits: ControlLimits
  violations: RuleViolation[]
  capability: ProcessCapability | null
  total_points: number
}

export type AttributeChartResult = {
  chart_type: 'p' | 'np' | 'c' | 'u'
  points: ChartPoint[]
  limits: ControlLimits
  violations: RuleViolation[]
  total_points: number
}

// ─── SPC Service Base URL ────────────────────────────────────────────────

// 為什麼獨立一個 env 變數（VITE_SPC_API_URL）：
// - SPC 服務跑在不同 port（8001），不能直接用 VITE_API_BASE_URL（8080）。
// - 未來 SPC 可能部署到不同主機，這樣切換更乾淨。
const SPC_BASE = (import.meta.env.VITE_SPC_API_URL as string | undefined) ?? 'http://localhost:8001'

async function spcPost<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(`${SPC_BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!res.ok) {
    const detail = await res.text()
    throw new Error(`SPC API ${path} failed ${res.status}: ${detail}`)
  }
  return res.json() as Promise<T>
}

async function spcGet<T>(path: string): Promise<T> {
  const res = await fetch(`${SPC_BASE}${path}`)
  if (!res.ok) {
    const detail = await res.text()
    throw new Error(`SPC API ${path} failed ${res.status}: ${detail}`)
  }
  return res.json() as Promise<T>
}

// ─── API 函式 ────────────────────────────────────────────────────────────

/** 分析 Xbar-R 管制圖 */
export function analyzeXbarR(payload: {
  subgroup_size: number
  values: number[]
  usl?: number
  lsl?: number
  target?: number
}): Promise<XbarRResult> {
  return spcPost<XbarRResult>('/api/spc/xbar-r', payload)
}

/** 分析 I-MR 管制圖 */
export function analyzeIMR(payload: {
  values: number[]
  usl?: number
  lsl?: number
  target?: number
}): Promise<IMRResult> {
  return spcPost<IMRResult>('/api/spc/imr', payload)
}

/** 分析 P 圖 */
export function analyzePChart(payload: {
  defective_counts: number[]
  sample_sizes: number[] | number
}): Promise<AttributeChartResult> {
  return spcPost<AttributeChartResult>('/api/spc/p-chart', payload)
}

/** 分析 C 圖 */
export function analyzeCChart(payload: {
  defect_counts: number[]
}): Promise<AttributeChartResult> {
  return spcPost<AttributeChartResult>('/api/spc/c-chart', payload)
}

/** 計算製程能力指數 */
export function calcCapability(payload: {
  values: number[]
  usl: number
  lsl: number
  target?: number
}): Promise<ProcessCapability> {
  return spcPost<ProcessCapability>('/api/spc/capability', payload)
}

/** 取得 Demo 輸入資料 */
export function getDemoData(chartType: string): Promise<{ chart_type: string; description: string; data: unknown }> {
  return spcGet(`/api/spc/demo/${chartType}`)
}

/** 確認 SPC 服務健康狀態 */
export function checkSpcHealth(): Promise<{ status: string }> {
  return spcGet('/api/spc/health')
}

// ─── Live（真資料）API ───────────────────────────────────────────────────

export type LiveToolItem = { tool_code: string; tool_name: string }

/** 取得 tools 清單（供 Live 模式下拉） */
export function getLiveTools(): Promise<{ tools: LiveToolItem[] }> {
  return spcGet('/api/spc/live/tools')
}

/** 從 DB 拉真資料並計算 I-MR（SPC Live） */
export function analyzeLiveIMR(params: {
  tool_code?: string
  metric?: 'temperature' | 'pressure' | 'yield_rate'
  limit?: number
  usl?: number
  lsl?: number
  target?: number
}): Promise<IMRResult> {
  const q = new URLSearchParams()
  if (params.tool_code) q.set('tool_code', params.tool_code)
  if (params.metric) q.set('metric', params.metric)
  if (params.limit != null) q.set('limit', String(params.limit))
  if (params.usl != null) q.set('usl', String(params.usl))
  if (params.lsl != null) q.set('lsl', String(params.lsl))
  if (params.target != null) q.set('target', String(params.target))

  return spcGet(`/api/spc/live/imr?${q.toString()}`)
}

// ─── 前端內建 Demo 資料（不需後端也能展示）──────────────────────────────
// 為什麼放在前端：
// - 讓 SPC Dashboard 在後端服務未啟動時也能顯示圖表，方便 demo / 開發。
// - 這些資料與後端 demo_data.py 的 gen_* 函式結果一致（同一份 seed 邏輯）。

export const DEMO_XBAR_R = {
  subgroup_size: 5,
  usl: 105,
  lsl: 95,
  target: 100,
  // 30 組子群（每組 5 個，共 150 個值）：前15穩定 → 7組漂移 → 1組超限 → 7組回穩
  values: [
    // 前15組穩定（μ=100, σ≈1）
    100.2, 99.5, 100.8, 99.1, 101.0,
     99.7, 100.3, 98.9, 101.2,  99.8,
    101.1,  99.6, 100.5, 100.0,  99.3,
    100.4,  99.9, 101.3,  98.8, 100.7,
    100.6,  99.4, 100.9,  99.2, 101.1,
    100.0, 100.2,  99.7, 100.5,  99.8,
     99.5, 101.0, 100.3,  99.6, 100.8,
    100.1,  99.3, 100.7, 100.0,  99.5,
    100.9,  99.8, 101.0, 100.2,  99.6,
    100.3,  99.7, 100.5,  99.9, 101.2,
    100.4, 100.1,  99.6, 100.8,  99.4,
    100.7,  99.5, 101.1, 100.0,  99.9,
    100.2,  99.8, 100.6,  99.3, 100.9,
     99.6, 100.4, 100.1,  99.7, 101.0,
    100.5,  99.4, 100.8, 100.0,  99.6,
    // 7組漂移（均值逐步+0.4）
    100.8, 101.5, 100.2, 101.0, 100.9,
    101.5, 102.1, 101.0, 101.8, 101.2,
    102.0, 102.8, 101.5, 102.3, 101.9,
    102.5, 103.0, 102.1, 102.8, 102.4,
    103.1, 103.8, 102.5, 103.4, 102.9,
    103.8, 104.2, 103.0, 104.0, 103.5,
    104.2, 104.8, 103.6, 104.5, 104.0,
    // 1組超限（均值≈104.5）
    104.8, 105.2, 104.6, 105.0, 104.9,
    // 7組回穩
    100.1,  99.8, 100.5,  99.3, 100.7,
    100.3,  99.6, 100.8,  99.5, 100.4,
     99.9, 100.2, 100.6,  99.7, 100.1,
    100.5,  99.4, 100.9, 100.0,  99.6,
    100.2,  99.8, 100.4,  99.9, 100.7,
    100.6,  99.5, 100.3, 100.1,  99.8,
     99.9, 100.4, 100.7,  99.6, 100.2,
  ],
}

export const DEMO_IMR = {
  usl: 58,
  lsl: 42,
  target: 50,
  values: [
    // 前20點穩定（μ=50, σ≈2）
    50.2, 48.9, 51.3, 49.5, 50.8, 51.2, 49.1, 50.5, 48.7, 51.0,
    50.3, 49.6, 51.1, 49.8, 50.6, 51.4, 49.3, 50.7, 49.0, 50.9,
    // 連7點偏高（μ=54，模擬規則2）
    54.1, 53.8, 54.5, 53.6, 54.8, 53.9, 54.3,
    // 超限點（≈58.6）
    58.6,
    // 後12點回穩
    50.1, 49.7, 50.4, 51.0, 49.2, 50.8, 49.5, 51.3, 50.0, 49.9, 50.6, 51.1,
  ],
}

export const DEMO_P_CHART = {
  sample_sizes: 200,
  defective_counts: [
    // 前15批次穩定（不良率≈3%）
    6, 7, 5, 8, 6, 7, 5, 6, 8, 5, 7, 6, 5, 7, 6,
    // 第16-20批次惡化（≈6-8%）
    13, 15, 14, 16, 14,
    // 超限批次（≈12%）
    24,
    // 後4批次回穩
    6, 7, 5, 6,
  ],
}

export const DEMO_C_CHART = {
  defect_counts: [
    // 前18個穩定（平均≈4）
    4, 3, 5, 4, 3, 5, 4, 4, 3, 5, 4, 3, 5, 4, 4, 3, 5, 4,
    // 偏高段（模擬規則6：5點4個超±1σ）
    7, 8, 7, 9, 8,
    // 超限
    18,
    // 回穩
    4, 3,
  ],
}
