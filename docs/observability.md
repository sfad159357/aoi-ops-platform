# 觀測 — Logging + Metrics + 情境演練

> W11 加上：Serilog 結構化日誌、`/api/metrics` in-memory 快照、SIM_SCENARIO 切換指令。

## Serilog 結構化日誌

後端用 `Serilog.AspNetCore` + `CompactJsonFormatter` 輸出 JSON，每行一筆事件：

```json
{"@t":"2026-04-26T12:30:00Z","@l":"Information","@mt":"Alarm 已寫入並推送：code={Code} severity={Severity} tool={Tool} pushMs={PushMs}","Code":"DEF-0042","Severity":"high","Tool":"AOI-A","PushMs":1.83,"service":"aoiops-backend"}
```

為什麼選 Compact JSON：

- 每行一筆，方便 `docker logs aoiops-backend-1 | jq` 直接 pipe
- 未來接 Loki / ELK 不用改程式
- HTTP request 也會 enrich `RequestPath` / `StatusCode` / `Elapsed`（透過 `UseSerilogRequestLogging`）

關鍵欄位：

- `service`：固定 `aoiops-backend`，方便多服務蒐集後 group by
- workers 加了 `pushMs`：方便 grep `"pushMs"` 看 SignalR 推播延遲
- `@l`：log level；`@mt`：message template，可做模板化彙整

調整 LogLevel：改 `appsettings.json` 的 `Logging:LogLevel` 區塊（容器重啟後生效）。

## In-memory metrics — `GET /api/metrics`

> 為什麼不直接接 prometheus-net：W11 目標只要「能驗證 SignalR push 延遲、違規事件數」；
> 自己實作的 in-memory 收集器 + 一個 controller 已足夠 demo。未來換 prometheus 只需替換 `IRealtimeMetrics` 實作。

```bash
curl -s http://localhost:8080/api/metrics | jq
```

```json
{
  "startedAt": "2026-04-26T12:00:00Z",
  "uptimeSeconds": 612.41,
  "spc": {
    "total": 4521,
    "violations": 38,
    "meanLatencyMs": 1.32,
    "p50LatencyMs": 0.94,
    "p95LatencyMs": 3.18,
    "eventsLast60s": 240
  },
  "alarm": {
    "total": 21,
    "violations": null,
    "meanLatencyMs": 1.07,
    "p50LatencyMs": 0.85,
    "p95LatencyMs": 2.30,
    "eventsLast60s": 4
  },
  "workorder": {
    "total": 6,
    "violations": null,
    "meanLatencyMs": 1.15,
    "p50LatencyMs": 0.92,
    "p95LatencyMs": 2.50,
    "eventsLast60s": 1
  }
}
```

欄位語意：

| 欄位 | 說明 |
|---|---|
| `total` | 自啟動以來累計推播事件數（含正常 + 違規） |
| `violations` | 僅 SPC 用：累計違規次數 |
| `meanLatencyMs` / `p50` / `p95` | 最近 1024 筆樣本的 SignalR push 耗時 |
| `eventsLast60s` | 最近 60 秒的事件數，可估吞吐 |

實作：
- 介面：[`backend/src/Application/Observability/IRealtimeMetrics.cs`](../backend/src/Application/Observability/IRealtimeMetrics.cs)
- 實作：[`backend/src/Api/Observability/RealtimeMetricsService.cs`](../backend/src/Api/Observability/RealtimeMetricsService.cs)
- Controller：`MetricsController.Get` → `RealtimeMetricsSnapshot`
- 注入點：
  - `SpcRealtimeWorker.HandleAsync`：`Stopwatch` 量 PushSpcPointAsync + PushSpcViolationAsync
  - `AlarmRabbitWorker` / `WorkorderRabbitWorker`：DB 落地後才計時，純量 push 段落

## SIM_SCENARIO 情境演練

`services/ingestion` 用 `SIM_SCENARIO` 切換假資料型態，方便 demo 時讓 SPC 觸發不同規則：

| 情境 | 行為 | 預期觸發的規則（Western Electric） |
|---|---|---|
| `normal` | 小幅高斯雜訊 | 偶發 Rule5（連續 4/5 點 > 1σ）|
| `drift` | 平均逐點偏移 | Rule2（連續 8 點同側）/ Rule3（連續 6 點同方向） |
| `spike` | 每 25 點注入 ±3σ 跳點 | Rule1（單點超 ±3σ）|
| `misjudge` | 噪音變大 | Rule5 / Rule6（連續 2/3 點 > 2σ） |

切換指令（會重啟 `ingestion` 容器）：

```bash
make scenario-drift
make scenario-spike
make scenario-misjudge
make scenario-normal
```

驗證流程：

1. `make scenario-spike`
2. 打開前端 `http://localhost:5173` 進 SPC Dashboard
3. 觀察 X̄ 圖出現紅點（Rule1 違規）+ 違規表新增列
4. 同時打 `curl -s http://localhost:8080/api/metrics | jq .spc` 看 `violations` 累計增加

## 容器層 healthcheck

W10 已為下列容器加上 `healthcheck`：

| 容器 | 檢查方式 | 為什麼 |
|---|---|---|
| `db` | `pg_isready` | Postgres 啟動到接受連線之間有 5–10 秒空檔 |
| `kafka` | `kafka-broker-api-versions.sh` | 純 TCP 接得上不代表 KRaft 選舉完成 |
| `rabbitmq` | `rabbitmq-diagnostics ping` | management UI 通就代表 broker ready |
| `influxdb` | `wget /health` | InfluxDB 2.x 內建 `/health` |
| `backend` | `curl /api/health/db` | 同時驗證 backend + DB 連線 |

`backend` 與 Python workers 都改成 `condition: service_healthy` 等 broker / DB 真的 ready 才啟動，
避免 demo 時看到「容器活著但啥也沒收」。
