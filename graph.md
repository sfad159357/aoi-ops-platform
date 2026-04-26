# AOI Ops Platform — 架構與資料流視覺化（Mermaid）

> **為什麼要有這份檔案**：把 `project.md`、`structrure.md`、`ERD.md` 裡的文字規格，濃縮成可一眼掃過的圖；
> 之後規格變更時，只要改這裡對應的區塊即可持續迭代。
>
> **2026-04 v2 重新對齊**：取消 MQTT / OPC-UA / Knowledge Copilot；
> SPC 改為 Kafka → .NET SignalR 即時推播；新增 4 個 SignalR Hub；
> 新增 Domain Profile 機制（pcb / semiconductor 同 codebase 切換）。

---

## 1. 系統脈絡（誰跟誰說話）

```mermaid
flowchart TB
  subgraph OT["OT 設備層（模擬）"]
    SIM["🏭 ingestion（Python）<br/>SPI / SMT / REFLOW / AOI / ICT / FQC<br/>SIM_SCENARIO=normal/drift/spike/misjudge"]
  end

  subgraph Stream["事件串流層"]
    KAFKA["⚡ Kafka（KRaft）"]
    T1["topic: aoi.inspection.raw"]
    T2["topic: aoi.defect.event"]
    KAFKA --> T1
    KAFKA --> T2
  end

  subgraph PyWorkers["Python Workers（落地）"]
    INF_W["kafka-influx-writer"]
    RMQ_PUB["kafka-rabbitmq-publisher"]
  end

  subgraph DotnetWorkers[".NET Workers（推前端）"]
    SPC_W["SpcRealtimeWorker<br/>(Kafka consumer)"]
    AL_W["AlarmRabbitWorker<br/>(RabbitMQ consumer)"]
    WO_W["WorkorderRabbitWorker<br/>(RabbitMQ consumer)"]
  end

  subgraph Bus["業務路由層"]
    RMQ["🐇 RabbitMQ"]
    QA["queue: alert"]
    QW["queue: workorder"]
    RMQ --> QA
    RMQ --> QW
  end

  subgraph Storage["儲存層"]
    INFLUX[("📉 InfluxDB<br/>tool_metrics / yield_trend")]
    PG[("🗄️ PostgreSQL<br/>lots / wafers / alarms / workorders<br/>material_lots / panel_station_log ...")]
  end

  subgraph DOTNET["⚙️ ASP.NET Core 8（唯一前端入口）"]
    REST["REST API<br/>/api/lots /alarms /workorders<br/>/api/trace/* /api/meta/profile"]
    HUB_SPC["SignalR /hubs/spc"]
    HUB_AL["SignalR /hubs/alarm"]
    HUB_WO["SignalR /hubs/workorder"]
  end

  subgraph SPCSVC["📊 Python spc-service（FastAPI 8001）"]
    SPC_BATCH["批次 / 歷史 SPC 報表<br/>Cpk · Pareto"]
  end

  FE["🖥️ React + SignalR<br/>SPC / 工單 / 異常 / 物料追溯"]

  SIM -->|Kafka publish| KAFKA
  T1 --> INF_W
  T1 --> RMQ_PUB
  T2 --> RMQ_PUB
  T1 --> SPC_W
  RMQ_PUB --> RMQ
  QA --> AL_W
  QW --> WO_W

  INF_W --> INFLUX
  AL_W --> PG
  WO_W --> PG

  PG --> REST
  INFLUX --> REST

  SPC_W --> HUB_SPC
  AL_W --> HUB_AL
  WO_W --> HUB_WO

  REST --> FE
  HUB_SPC -->|WebSocket| FE
  HUB_AL  -->|WebSocket| FE
  HUB_WO  -->|WebSocket| FE
  FE -->|REST 批次| SPC_BATCH
```

> 規則：
> - Kafka = 設備層 fan-out（多 consumer group 並行、可 replay）
> - RabbitMQ = 業務層分級路由 + ack（告警必處理 / 工單必建立）
> - **.NET 是前端唯一 SignalR push 入口**，Python 只負責落地（時序 / 業務寫入）

---

## 2. Repo 目錄與後端分層

