# Architecture

> 為什麼要有這份檔案：把 `project.md` / `graph.md` 的高層脈絡濃縮成一頁，新加入的成員從這裡 30 秒理解全貌。

## 目標

AOI Ops Platform 模擬 PCB SMT 產線（SPI / SMT / REFLOW / AOI / ICT / FQC）的 MES 品質模組，
用 **Kafka 即時推播 + .NET Core SignalR** 為骨幹，提供 SPC 即時監控 + 工單 / 異常 / 物料追溯 三組業務模組。

不做：MQTT / OPC-UA / Mosquitto / Knowledge Copilot / 機器學習。

## 模組分層

| 層級 | 內容 |
|---|---|
| Frontend (`frontend/`) | React + TypeScript + SignalR client，4 大頁（SPC / 工單 / 異常 / 物料追溯）|
| Core Backend (`backend/`) | ASP.NET Core 8（Api / Application / Domain / Infrastructure），REST API + 4 個 SignalR Hub + Kafka / RabbitMQ consumer |
| 事件串流 | Kafka（KRaft）— 設備層 fan-out |
| 業務路由 | RabbitMQ — alert / workorder 業務佇列 |
| 結構化資料 | PostgreSQL 16 — 業務資料、SPC 計算來源、物料追溯 |
| 時序資料 | InfluxDB 2.7 — 機台心跳 / 良率趨勢 |
| Python 微服務 | `ingestion`（模擬器 + Kafka producer）/ `kafka-consumers/{influx-writer, rabbitmq-publisher}` / `spc-service`（批次 SPC 報表） |

## 即時資料流

1. `ingestion` Python 模擬器 publish Kafka `aoi.inspection.raw` / `aoi.defect.event`
2. 多個 consumer group 並行：
   - `kafka-influx-writer` → InfluxDB
   - `kafka-rabbitmq-publisher` → RabbitMQ alert / workorder
   - **.NET `SpcRealtimeWorker`** → SignalR `/hubs/spc`（spcPoint / spcViolation）
3. RabbitMQ 業務佇列：
   - `alert` → **.NET `AlarmRabbitWorker`** → 寫 PG `alarms` + SignalR `/hubs/alarm`
   - `workorder` → **.NET `WorkorderRabbitWorker`** → 寫 PG `workorders` + SignalR `/hubs/workorder`
4. 前端透過 `@microsoft/signalr` 訂閱 4 個 Hub；歷史資料透過 REST API 預載

詳見 [graph.md](../graph.md) 第 3、4 節 sequence。

## 相關文件

- 即時推播訊息格式：[realtime-signalr.md](realtime-signalr.md)
- Domain Profile 機制：[domain-profile.md](domain-profile.md)
- 物料追溯資料模型：[traceability.md](traceability.md)
- ERD：[../ERD.md](../ERD.md)
- 開發除錯筆記：[debug-notes.md](debug-notes.md)
