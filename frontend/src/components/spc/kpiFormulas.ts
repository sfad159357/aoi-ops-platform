// kpiFormulas：SPC 儀表板各 KPI 的公式說明（點開 card 的 modal 用）。
//
// 為什麼集中成表：與 SpcDashboard / spcKpiContext 實作對齊，單一處更新文案避免與畫面邏輯飄移。
// 語氣方針：口語、直接，工程師一眼就知道「這數字怎麼來的」，不需要交代技術細節。

export type KpiFormulaBlock = { title: string; lines: string[] }

export const KPI_FORMULA_BY_KEY: Record<string, KpiFormulaBlock> = {
  yield_rate: {
    title: '今日良率',
    lines: [
      '公式：(今日累積件 − 今日違規件) ÷ 今日累積件 × 100%',
      '「今日違規件」= 今天內、超出管制線（UCL/LCL）或觸發 Nelson Rule 的件數。',
      '若今日無資料則顯示「—」。',
    ],
  },
  cpk: {
    title: 'Cpk 製程能力指數',
    lines: [
      'Cpk = min( CPU, CPL )',
      'CPU = (USL − 平均) ÷ (3σ)　CPL = (平均 − LSL) ÷ (3σ)',
      'σ 由目前視窗的樣本標準差計算，視窗滿點前數值會波動。',
      'Cpk ≥ 1.33 → 製程良好　1.00 ~ 1.33 → 勉強合格　< 1.00 → 需改善',
    ],
  },
  violation_today: {
    title: '今日違規數',
    lines: [
      '今天之內，超出管制線（value > UCL 或 value < LCL）或觸發 Nelson Rule 的件數。',
      '永遠 ≤ 今日累積件數；違規件 ÷ 累積件 ≈ (1 − 良率)，可互相對帳。',
    ],
  },
  panels_per_hour: {
    title: '秒均產出',
    lines: [
      '公式：視窗累積件數 ÷ 首末觀測點時間差（秒）',
      '至少要有 2 筆觀測點才計算；時間差不足 60 秒時以 60 秒計（避免除出天文數字）。',
    ],
  },
  cumulative_output: {
    title: '累積產出',
    lines: [
      '= 目前視窗內所有觀測點的 inspected_qty 加總。',
      '預設每點 1 件，所以通常等於圖上點數；批量送出時（inspected_qty > 1）會大於點數。',
    ],
  },
}
