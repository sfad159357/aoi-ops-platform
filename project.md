# AOI Ops Platform

## 定位
模擬高科技製造場景（PCB / 半導體）的 AOI 缺陷管理、
生產資訊監控，並以 **SPC（統計製程管制）** 做製程穩定性與異常趨勢偵測。

主體用 C# / ASP.NET Core，輔助模組用 Python。
模擬真實 MES 場景：設備數據收集（MQTT → Kafka）、製程監控、異常告警、
缺陷追蹤、業務事件路由（RabbitMQ）、以及 SPC 圖表與規則告警（八大規則 + Ca/Cp/Cpk）。

---

### 整體架構

#### OT 設備層（現場端）
AOI Machine / PLC / SCADA
- 透過 MQTT 協議發送檢測數據（topic: `aoi/inspection/#`）
- 模擬端用 Python Data Simulator 取代真實設備

#### Edge Broker
Mosquitto Broker（MQTT pub/sub，輕量邊緣代理）
- 接收 OT 設備 MQTT 訊息
- 為什麼保留 Mosquitto：OT 設備標準協議是 MQTT，Mosquitto 是最常見的輕量 Broker，方便未來接真機

#### 事件串流層（Kafka，新增）
Kafka Broker（KRaft mode，single node，Docker 容器化）
- MQTT Bridge 將 Mosquitto 的訊息轉發給 Kafka Producer
- Topics：
  - `aoi.inspection.raw`：原始檢測結果（溫度、壓力、良率、缺陷）
  - `aoi.defect.event`：高嚴重度缺陷事件（立即觸發告警）
- 為什麼要 Kafka：Mosquitto 只是 1 對 1 或廣播，Kafka 讓多個「消費者」可以**各自獨立消費同一份資料流**，不互相影響（例如：InfluxDB Writer、RabbitMQ Publisher、DB Writer 三個消費者同時處理同一則訊息）

#### IT 應用層（Workers + RabbitMQ，新增）
- **Consumer Group A — InfluxDB Writer（Python）**
  - 從 `aoi.inspection.raw` 消費 → 寫 InfluxDB（時序資料）
- **Consumer Group B — RabbitMQ Publisher（Python）**
  - 判斷是否為異常事件 → publish 至 RabbitMQ exchange
  - RabbitMQ Queue: `alert` → 觸發告警寫入 PostgreSQL
  - RabbitMQ Queue: `workorder` → 觸發工單建立
- **Consumer Group C — DB Writer（Python）**
  - 從 `aoi.inspection.raw` 消費 → 寫 PostgreSQL（process_runs、defects）
- **良率統計 Worker（Python / FastAPI）**
  - 定期聚合 InfluxDB 良率資料，回寫 PostgreSQL 供 dashboard 查詢

#### 儲存層
- PostgreSQL（結構化業務資料：lot、wafer、tool、recipe、alarm、defect、review、workorder、documents）
- InfluxDB（時序資料：設備即時數值、良率趨勢）
- 為什麼兩個 DB：PostgreSQL 適合「跨表查詢、業務流程」；InfluxDB 適合「高頻寫入、時間範圍查詢」

#### Core Backend（C# / ASP.NET Core Web API）
- 主業務邏輯、帳號、查詢、review workflow、alarm workflow
- 整合讀取 PostgreSQL 與 InfluxDB
- 提供 REST API 給前端
- 這是主要拿來對標企業 C#/.NET 技術棧的核心

#### Python Microservices（FastAPI）
- Data Simulator：模擬 AOI 設備，透過 MQTT 發送假資料
- Kafka Consumer Workers：InfluxDB Writer / RabbitMQ Publisher / DB Writer
- **SPC Service（新增）**：統計製程管制計算服務
  - 計量型圖表：Xbar-R、I-MR、Xbar-S
  - 計數型圖表：P、Np、C、U
  - 八大規則偵測（Western Electric Rules）
  - 製程能力指數：Ca、Cp、Cpk
  - REST API + Demo 端點（前端展示用）

#### Frontend（React + TypeScript）
- Dashboard、Defect Review、查詢頁、**SPC 管制圖頁（重點）**
- 即時監控（未來可接 WebSocket push）
- 主角不是前端，但展示你能做出可用的操作介面

