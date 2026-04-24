# AOI Ops Platform — 架構與資料流視覺化（Mermaid）

> **為什麼要有這份檔案**：把 `project.md`、`structrure.md`、`ERD.md` 裡的文字規格，濃縮成可一眼掃過的圖；之後規格變更時，只要改這裡對應的區塊即可持續迭代。  
> **如何更新**：新增模組時補「系統脈絡圖」的節點；API 或批次流程變更時改「資料流」sequence；資料表增刪時同步「ERD」區塊。圖與文字來源以根目錄 `project.md` / `structrure.md` / `ERD.md` 為準。

---

## 1. 系統脈絡（誰跟誰說話）

> 2026-04-24 更新：加入 Kafka 事件串流層、RabbitMQ 業務路由層、InfluxDB 時序儲存，對齊 HTML 架構設計圖。  
> **為什麼這樣設計**：OT 設備（AOI / PLC）只懂 MQTT，IT 系統需要可重播、可多消費者的事件流；Kafka 做兩者橋接，RabbitMQ 再做業務事件分級路由，讓「告警通知」與「工單觸發」職責分離、互不干擾。

```mermaid
flowchart TB
  subgraph OT["OT 設備層"]
    MA["🏭 AOI Machine A\nMQTT Client"]
    MB["🏭 AOI Machine B\nMQTT Client"]
    PLC["🔧 PLC / SCADA\nOPC-UA / MQTT"]
  end

  subgraph Edge["Edge Broker"]
    MOSQ["📡 Mosquitto Broker\nMQTT pub/sub"]
  end

  subgraph Stream["事件串流層（新增）"]
    KAFKA["⚡ Kafka Broker\nKRaft mode · single node"]
    T1["topic: aoi.inspection.raw"]
    T2["topic: aoi.defect.event"]
    KAFKA --> T1
    KAFKA --> T2
  end

  subgraph Workers["IT 應用層（新增）"]
    CGA["Consumer Group A\nInfluxDB Writer（Python）"]
    CGB["Consumer Group B\nRabbitMQ Publisher（Python）"]
    CGC["Consumer Group C\nDB Writer（Python）"]
    RMQ["🐇 RabbitMQ\nAMQP · Exchange 路由"]
    YIELD["📈 良率統計 Worker\nPython / FastAPI"]
  end

  subgraph Storage["儲存層"]
    INFLUX[("📉 InfluxDB\n時序：機台心跳 · 良率趨勢")]
    PG[("🗄️ PostgreSQL\n業務：工單 · 異常記錄")]
  end

  subgraph API["API 層"]
    DOTNET["⚙️ ASP.NET Core API\nREST · 業務邏輯"]
    FASTAPI["🔌 FastAPI\n分析 / ML / Copilot"]
  end

  subgraph FE["前端層"]
    UI["🖥️ React TypeScript\n即時監控儀表板"]
    U["👤 Engineer / Operator"]
  end

  MA -->|MQTT pub| MOSQ
  MB -->|MQTT pub| MOSQ
  PLC -->|MQTT pub| MOSQ
  MOSQ -->|MQTT Bridge → Kafka Producer| KAFKA
  T1 --> CGA
  T1 --> CGB
  T1 --> CGC
  T2 --> CGB
  CGA -->|寫時序| INFLUX
  CGB -->|publish event| RMQ
  CGC -->|寫業務資料| PG
  RMQ -->|Queue: alert| PG
  RMQ -->|Queue: workorder| PG
  YIELD --> PG
  INFLUX -->|REST / Flux| DOTNET
  PG -->|讀寫| DOTNET
  PG -->|讀| FASTAPI
  DOTNET -->|REST API / WebSocket| UI
  FASTAPI -->|REST API| UI
  U --> UI
```

---

## 2. Repo 目錄與後端分層（對齊 `structrure.md`）

```mermaid
flowchart LR
  subgraph Repo["aoi-ops-platform/"]
    F[frontend/]
    B[backend/]
    S[services/]
    I[infra/]
    D[docs/]
    X[scripts/]
  end

  subgraph Backend["backend/src/"]
    Api[Api]
    App[Application]
    Dom[Domain]
    Inf[Infrastructure]
    Mqtt[Infrastructure/Mqtt]
    TS[Infrastructure/TimeSeries]
  end

  subgraph Services["services/"]
    SIM[data-simulator/\nmqtt_publisher.py]
    CGA_S[kafka-consumer/\ninflux-writer/]
    CGB_S[kafka-consumer/\nrabbitmq-publisher/]
    CGC_S[kafka-consumer/\ndb-writer/]
    VIS[vision-helper/]
    COP[ai-copilot/]
  end

  B --> Backend
  Api --> App
  App --> Dom
  Inf --> Dom
  Api --> Inf
  Mqtt --> Inf
  TS --> Inf
  S --> Services
```

**依賴方向（初學者記這句就好）**：`Api` 組裝一切；`Application` 寫用例流程；`Domain` 放業務模型與規則；`Infrastructure` 實作 DB、外部服務。**內層（Domain）不依賴外層**。

---

## 3. 主要資料流：OT 設備 → Kafka → 各消費者（新版）

> **為什麼改成這張圖**：原本「Simulator 直接寫 DB」的流程太簡單，無法反映真實工廠的 OT→IT 整合場景。加入 Kafka 後，你可以在履歷上說「設計過 MQTT Bridge → Kafka → 多消費者 fan-out 架構」，這是相當有說服力的設計經驗。

