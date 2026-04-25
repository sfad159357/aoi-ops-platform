/**
 * SpcDashboard（SPC 統計製程管制主頁面）
 *
 * 為什麼要獨立一個 page：
 * - SPC 是製程監控的核心功能，值得有一個完整的專屬頁面。
 * - 把 Xbar-R、I-MR、P 圖、C 圖、製程能力指數整合在同一頁，讓工程師一站式查看。
 *
 * 解決什麼問題：
 * - 前端 Demo 模式（不需後端）：頁面載入就能看到完整圖表，適合 portfolio 展示。
 * - 後端模式（有 SPC Service）：支援送出真實資料計算。
 * - 兩種模式用 TAB 切換，讓展示更彈性。
 *
 * 頁面結構：
 * ┌─ 頁籤：Xbar-R ｜ I-MR ｜ P圖 ｜ C圖 ─────────────────┐
 * │  ┌─ 上半：管制圖（2張，主圖+輔圖） ──────────────────┐  │
 * │  │  ControlChart（Xbar 或 I）                        │  │
 * │  │  ControlChart（R 或 MR）                          │  │
 * │  └────────────────────────────────────────────────── ┘  │
 * │  ┌─ 下半：左側製程能力卡片 ｜ 右側八大規則表 ─────────┐  │
 * │  └────────────────────────────────────────────────── ┘  │
 * └─────────────────────────────────────────────────────────┘
 */

import { useState, useCallback, useEffect } from 'react'
import ControlChart from '../components/spc/ControlChart'
import ProcessCapabilityCard from '../components/spc/ProcessCapabilityCard'
import RulesViolationTable from '../components/spc/RulesViolationTable'
import {
  analyzeXbarR, analyzeIMR, analyzePChart, analyzeCChart,
  analyzeLiveIMR, getLiveTools,
  DEMO_XBAR_R, DEMO_IMR, DEMO_P_CHART, DEMO_C_CHART,
} from '../api/spc'
import type { XbarRResult, IMRResult, AttributeChartResult } from '../api/spc'

// ─── 頁籤定義 ────────────────────────────────────────────────────────────

type TabId = 'xbar-r' | 'imr' | 'p-chart' | 'c-chart'

const TABS: { id: TabId; label: string; desc: string }[] = [
  { id: 'xbar-r',   label: 'Xbar-R 圖',  desc: '計量型｜子群均值與全距（subgroup n=5）' },
  { id: 'imr',      label: 'I-MR 圖',    desc: '計量型｜個別值與移動全距（subgroup n=1）' },
  { id: 'p-chart',  label: 'P 圖',       desc: '計數型｜不良品比例（P Chart）' },
  { id: 'c-chart',  label: 'C 圖',       desc: '計數型｜單位缺陷數（C Chart）' },
]

// ─── 主頁面元件 ──────────────────────────────────────────────────────────

