// FilterBar：SPC Dashboard 上方的「產線 / 機台 / 參數」過濾器。
//
// 為什麼用三個 select：
// - HTML 設計圖中的篩選器是水平 toolbar；
//   <select> 是無障礙最高的選擇方式，不用自製 dropdown。
// - 變動任一欄位都觸發 SignalR group 重新訂閱，邏輯由 parent 處理，這個元件只負責 UI。
//
// 解決什麼問題：
// - 把產線清單、參數清單從 profile 拉出來填入 select；
//   切換產業時自動帶不同選項，不需要改 code。

import type { DomainLine, DomainParameter } from '../../domain/profile'

export type FilterBarValue = {
  lineCode: string
  toolCode: string
  parameterCode: string
}

type Props = {
  lines: DomainLine[]
  /** 機台列表；先不從 profile 拿（profile 沒列），由父層用 /api/tools 取 */
  tools: { code: string; label: string }[]
  parameters: DomainParameter[]
  value: FilterBarValue
  onChange: (next: FilterBarValue) => void
}

export default function FilterBar({ lines, tools, parameters, value, onChange }: Props) {
  return (
    <div
      style={{
        display: 'flex',
        gap: 12,
        alignItems: 'center',
        marginBottom: 16,
        background: '#161b22',
        border: '1px solid #21262d',
        borderRadius: 8,
        padding: '12px 16px',
        fontFamily: 'JetBrains Mono, ui-monospace, monospace',
        fontSize: 13,
      }}
    >
      <span style={{ color: '#9ca3af' }}>篩選</span>
      <Select
        label="產線"
        value={value.lineCode}
        onChange={(v) => onChange({ ...value, lineCode: v })}
        options={[
          { v: '', label: '— 全部 —' },
          ...lines.map((l) => ({ v: l.code, label: `${l.code} ${l.labelZh}` })),
        ]}
      />
      <Select
        label="機台"
        value={value.toolCode}
        onChange={(v) => onChange({ ...value, toolCode: v })}
        options={[
          { v: '', label: '— 全部 —' },
          ...tools.map((t) => ({ v: t.code, label: `${t.code}${t.label ? ` ${t.label}` : ''}` })),
        ]}
      />
      <Select
        label="參數"
        value={value.parameterCode}
        onChange={(v) => onChange({ ...value, parameterCode: v })}
        options={parameters.map((p) => ({
          v: p.code,
          label: `${p.labelZh}（${p.unit}）`,
        }))}
      />
    </div>
  )
}

function Select(props: {
  label: string
  value: string
  onChange: (v: string) => void
  options: { v: string; label: string }[]
}) {
  return (
    <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
      <span style={{ color: '#6b7280' }}>{props.label}</span>
      <select
        value={props.value}
        onChange={(e) => props.onChange(e.target.value)}
        style={{
          background: '#0d1117',
          color: '#e5e7eb',
          border: '1px solid #21262d',
          borderRadius: 6,
          padding: '4px 8px',
          fontFamily: 'inherit',
          fontSize: 13,
        }}
      >
        {props.options.map((o) => (
          <option key={o.v} value={o.v}>
            {o.label}
          </option>
        ))}
      </select>
    </label>
  )
}