```mermaid
sequenceDiagram
  autonumber
  participant SIM as Data Simulator（Python）
  participant MOSQ as Mosquitto Broker
  participant KAFKA as Kafka Broker
  participant CGA as Consumer A（InfluxDB Writer）
  participant CGB as Consumer B（RabbitMQ Publisher）
  participant CGC as Consumer C（DB Writer）
  participant RMQ as RabbitMQ
  participant INFLUX as InfluxDB
  participant PG as PostgreSQL
  participant API as ASP.NET Core API
  participant UI as React 前端

  Note over SIM: 模擬 AOI Machine：<br/>正常 / 異常 / 漂移 / 誤判情境
  SIM->>MOSQ: MQTT publish（topic: aoi/inspection/#）
  MOSQ->>KAFKA: MQTT Bridge 轉發 → Kafka Producer
  Note over KAFKA: topic: aoi.inspection.raw<br/>topic: aoi.defect.event

  par Consumer Fan-out
    KAFKA->>CGA: Consumer Group A
    CGA->>INFLUX: 寫 tool_metrics / yield_trend（時序）
  and
    KAFKA->>CGB: Consumer Group B
    CGB->>RMQ: publish 異常事件
    RMQ->>PG: Queue alert → 寫 alarms
    RMQ->>PG: Queue workorder → 寫 workorders
  and
    KAFKA->>CGC: Consumer Group C
    CGC->>PG: 寫 process_runs / defects（業務）
  end

  UI->>API: GET 查詢（dashboard / review / 趨勢）
  API->>PG: 讀取彙總與明細
  API->>INFLUX: 讀取趨勢（Flux query）
  PG-->>API: 結果集
  INFLUX-->>API: 時序資料
  API-->>UI: JSON 回應
```

---

## 4. 資料流：文件上傳與 Copilot 問答

```mermaid
sequenceDiagram
  autonumber
  participant UI as React 前端
  participant API as ASP.NET Core API（可選：metadata）
  participant COP as AI Copilot（Python / FastAPI）
  participant PG as PostgreSQL
  participant FS as 物件儲存/檔案路徑（規劃）

  UI->>COP: 上傳 SOP / recipe / 異常手冊
  COP->>COP: 切塊 · embedding · 索引
  COP->>PG: 寫入 documents · document_chunks（與 ERD 一致）
  UI->>COP: 問答（可帶 defect_id / alarm_id context）
  COP->>PG: 檢索 chunks · 關聯 copilot_queries 紀錄
  COP-->>UI: 回答 + 來源引用（source_refs）
  opt 平台統一帳號與稽核
    UI->>API: 登入 / 權限檢查
    API-->>UI: Token / 角色
  end
```

---

## 5. 功能模組與資料領域（鳥瞰）

```mermaid
mindmap
  root((AOI Ops Platform))
    OT 接收層
      MQTT Mosquitto
      Kafka 事件串流
      MQTT Bridge Producer
    Defect Review
      影像與 metadata
      True / False 標記
      分類與 review history
      相似案例查詢
    Fab Monitoring
      Tool / Lot / Wafer / Recipe
      Yield · Defect · Alarm 趨勢
      InfluxDB 時序儀表板
      異常查詢與報表
    業務路由層
      RabbitMQ Exchange
      Queue alert（告警通知）
      Queue workorder（工單觸發）
    Knowledge Copilot
      文件上傳與索引
      搜尋與問答附來源
      Defect/Alarm 情境建議
    Data Simulation
      MQTT 假資料發送
      正常/異常/漂移/誤判
      定時模擬機台心跳
```

---

## 6. 實體關係（ERD，PostgreSQL 部分，對齊 `ERD.md`）

表名採 Mermaid `erDiagram` 慣例（大寫節點名僅為可讀性；實際 DB 命名以 migration 為準）。

```mermaid
erDiagram
  TOOLS ||--o{ PROCESS_RUNS : "tool_id"
  RECIPES ||--o{ PROCESS_RUNS : "recipe_id"
  LOTS ||--o{ WAFERS : "lot_id"
  LOTS ||--o{ PROCESS_RUNS : "lot_id"
  WAFERS ||--o{ PROCESS_RUNS : "wafer_id"
  PROCESS_RUNS ||--o{ ALARMS : "process_run_id"
  PROCESS_RUNS ||--o{ DEFECTS : "process_run_id"
  TOOLS ||--o{ DEFECTS : "tool_id"
  LOTS ||--o{ DEFECTS : "lot_id"
  WAFERS ||--o{ DEFECTS : "wafer_id"
  DEFECTS ||--o{ DEFECT_IMAGES : "defect_id"
  DEFECTS ||--o{ DEFECT_REVIEWS : "defect_id"
  DOCUMENTS ||--o{ DOCUMENT_CHUNKS : "document_id"
  LOTS ||--o{ WORKORDERS : "lot_id"

  COPILOT_QUERIES }o--o| ALARMS : "related_alarm_id"
  COPILOT_QUERIES }o--o| DEFECTS : "related_defect_id"
```

> **新增**：`WORKORDERS` 表承接 RabbitMQ `workorder` queue 的業務事件。`ALARMS.source` 欄位新增 `kafka` 來源識別，`DEFECTS.kafka_event_id` 對應 Kafka offset 追溯。

---

## 7. 變更紀錄

> 詳見根目錄 `log.md`（此 repo 習慣把 changelog 集中在一個檔案，避免每份文件都各寫一份而漂移）。