export default function SpcDashboard() {
  const [activeTab, setActiveTab] = useState<TabId>('xbar-r')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // 各圖表結果
  const [xbarRResult, setXbarRResult]   = useState<XbarRResult | null>(null)
  const [imrResult,   setImrResult]     = useState<IMRResult | null>(null)
  const [pResult,     setPResult]       = useState<AttributeChartResult | null>(null)
  const [cResult,     setCResult]       = useState<AttributeChartResult | null>(null)

  // 模式：demo（前端內建）/ api（打後端，demo payload）/ live（真資料：DB→SPC）
  const [mode, setMode] = useState<'demo' | 'api' | 'live'>('demo')

  // Live 模式控制項（先針對 I-MR 走通：process_runs 的 temperature/pressure/yield_rate）
  const [liveTools, setLiveTools] = useState<Array<{ tool_code: string; tool_name: string }>>([])
  const [liveToolCode, setLiveToolCode] = useState<string>('') // 空字串代表不篩 tool（全體）
  const [liveMetric, setLiveMetric] = useState<'temperature' | 'pressure' | 'yield_rate'>('temperature')
  const [liveLimit, setLiveLimit] = useState<number>(60)

  /**
   * 執行 Demo 模式分析：用前端內建資料打後端計算。
   * 若後端不通則顯示錯誤提示，不影響 Demo 資料展示。
   *
   * 為什麼要打後端而不是純前端計算：
   * - 展示「後端 SPC 服務真的能計算」是 portfolio 的重點。
   * - 但萬一後端沒起來，Demo 資料仍能讓前端正常運作（見 fallback logic）。
   */
  const runAnalysis = useCallback(async (tab: TabId) => {
    setLoading(true)
    setError(null)
    try {
      if (mode === 'live') {
        // Live 模式（真資料）：目前先支援 I-MR，讓 SPC 可以直接吃 Kafka→DB 的資料
        if (tab !== 'imr') {
          throw new Error('Live 模式目前先提供 I-MR（下一步再加 Xbar-R/P/C）')
        }
        const res = await analyzeLiveIMR({
          tool_code: liveToolCode || undefined,
          metric: liveMetric,
          limit: liveLimit,
        })
        setImrResult(res)
        return
      }

      if (tab === 'xbar-r') {
        const res = await analyzeXbarR(DEMO_XBAR_R)
        setXbarRResult(res)
      } else if (tab === 'imr') {
        const res = await analyzeIMR(DEMO_IMR)
        setImrResult(res)
      } else if (tab === 'p-chart') {
        const res = await analyzePChart(DEMO_P_CHART)
        setPResult(res)
      } else if (tab === 'c-chart') {
        const res = await analyzeCChart(DEMO_C_CHART)
        setCResult(res)
      }
    } catch (e) {
      setError(
        e instanceof Error
          ? `SPC 計算失敗：${e.message}\n\n` +
            `若是 422（資料不足），代表 DB 裡的 process_runs 還不夠點數。\n` +
            `請啟動：data-simulator + kafka-db-writer（Kafka→DB 落地），跑 1–2 分鐘後再試。\n` +
            `若是連線錯誤，請確認 spc-service 是否在 port 8001。`
          : String(e)
      )
    } finally {
      setLoading(false)
    }
  }, [mode, liveToolCode, liveMetric, liveLimit])

  // Live 模式載入 tools（下拉用）
  useEffect(() => {
    if (mode !== 'live') return
    getLiveTools()
      .then((r) => setLiveTools(r.tools))
      .catch(() => setLiveTools([]))
  }, [mode])

  // 頁籤切換時自動載入 Demo 資料
  useEffect(() => {
    if (mode === 'api' || mode === 'live') {
      void runAnalysis(activeTab)
    }
  }, [activeTab, mode, runAnalysis])

  // 初次載入就跑一次 Xbar-R Demo
  useEffect(() => {
    void runAnalysis('xbar-r')
  }, [runAnalysis])

  return (
    <div style={{ maxWidth: 1100, margin: '0 auto', padding: '20px 16px' }}>
      {/* 頁面標題 */}
      <div style={{ marginBottom: 20 }}>
        <h1 style={{ margin: 0, fontSize: 22, color: '#f9fafb', fontWeight: 700 }}>
          SPC 統計製程管制
        </h1>
        <p style={{ margin: '6px 0 0', fontSize: 13, color: '#6b7280' }}>
          Statistical Process Control — 計量型 / 計數型管制圖、八大規則偵測、製程能力指數（Ca/Cp/Cpk）
        </p>
      </div>

      {/* 模式切換 */}
      <div style={{ display: 'flex', gap: 8, marginBottom: 16, alignItems: 'center' }}>
        <span style={{ fontSize: 12, color: '#6b7280' }}>資料來源：</span>
        {(['demo', 'api', 'live'] as const).map((m) => (
          <button
            key={m}
            onClick={() => {
              setMode(m)
              if (m === 'api' || m === 'live') void runAnalysis(activeTab)
            }}
            style={{
              padding: '4px 14px',
              borderRadius: 6,
              border: mode === m ? '1px solid #3b82f6' : '1px solid #374151',
              background: mode === m ? '#1e3a5f' : '#1f2937',
              color: mode === m ? '#60a5fa' : '#9ca3af',
              fontSize: 12,
              cursor: 'pointer',
              fontWeight: mode === m ? 600 : 400,
            }}
          >
            {m === 'demo'
              ? '🖥 前端 Demo（內建資料）'
              : m === 'api'
                ? '🔌 API 模式（Demo payload→後端計算）'
                : '🟢 Live（Kafka→DB→SPC）'}
          </button>
        ))}
        {(mode === 'api' || mode === 'live') && (
          <button
            onClick={() => void runAnalysis(activeTab)}
            disabled={loading}
            style={{
              padding: '4px 12px',
              borderRadius: 6,
              border: '1px solid #374151',
              background: '#111827',
              color: '#9ca3af',
              fontSize: 12,
              cursor: loading ? 'not-allowed' : 'pointer',
            }}
          >
            {loading ? '計算中…' : '↺ 重新計算'}
          </button>
        )}
      </div>

      {/* Live 模式控制項（目前只針對 I-MR） */}
      {mode === 'live' && (
        <div style={{
          background: '#111827',
          border: '1px solid #374151',
          borderRadius: 10,
          padding: '12px 16px',
          marginBottom: 16,
          display: 'flex',
          gap: 12,
          flexWrap: 'wrap',
          alignItems: 'center',
        }}>
          <div style={{ fontSize: 12, color: '#9ca3af', fontWeight: 600 }}>Live 設定</div>

          <label style={{ fontSize: 12, color: '#9ca3af' }}>
            Tool：
            <select
              value={liveToolCode}
              onChange={(e) => setLiveToolCode(e.target.value)}
              style={{ marginLeft: 6, background: '#0d1117', color: '#e5e7eb', border: '1px solid #374151', borderRadius: 6, padding: '4px 8px' }}
            >
              <option value="">（全部）</option>
              {liveTools.map((t) => (
                <option key={t.tool_code} value={t.tool_code}>
                  {t.tool_code} — {t.tool_name}
                </option>
              ))}
            </select>
          </label>

          <label style={{ fontSize: 12, color: '#9ca3af' }}>
            Metric：
            <select
              value={liveMetric}
              onChange={(e) => setLiveMetric(e.target.value as typeof liveMetric)}
              style={{ marginLeft: 6, background: '#0d1117', color: '#e5e7eb', border: '1px solid #374151', borderRadius: 6, padding: '4px 8px' }}
            >
              <option value="temperature">temperature</option>
              <option value="pressure">pressure</option>
              <option value="yield_rate">yield_rate</option>
            </select>
          </label>

          <label style={{ fontSize: 12, color: '#9ca3af' }}>
            Limit：
            <input
              type="number"
              min={10}
              max={500}
              value={liveLimit}
              onChange={(e) => setLiveLimit(Number(e.target.value))}
              style={{ marginLeft: 6, width: 90, background: '#0d1117', color: '#e5e7eb', border: '1px solid #374151', borderRadius: 6, padding: '4px 8px' }}
            />
          </label>

          <button
            onClick={() => {
              setActiveTab('imr')
              void runAnalysis('imr')
            }}
            style={{ padding: '5px 12px', borderRadius: 8, border: '1px solid #3b82f6', background: '#1e3a5f', color: '#60a5fa', fontSize: 12, cursor: 'pointer', fontWeight: 600 }}
          >
            套用並計算 I‑MR
          </button>

          <div style={{ fontSize: 11, color: '#6b7280' }}>
            資料來源：PostgreSQL `process_runs`（由 Kafka `aoi.inspection.raw` 落地）
          </div>
        </div>
      )}

      {/* 錯誤提示 */}
      {error && (
        <div style={{
          background: '#450a0a',
          border: '1px solid #7f1d1d',
          borderRadius: 8,
          padding: '12px 16px',
          marginBottom: 16,
          fontSize: 12,
          color: '#fca5a5',
          whiteSpace: 'pre-wrap',
        }}>
          {error}
        </div>
      )}

      {/* 圖表類型頁籤 */}
      <div style={{ display: 'flex', gap: 4, marginBottom: 20, borderBottom: '1px solid #374151' }}>
        {TABS.map((t) => (
          <button
            key={t.id}
            onClick={() => setActiveTab(t.id)}
            style={{
              padding: '8px 16px',
              border: 'none',
              borderBottom: activeTab === t.id ? '2px solid #3b82f6' : '2px solid transparent',
              background: 'transparent',
              color: activeTab === t.id ? '#60a5fa' : '#6b7280',
              fontSize: 13,
              fontWeight: activeTab === t.id ? 600 : 400,
              cursor: 'pointer',
              marginBottom: -1,
            }}
          >
            {t.label}
          </button>
        ))}
      </div>

      {/* 圖表說明 */}
      <div style={{ fontSize: 12, color: '#6b7280', marginBottom: 16 }}>
        {TABS.find((t) => t.id === activeTab)?.desc}
      </div>

      {/* 圖表主體 */}
      <XbarRTab result={xbarRResult} active={activeTab === 'xbar-r'} loading={loading && activeTab === 'xbar-r'} />
      <IMRTab   result={imrResult}   active={activeTab === 'imr'}    loading={loading && activeTab === 'imr'} />
      <PTab     result={pResult}     active={activeTab === 'p-chart'} loading={loading && activeTab === 'p-chart'} />
      <CTab     result={cResult}     active={activeTab === 'c-chart'} loading={loading && activeTab === 'c-chart'} />
    </div>
  )
}