#### Infra
Docker Compose 一鍵拉起：
前端 / 後端 / PostgreSQL / InfluxDB / Mosquitto / Kafka / RabbitMQ / Python services

---

### 功能模組

#### Defect Review Module
- 匯入 defect image 與 metadata
- defect list / detail
- true defect / false alarm 標記
- defect 分類
- 相似案例查詢
- review history

#### Fab Monitoring Module
- tool / lot / wafer / recipe dashboard
- yield trend（InfluxDB 時序）
- defect trend
- alarm list（PostgreSQL + RabbitMQ alert queue 觸發）
- 異常查詢
- summary report

#### SPC 統計製程管制模組（新增）
- 計量型管制圖：Xbar-R、I-MR、Xbar-S、I-MR
- 計數型管制圖：P、Np、C、U
- 八大規則偵測（嚴重程度分級：red/yellow/green）
- 製程能力指數：Ca（準確度）、Cp（精密度）、Cpk（綜合）
- 製程等級判定：A+/A/B/C/D
- React Dashboard：管制圖視覺化、違規高亮、製程能力卡片

#### Knowledge Copilot Module
> 目前不是專案主線（降級為選配）。  
> 現階段前端重點是「監控設備回傳數據 → 製作 SPC 圖表」。
>
> 後續若要補強，可再加入：文件上傳、檢索問答、回覆附來源等功能。

#### Data Simulation Module
- Python simulator 透過 MQTT 發送假資料（模擬 AOI Machine）
- 正常 / 異常 / 漂移 / 誤判情境
- 定時模擬機台心跳，寫入 Kafka → 各消費者

---

### 資料表（PostgreSQL）
tools / lots / wafers / recipes / process_runs /
alarms / defects / defect_images / defect_reviews /
workorders（新增，來自 RabbitMQ workorder queue）/
documents / document_chunks / copilot_queries（選配，非主線）

---

### 資料流（更新版）
```
Python Data Simulator
  → MQTT publish（topic: aoi/inspection/#）
  → Mosquitto Broker
  → MQTT Bridge → Kafka Producer
  → Kafka（topic: aoi.inspection.raw / aoi.defect.event）
  → Consumer Group A → InfluxDB（時序）
  → Consumer Group B → RabbitMQ → alert queue → PostgreSQL alarms
                               → workorder queue → PostgreSQL workorders
  → Consumer Group C → PostgreSQL process_runs / defects

ASP.NET Core API 讀取 PostgreSQL + InfluxDB
→ React 前端顯示 dashboard / review / 趨勢

SPC Service（Python FastAPI）讀取 PostgreSQL（process_runs / tool_measurements）
→ 計算管制圖（Xbar-R / I-MR / P / C ...）、八大規則、Ca/Cp/Cpk
→ React 前端顯示 SPC Dashboard（違規高亮 + 處理優先級）
```

---

### 目錄切分建議
```
frontend/
backend/
services/
  data-simulator/       ← MQTT publisher
  kafka-consumers/
    influx-writer/      ← Consumer Group A
    rabbitmq-publisher/ ← Consumer Group B
    db-writer/          ← Consumer Group C
  spc-service/          ← SPC 統計製程管制（新增，port 8001）
    app/
      main.py           ← FastAPI 進入點
      models.py         ← Pydantic 模型
      spc_engine.py     ← Xbar-R/I-MR/P/Np/C/U 計算引擎
      rules.py          ← 八大規則偵測邏輯
      demo_data.py      ← Demo 資料產生器
infra/
docs/
```

---

### 技術分工
| 技術 | 角色 |
|------|------|
| C# / ASP.NET Core | 平台主體、企業系統感、履歷主訊號 |
| Kafka（KRaft） | 事件串流骨幹、OT→IT 橋接 |
| RabbitMQ（AMQP） | 業務事件分級路由（告警 / 工單） |
| Mosquitto（MQTT） | OT 邊緣代理、貼近真實設備協議 |
| Python（FastAPI） | AI、影像、消費者 Worker、模擬器 |
| InfluxDB | 時序儲存、機台心跳、良率趨勢 |
| PostgreSQL | 業務關聯資料、主要查詢來源 |
| React TypeScript | 展示前端整合能力 |
| Docker Compose | 全服務容器化，一鍵啟動 |
