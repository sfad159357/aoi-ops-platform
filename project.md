# AOI Ops Platform

## 定位

模擬 **PCB SMT 產線** 的 MES 品質模組（錫膏印刷 → 貼片 → 回焊 → AOI → ICT → FQC），
以 **Kafka 即時推播 + .NET Core SignalR** 為骨幹，
即時計算 SPC 八大規則 / Cpk，並以事件驅動方式串接 **工單 / 異常 / 物料追溯** 三組業務模組。

主軸：**即時生產 → 儲存 → 消費 → API 回傳 → 即時監控 → 即時運算 → 業務模組 → 可追溯查表**。

> 同一份 codebase 透過 **Domain Profile** 機制（`shared/domain-profiles/{profile}.json`），可切換 PCB / 半導體用語與規格。
> 不做 ML / RAG / Knowledge Copilot，不使用 MQTT / OPC-UA。

---

## 一、整體架構

### 1) OT 設備層（現場 / 模擬）

`services/ingestion`（Python）：

- 模擬 AOI / SPI / Reflow 等設備持續發送檢測資料；
- 直接把訊息打到 Kafka，**不經 MQTT / OPC-UA**。
- 支援 `SIM_SCENARIO=normal|drift|spike|misjudge` 切換情境，方便 demo / 壓測。

### 2) 事件串流層（Kafka）

- `aoi.inspection.raw`：原始檢測資料（溫度 / 壓力 / 良率 / 缺陷）
- `aoi.defect.event`：高嚴重度 defect 事件，觸發業務分流

為什麼要 Kafka：多消費群（InfluxDB / .NET SignalR / Python rabbitmq-publisher）可獨立並行消費同一份資料流，重啟也能 replay。

### 3) IT 應用層

| Worker | 來源 | 落點 |
|---|---|---|
| Python `kafka-influx-writer` | Kafka `aoi.inspection.raw` | InfluxDB（時序）|
| Python `kafka-rabbitmq-publisher` | Kafka `aoi.defect.event` | RabbitMQ alert / workorder |
| .NET `SpcRealtimeWorker` | Kafka `aoi.inspection.raw` | SignalR `/hubs/spc`（即時 SPC 點 + 違規）|
| .NET `AlarmRabbitWorker` | RabbitMQ `alert` | SQL Server `alarms` + SignalR `/hubs/alarm` |
| .NET `WorkorderRabbitWorker` | RabbitMQ `workorder` | SQL Server `workorders` + SignalR `/hubs/workorder` |

> .NET 是「唯一」對前端 push 的入口；Python workers 只負責落地（時序 / 業務寫入），不直接面對前端，避免 CORS 雙頭管理。

### 4) 儲存層

- **SQL Server（Azure SQL Edge）**：業務資料（lot / wafer / panel / 物料 / 工單 / 異常）
- **InfluxDB**：時序資料（機台心跳 / 良率趨勢）

### 5) Core Backend（C# / ASP.NET Core 8）

- REST API：`/api/lots`、`/api/alarms`、`/api/workorders`、`/api/trace/panel/{panelNo}`、`/api/meta/profile` …
- SignalR Hub：`/hubs/spc`、`/hubs/alarm`、`/hubs/workorder`、`/hubs/trace`
- BackgroundService：
  - `KafkaConsumerHostedService`（含 `SpcRealtimeWorker`）
  - `RabbitMqConsumerHostedService`（含 alarm / workorder workers）
- `DomainProfileService`：啟動載入 `DOMAIN_PROFILE` 對應的 JSON。

### 6) Python Microservices

- `services/ingestion`：模擬產線 + Kafka producer
- `services/kafka-consumers/influx-writer`：寫 InfluxDB
- `services/kafka-consumers/rabbitmq-publisher`：判斷 severity → 推 RabbitMQ
- `services/spc-service`（FastAPI，port 8001）：**批次 / 歷史 SPC 報表**（即時計算已搬到 .NET）
- `services/rabbitmq-consumers/db-sink`：legacy；W07 起由 .NET 接管 RabbitMQ 消費，docker compose 預設不啟動

### 7) Frontend（React + TypeScript）

