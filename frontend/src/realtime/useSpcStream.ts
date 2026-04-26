// useSpcStream：訂閱 SPC Hub、收 spcPoint / spcViolation 的 React hook。
//
// 為什麼做成 hook：
// - 元件層只關心「現在最新一批點是什麼」，不該去管 SignalR 連線生命週期；
//   hook 把連線、加入 group、收訊息、cleanup 都包起來。
// - 同樣模式之後可複製成 useAlarmStream / useWorkorderStream，重用骨架。
//
// 解決什麼問題：
// - 前端從「打 REST 取 demo」變成「被 push 真實資料」的最小切入點。

import { useEffect, useRef, useState } from 'react'
import { HubConnection, HubConnectionState } from '@microsoft/signalr'
import { createHubConnection, safeStopHub } from './signalr'

/**
 * 對齊後端 <c>SpcPointPayload</c>（C# record）的前端型別。
 *
 * 為什麼用 camelCase：
 * - SignalR 預設用 camelCase serializer（見 ASP.NET Core 預設 SystemTextJsonHubProtocol）。
 *   配合好之後資料看起來就是「JSON 標準款」。
 */
export type SpcPointPayload = {
  lineCode: string
  toolCode: string
  parameterCode: string
  timestamp: string
  value: number
  mean: number
  sigma: number
  ucl: number
  cl: number
  lcl: number
  cpk: number | null
  violations: SpcRuleViolation[]
}

export type SpcRuleViolation = {
  ruleId: number
  ruleName: string
  severity: 'red' | 'yellow' | 'green' | string
  description: string
}

/**
 * SPC Hub 連線狀態 + 最新一段點 buffer。
 */
export type UseSpcStreamResult = {
  connectionState: HubConnectionState | 'idle'
  /** 最新累積的點，超過 maxPoints 會自動裁掉前面 */
  points: SpcPointPayload[]
  /** SPC 違規事件（紅 / 黃燈），最新在前 */
  violations: SpcPointPayload[]
}

export type UseSpcStreamOptions = {
  /** 訂閱的產線代碼，例如 SMT / AOI；空字串代表先連線但不加 group */
  lineCode: string
  /** 訂閱的 SPC 參數代碼，例如 yield_rate */
  parameterCode: string
  /** 視窗大小，預設 100 點，避免 UI memory 暴增 */
  maxPoints?: number
  /** 違規 buffer 大小，預設 50 */
  maxViolations?: number
}

/**
 * 訂閱 SPC Hub 並維護一份滑動視窗 buffer 的 hook。
 *
 * 為什麼用 ref 暫存最新陣列：
 * - 避免 setState 連續觸發重渲染導致 dropped frame；
 *   實作上每進來一筆 push 進 ref，再以 setState 換 reference 一次即可。
 */
export function useSpcStream(opts: UseSpcStreamOptions): UseSpcStreamResult {
  const { lineCode, parameterCode, maxPoints = 100, maxViolations = 50 } = opts
  const [connectionState, setConnectionState] = useState<HubConnectionState | 'idle'>('idle')
  const [points, setPoints] = useState<SpcPointPayload[]>([])
  const [violations, setViolations] = useState<SpcPointPayload[]>([])
  const connectionRef = useRef<HubConnection | null>(null)

  useEffect(() => {
    let cancelled = false

    async function connect() {
      try {
        const conn = await createHubConnection('spc')
        if (cancelled) {
          await safeStopHub(conn)
          return
        }

        connectionRef.current = conn
        setConnectionState(conn.state)

        conn.onreconnecting(() => setConnectionState(HubConnectionState.Reconnecting))
        conn.onreconnected(async () => {
          setConnectionState(HubConnectionState.Connected)
          // 重連後重新加入 group：SignalR 不會幫我們記得之前的 group
          if (lineCode && parameterCode) {
            try {
              await conn.invoke('JoinGroup', lineCode, parameterCode)
            } catch {
              // 忽略；下次 effect 變化會再試一次
            }
          }
        })
        conn.onclose(() => setConnectionState(HubConnectionState.Disconnected))

        conn.on('spcPoint', (payload: SpcPointPayload) => {
          setPoints((prev) => {
            const next = [...prev, payload]
            // 為什麼用 splice 替代 slice：
            // - 大多時候只超出 1~2 點，splice 比 slice 省記憶體拷貝。
            if (next.length > maxPoints) next.splice(0, next.length - maxPoints)
            return next
          })
        })
        conn.on('spcViolation', (payload: SpcPointPayload) => {
          setViolations((prev) => {
            const next = [payload, ...prev]
            if (next.length > maxViolations) next.length = maxViolations
            return next
          })
        })

        if (lineCode && parameterCode) {
          await conn.invoke('JoinGroup', lineCode, parameterCode)
        }
      } catch (err) {
        // 為什麼只是 console.warn：
        // - 開發階段 backend 還沒起時，前端不應該整頁壞掉；
        //   重連邏輯由 SignalR 自動處理，這裡記一下就好。
        console.warn('[useSpcStream] 連線失敗：', err)
        setConnectionState(HubConnectionState.Disconnected)
      }
    }

    void connect()

    return () => {
      cancelled = true
      const conn = connectionRef.current
      connectionRef.current = null
      void (async () => {
        if (conn && lineCode && parameterCode) {
          try {
            await conn.invoke('LeaveGroup', lineCode, parameterCode)
          } catch {
            // 忽略
          }
        }
        await safeStopHub(conn)
      })()
    }
  }, [lineCode, parameterCode, maxPoints, maxViolations])

  return { connectionState, points, violations }
}