```mermaid
flowchart LR
  subgraph Repo["aoi-ops-platform/"]
    F[frontend/]
    B[backend/]
    S[services/]
    SH[shared/]
    I[infra/]
    D[docs/]
  end

  subgraph Backend["backend/src/"]
    Api[Api<br/>Controllers / Hubs / Realtime]
    App[Application<br/>Domain / Spc / Hubs / Workers / Messaging]
    Dom[Domain/Entities]
    Inf[Infrastructure<br/>Data / Messaging / Workers]
  end

  subgraph Services["services/"]
    SIM[ingestion/]
    INF_S[kafka-consumers/influx-writer/]
    PUB_S[kafka-consumers/rabbitmq-publisher/]
    SPC_S[spc-service/  ← 批次 / 歷史]
    LEG[rabbitmq-consumers/db-sink/  ← legacy profile]
  end

  subgraph Shared["shared/"]
    DP[domain-profiles/<br/>pcb.json / semiconductor.json]
  end

  B --> Backend
  Api --> App
  App --> Dom
  Inf --> Dom
  Api --> Inf
  S --> Services
  SH --> Shared
```

依賴方向：`Api` 組裝一切；`Application` 寫用例與業務流程（不依賴 `Infrastructure`）；
`Domain` 放純資料模型；`Infrastructure` 實作 DB / Kafka / RabbitMQ。

---

## 3. SPC 即時推播 sequence

```mermaid
sequenceDiagram
  autonumber
  participant ING as ingestion (Python)
  participant K as Kafka aoi.inspection.raw
  participant SW as .NET SpcRealtimeWorker
  participant H as SpcHub (SignalR)
  participant FE as React SPC Dashboard

  ING->>K: publish(line, machine, parameter, value)
  K->>SW: consume(group=aoiops-spc-realtime)
  SW->>SW: 套八大規則 + 算 Cpk(滑動視窗 N=25)
  alt 無違規
    SW->>H: Clients.Group("line:SMT-A|param:solder_thickness").SendAsync("spcPoint", payload)
  else 有違規
    SW->>H: SendAsync("spcPoint", payload)
    SW->>H: SendAsync("spcViolation", payload)
  end
  H-->>FE: WebSocket 推送
  FE->>FE: append 一點，違規高亮 + 違規表新增一行
```

---

## 4. 業務事件 sequence（異常 / 工單）

```mermaid
sequenceDiagram
  autonumber
  participant K as Kafka aoi.defect.event
  participant PUB as Python rabbitmq-publisher
  participant Q as RabbitMQ
  participant W as .NET Worker（AlarmRabbitWorker / WorkorderRabbitWorker）
  participant PG as PostgreSQL
  participant H as SignalR Hub
  participant FE as React 前端

  K->>PUB: defect event（severity=high/critical）
  PUB->>Q: publish queue=alert / workorder
  Q->>W: deliver（manual ack）
  W->>PG: INSERT alarms / workorders
  W->>H: PushAsync(payload)
  H-->>FE: /hubs/alarm 或 /hubs/workorder
  FE->>FE: 列表頂端新增一行（高亮 1.5s）
  W-->>Q: ack
```

---

## 5. 物料追溯查詢

```mermaid
sequenceDiagram
  autonumber
  participant FE as React Traceability
  participant API as TraceController
  participant PG as PostgreSQL

  FE->>API: GET /api/trace/panels/recent?take=20
  API->>PG: SELECT recent wafers (with panel_no)
  PG-->>API: list
  API-->>FE: RelatedPanelDto[]
  FE->>API: GET /api/trace/panel/{panelNo}
  API->>PG: 1) wafers + lots
  API->>PG: 2) panel_station_log（6 站時間軸）
  API->>PG: 3) panel_material_usage join material_lots
  API->>PG: 4) 同 lot / 同物料的 related panels
  PG-->>API: 4 段資料
  API-->>FE: PanelTraceDto
```

---

## 6. ERD（PostgreSQL 部分，對齊 `ERD.md`）

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
  LOTS ||--o{ WORKORDERS : "lot_id"

  WAFERS ||--o{ PANEL_STATION_LOG : "panel_id"
  WAFERS ||--o{ PANEL_MATERIAL_USAGE : "panel_id"
  MATERIAL_LOTS ||--o{ PANEL_MATERIAL_USAGE : "material_lot_id"
```

> W08 新增三張表：`MATERIAL_LOTS` / `PANEL_MATERIAL_USAGE` / `PANEL_STATION_LOG`，
> `WAFERS` 加 `panel_no varchar UNIQUE`（對外可讀識別，例：`PCB-20240422-LOT-001-1`）。
>
> `DOCUMENTS` / `DOCUMENT_CHUNKS` / `COPILOT_QUERIES` 為舊版殘留，標 deprecated，未來會移除。

---

## 7. 變更紀錄

> 詳見根目錄 `log.md`。