- 4 大頁：SPC Dashboard / 工單管理 / 異常記錄 / 物料追溯查詢
- `@microsoft/signalr` 訂閱對應 Hub
- 所有中文文案、站別、規格 USL/LSL、KPI 門檻都從 `/api/meta/profile` 取得

### 8) Infra

Docker Compose 一鍵拉起：SQL Server / InfluxDB / Kafka / RabbitMQ / 後端 / 前端 / Python services。

---

## 二、功能模組

### 1) SPC 統計製程管制

- 即時 X̄-R 雙圖（從 Kafka 推來的點，UCL/CL/LCL 即時計算）
- 八大規則違規偵測（Western Electric Rules，紅 / 黃 / 綠分級）
- 製程能力：Ca / Cp / Cpk（A+ / A / B / C / D 等級）
- KPI 卡：良率 / Cpk / 今日違規 / 每小時產出

### 2) 工單管理

- RabbitMQ `workorder` queue → .NET `WorkorderRabbitWorker` → SQL Server `workorders`
- SignalR `/hubs/workorder` → 前端即時長新一行（高亮 1.5 秒）
- REST：`GET /api/workorders?take=100` 預載歷史

### 3) 異常記錄

- RabbitMQ `alert` queue → .NET `AlarmRabbitWorker` → SQL Server `alarms`
- SignalR `/hubs/alarm` 即時推
- 嚴重度 badge：critical / high / medium / low

### 4) 物料追溯查詢

- 入口：`panel_no`（QR Code 對應）
- API：`GET /api/trace/panel/{panelNo}` 一次回 4 段
  - 板資訊（panel_no / lot / 狀態 / 建立時間）
  - 6 站時間軸（SPI / SMT / REFLOW / AOI / ICT / FQC）
  - 使用物料批號（錫膏 / FR4 / 電容…）
  - 同 lot / 同物料的相關板（最多 50 張）
- 站別中文由 `domain profile` 提供

---

## 三、Kafka / RabbitMQ 分工

```
Kafka  = 設備層 fan-out（消費群獨立並行，可 replay）
RabbitMQ = 業務層分級路由 + ack（告警必處理、工單必建立）
.NET    = 前端唯一 SignalR push 入口
Python  = 落地（時序 / 業務寫入），不直接面對前端
```

---

## 四、資料表（SQL Server）

```
tools / lots / wafers（含 panel_no） / recipes / process_runs
alarms / defects / defect_images / defect_reviews
workorders
material_lots / panel_material_usage / panel_station_log    -- W08 新增
documents / document_chunks / copilot_queries               -- 標 deprecated，未來會移除
```

詳細欄位見 [ERD.md](ERD.md)。

---

## 五、目錄概覽

```
frontend/
backend/
  src/
    Api/                Controllers / Hubs / Realtime
    Application/        Domain / Hubs / Messaging / Spc / Workers
    Domain/Entities/    Tool / Lot / Wafer / MaterialLot / PanelStationLog ...
    Infrastructure/     Data / Messaging / Workers
services/
  ingestion/
  kafka-consumers/
    influx-writer/
    rabbitmq-publisher/
  spc-service/                      ← 批次 / 歷史 SPC 報表（FastAPI）
  rabbitmq-consumers/db-sink/       ← legacy；docker compose profile=legacy
shared/
  domain-profiles/
    pcb.json
    semiconductor.json
infra/
  docker/docker-compose.yml
  db/init/
docs/
```

詳細結構見 [structrure.md](structrure.md)。

---

## 六、技術分工

| 技術 | 角色 |
|---|---|
| ASP.NET Core 8 + SignalR | 平台主體、REST / WebSocket、即時推播 |
| Kafka（KRaft） | 設備層事件骨幹，多消費群 fan-out |
| RabbitMQ（AMQP） | 業務事件分級路由（alert / workorder） |
| Python（FastAPI） | 模擬器、Kafka 消費端、批次 SPC 報表 |
| InfluxDB | 時序：機台心跳、良率趨勢 |
| SQL Server（Azure SQL Edge） | 業務資料、SPC 計算來源、物料追溯 |
| React + TypeScript | 4 大頁，SignalR 即時推播 |
| Domain Profile JSON | 同 codebase 切換不同產業 demo |
| Docker Compose | 一鍵啟動 |