// ─── 各圖表頁籤內容 ──────────────────────────────────────────────────────

function XbarRTab({ result, active, loading }: { result: XbarRResult | null; active: boolean; loading: boolean }) {
  if (!active) return null
  if (loading) return <LoadingState />
  if (!result) return <EmptyState label="Xbar-R" />
  return (
    <ChartLayout
      mainChart={
        <>
          <ControlChart
            title="Xbar 圖（子群平均值）"
            points={result.xbar_points}
            limits={result.xbar_limits}
            violations={result.violations}
            yLabel="量測值均值"
            height={200}
          />
          <ControlChart
            title="R 圖（子群全距）"
            points={result.r_points}
            limits={result.r_limits}
            violations={[]}
            yLabel="全距 R"
            height={160}
            showSigmaBands={false}
          />
        </>
      }
      capability={result.capability}
      violations={result.violations}
      stats={[
        { label: '子群大小', value: String(result.subgroup_size) },
        { label: '子群數', value: String(result.total_points) },
        { label: 'Xbar UCL', value: result.xbar_limits.ucl.toFixed(4) },
        { label: 'Xbar CL',  value: result.xbar_limits.cl.toFixed(4) },
        { label: 'Xbar LCL', value: result.xbar_limits.lcl.toFixed(4) },
        { label: 'R UCL',    value: result.r_limits.ucl.toFixed(4) },
        { label: 'R̄',        value: result.r_limits.cl.toFixed(4) },
      ]}
    />
  )
}

