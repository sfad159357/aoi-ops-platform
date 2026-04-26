# API Spec

> 後端：ASP.NET Core 8（port 8080）。Swagger：<http://localhost:8080/swagger>。
>
> 即時推播走 SignalR Hub，不在這份 REST API 規格裡，請見 [realtime-signalr.md](realtime-signalr.md)。

## REST 資源

### Meta（domain profile）

- `GET /api/meta/profile` — 回傳當前啟用的 domain profile JSON（pcb 或 semiconductor）。前端啟動時 fetch 一次。

### 業務資料

- `GET /api/lots`
- `GET /api/lots/{id}`
- `GET /api/tools`
- `GET /api/wafers`
- `GET /api/recipes`
- `GET /api/process-runs`
- `GET /api/defects`
- `GET /api/defects/{id}/images`
- `GET /api/defects/{id}/reviews`

### 異常記錄（W07）

- `GET /api/alarms?take=100` — 倒序回最近 N 筆。前端事件驅動頁面用來預載歷史。

### 工單管理（W07）

- `GET /api/workorders?take=100` — 倒序回最近 N 筆。前端事件驅動頁面用來預載歷史。

### 物料追溯（W08）

- `GET /api/trace/panels/recent?take=20` — 回最近建立的板（含 panel_no / lot_no），前端 dropdown 用。
- `GET /api/trace/panel/{panelNo}` — 回單一板的完整追溯資料：
  - `panel`：板基本資料
  - `stationTimeline`：6 站時間軸
  - `materials`：使用的物料批號清單
  - `sameLotPanels` / `sameMaterialPanels`：相關板（最多 50 張）

### 健康檢查

- `GET /api/health/db` — 確認 PostgreSQL 可用

## SignalR Hub（即時推播）

| Hub Path | 訊息 | 來源 worker |
|---|---|---|
| `/hubs/spc` | `spcPoint`（SpcPointPayload） / `spcViolation`（同上但帶 violations） | .NET `SpcRealtimeWorker` |
| `/hubs/alarm` | `alarm`（AlarmEvent） | .NET `AlarmRabbitWorker` |
| `/hubs/workorder` | `workorder`（WorkorderEvent） | .NET `WorkorderRabbitWorker` |

訊息格式詳見 [realtime-signalr.md](realtime-signalr.md)。

## CORS

允許的前端 origin 由 `appsettings.Cors:AllowedOrigins` 設定，預設包含 `http://localhost:5173`（Vite dev server）。
