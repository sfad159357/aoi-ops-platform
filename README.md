## AOI Ops Platform（MVP）

模擬高科技製造場景的生產資訊系統，
涵蓋設備數據收集（MQTT→Kafka）、製程監控、缺陷管理、業務事件路由（RabbitMQ）與工程知識。

用 **.NET（分層架構）+ Kafka + RabbitMQ + PostgreSQL + InfluxDB + Python AI** 模擬 OT/IT 融合的 MES/AOI 場景，展示「企業後端設計 + 事件驅動架構 + 資料建模 + 可容器化落地」能力。

---

### 系統架構（OT/IT 融合）

```
🏭 AOI Machine / PLC / SCADA
   │  MQTT pub（topic: aoi/inspection/#）
   ▼
📡 Mosquitto Broker（Edge MQTT Broker）
   │  MQTT Bridge → Kafka Producer
   ▼
⚡ Kafka Broker（事件串流骨幹，KRaft mode）
   │  topic: aoi.inspection.raw
   │  topic: aoi.defect.event
   ├─── Consumer Group A ──▶ 📉 InfluxDB（時序：心跳 / 良率趨勢）
   ├─── Consumer Group B ──▶ 🐇 RabbitMQ（業務路由）
   │                             ├─ Queue: alert   ──▶ PostgreSQL alarms
   │                             └─ Queue: workorder ▶ PostgreSQL workorders
   └─── Consumer Group C ──▶ 🗄️ PostgreSQL（business：lot/wafer/defect）
                  ▲
   ⚙️ ASP.NET Core API（REST / WebSocket）
   🔌 FastAPI（分析 / ML / Copilot）
                  ▼
   🖥️ React TypeScript（即時監控儀表板）
```

---

### 系統定位
本專案模擬真實 MES/AOI 架構：

- Python Data Simulator 透過 **MQTT** 模擬設備發送製造數據
- Mosquitto Broker 接收後，透過 **MQTT Bridge → Kafka Producer** 轉發到 Kafka
- Kafka 讓多個消費者（InfluxDB Writer、RabbitMQ Publisher、DB Writer）**各自獨立消費同一份資料流**
- **RabbitMQ** 做業務事件分級路由：`alert` queue 觸發告警記錄，`workorder` queue 觸發工單建立
- **ASP.NET Core API** 整合讀取 PostgreSQL + InfluxDB，提供 REST API
- **React 前端**即時呈現 dashboard、告警、缺陷清單

- **PostgreSQL 開發環境一鍵啟動**：`docker compose up` 直接起 DB，並支援 init script
- **EF Core 資料模型已對齊 ERD**：`AoiOpsDbContext` 使用 Fluent API 管理 table/column/index
- **可快速驗證 DB 連線**：`GET /api/health/db` 回傳 `canConnect` 與 `toolsTableExists`

---

### 技術棧

| 層級 | 技術 | 說明 |
|------|------|------|
| 前端 | React、TypeScript、Vite | 即時監控儀表板 |
| 後端 | C# / ASP.NET Core Web API | 主業務邏輯、REST API |
| 事件串流 | **Kafka（KRaft mode）** | OT→IT 事件骨幹（新增） |
| 業務路由 | **RabbitMQ（AMQP）** | alert / workorder queue（新增） |
| Edge Broker | MQTT（Mosquitto） | OT 設備邊緣代理 |
| 結構化資料庫 | PostgreSQL | 業務資料、工單、異常記錄 |
| 時序資料庫 | InfluxDB | 機台心跳、良率趨勢 |
| 視覺化 | Grafana | InfluxDB 時序儀表板（規劃） |
| Python 服務 | FastAPI、kafka-python | 消費者 Workers、AI Copilot |
| 容器化 | Docker Compose | 全服務一鍵啟動 |

---

### 功能模組

- **Defect Review**：匯入缺陷資料/影像、標記 True/False、Review history、相似案例查詢
- **Fab Monitoring**：tool/lot/wafer dashboard、yield/alarm/defect trend、異常查詢
- **Knowledge Copilot**：文件上傳、檢索問答、回覆附來源
- **事件驅動告警**：Kafka → RabbitMQ alert queue → 自動寫入告警記錄

---

### 資料流

```
Python Simulator
  → MQTT publish
  → Mosquitto Broker
  → MQTT Bridge → Kafka（aoi.inspection.raw / aoi.defect.event）
  → Consumer A → InfluxDB（時序）
  → Consumer B → RabbitMQ → alert / workorder → PostgreSQL
  → Consumer C → PostgreSQL（業務資料）
  → ASP.NET Core API
  → React Dashboard / Grafana 視覺化
```

---

### Repo 結構

- `frontend/`：React + TypeScript（Vite）
- `backend/`：ASP.NET Core Web API（Api / Application / Domain / Infrastructure）
- `services/`：Python（data-simulator / kafka-consumers / vision-helper / ai-copilot）
  - `kafka-consumers/influx-writer/`：Consumer Group A
  - `kafka-consumers/rabbitmq-publisher/`：Consumer Group B
  - `kafka-consumers/db-writer/`：Consumer Group C
- `infra/`：Docker Compose、Mosquitto、Kafka、RabbitMQ、InfluxDB、DB migrations
- `docs/`：架構、ERD、API spec、MQTT 資料流、Kafka 事件格式

---

### Quick Start

```bash
docker compose -f infra/docker/docker-compose.yml up
```

- **DB 健康檢查**：啟動後打 `GET /api/health/db`
- **前端**：`http://localhost:5173`
- **後端 API**：`http://localhost:8080`
- **Kafka**：`localhost:9092`（開發用）
- **RabbitMQ 管理介面**：`http://localhost:15672`（預設帳密 guest/guest）
- **InfluxDB UI**：`http://localhost:8086`