function IMRTab({ result, active, loading }: { result: IMRResult | null; active: boolean; loading: boolean }) {
  if (!active) return null
  if (loading) return <LoadingState />
  if (!result) return <EmptyState label="I-MR" />
  return (
    <ChartLayout
      mainChart={
        <>
          <ControlChart
            title="I 圖（個別量測值）"
            points={result.x_points}
            limits={result.x_limits}
            violations={result.violations}
            yLabel="個別值"
            height={200}
          />
          <ControlChart
            title="MR 圖（移動全距）"
            points={result.mr_points}
            limits={result.mr_limits}
            violations={[]}
            yLabel="移動全距"
            height={160}
            showSigmaBands={false}
            yDomain={[0, 'auto']}
          />
        </>
      }
      capability={result.capability}
      violations={result.violations}
      stats={[
        { label: '資料點數', value: String(result.total_points) },
        { label: 'X UCL', value: result.x_limits.ucl.toFixed(4) },
        { label: 'X̄',     value: result.x_limits.cl.toFixed(4) },
        { label: 'X LCL', value: result.x_limits.lcl.toFixed(4) },
        { label: 'MR UCL', value: result.mr_limits.ucl.toFixed(4) },
        { label: 'MR̄',     value: result.mr_limits.cl.toFixed(4) },
      ]}
    />
  )
}

function PTab({ result, active, loading }: { result: AttributeChartResult | null; active: boolean; loading: boolean }) {
  if (!active) return null
  if (loading) return <LoadingState />
  if (!result) return <EmptyState label="P 圖" />
  return (
    <ChartLayout
      mainChart={
        <ControlChart
          title="P 圖（不良品比例）"
          points={result.points}
          limits={result.limits}
          violations={result.violations}
          yLabel="不良率 p"
          height={240}
        />
      }
      violations={result.violations}
      stats={[
        { label: '資料點數', value: String(result.total_points) },
        { label: 'UCL', value: result.limits.ucl.toFixed(4) },
        { label: 'p̄',   value: result.limits.cl.toFixed(4) },
        { label: 'LCL', value: result.limits.lcl.toFixed(4) },
      ]}
    />
  )
}

function CTab({ result, active, loading }: { result: AttributeChartResult | null; active: boolean; loading: boolean }) {
  if (!active) return null
  if (loading) return <LoadingState />
  if (!result) return <EmptyState label="C 圖" />
  return (
    <ChartLayout
      mainChart={
        <ControlChart
          title="C 圖（單位缺陷數）"
          points={result.points}
          limits={result.limits}
          violations={result.violations}
          yLabel="缺陷數 c"
          height={240}
          yDomain={[0, 'auto']}
        />
      }
      violations={result.violations}
      stats={[
        { label: '資料點數', value: String(result.total_points) },
        { label: 'UCL', value: result.limits.ucl.toFixed(3) },
        { label: 'c̄',   value: result.limits.cl.toFixed(3) },
        { label: 'LCL', value: result.limits.lcl.toFixed(3) },
      ]}
    />
  )
}

// ─── 共用佈局元件 ────────────────────────────────────────────────────────

type StatItem = { label: string; value: string }

function ChartLayout({
  mainChart,
  capability,
  violations,
  stats,
}: {
  mainChart: React.ReactNode
  capability?: import('../api/spc').ProcessCapability | null
  violations: import('../api/spc').RuleViolation[]
  stats: StatItem[]
}) {
  return (
    <div>
      {/* 管制圖區 */}
      <div style={{ background: '#111827', border: '1px solid #374151', borderRadius: 10, padding: '16px 20px', marginBottom: 16 }}>
        {mainChart}
      </div>

      {/* 統計摘要列 */}
      <div style={{
        display: 'flex',
        gap: 12,
        marginBottom: 16,
        flexWrap: 'wrap',
      }}>
        {stats.map((s) => (
          <div key={s.label} style={{
            background: '#1f2937',
            border: '1px solid #374151',
            borderRadius: 6,
            padding: '6px 12px',
            fontSize: 12,
            color: '#9ca3af',
          }}>
            {s.label}：<span style={{ color: '#e5e7eb', fontFamily: 'monospace' }}>{s.value}</span>
          </div>
        ))}
      </div>

      {/* 製程能力 + 八大規則 */}
      <div style={{ display: 'grid', gridTemplateColumns: capability ? '1fr 1fr' : '1fr', gap: 16 }}>
        {capability && <ProcessCapabilityCard capability={capability} />}
        <div>
          <RulesViolationTable violations={violations} />
        </div>
      </div>

      {/* 圖例說明 */}
      <Legend />
    </div>
  )
}

function Legend() {
  return (
    <div style={{
      display: 'flex',
      gap: 16,
      flexWrap: 'wrap',
      marginTop: 16,
      padding: '10px 14px',
      background: '#0d1117',
      borderRadius: 8,
      fontSize: 11,
      color: '#6b7280',
    }}>
      <span style={{ fontWeight: 600, color: '#9ca3af' }}>圖例：</span>
      <LegendItem color="#ef4444" dash label="UCL / LCL（±3σ 管制線）" />
      <LegendItem color="#22c55e" label="CL（中心線）" />
      <LegendItem color="#f97316" dash label="+2σ / -2σ" />
      <LegendItem color="#3b82f6" dash label="+1σ / -1σ" />
      <LegendItem dotColor="#ef4444" label="🔴 規則1 超限" />
      <LegendItem dotColor="#f97316" label="🟡 規則2/3/5 趨勢" />
      <LegendItem dotColor="#3b82f6" label="🟢 規則4/6/7/8 提示" />
    </div>
  )
}

function LegendItem({
  color,
  dotColor,
  dash = false,
  label,
}: {
  color?: string
  dotColor?: string
  dash?: boolean
  label: string
}) {
  return (
    <span style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
      {color && (
        <svg width="20" height="10">
          <line
            x1="0" y1="5" x2="20" y2="5"
            stroke={color}
            strokeWidth={2}
            strokeDasharray={dash ? '4 3' : undefined}
          />
        </svg>
      )}
      {dotColor && (
        <svg width="10" height="10">
          <circle cx="5" cy="5" r="4" fill={dotColor} />
        </svg>
      )}
      {label}
    </span>
  )
}

function LoadingState() {
  return (
    <div style={{ textAlign: 'center', padding: '60px 0', color: '#6b7280', fontSize: 14 }}>
      ⏳ 計算中，請稍候…
    </div>
  )
}

function EmptyState({ label }: { label: string }) {
  return (
    <div style={{ textAlign: 'center', padding: '60px 0', color: '#6b7280', fontSize: 13 }}>
      點擊「↺ 重新計算」或切換至 Demo 模式查看 {label} 圖
    </div>
  )
}
