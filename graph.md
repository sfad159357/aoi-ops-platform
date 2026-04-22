# AOI Ops Platform — 架構與資料流視覺化（Mermaid）

> **為什麼要有這份檔案**：把 `project.md`、`structrure.md`、`ERD.md` 裡的文字規格，濃縮成可一眼掃過的圖；之後規格變更時，只要改這裡對應的區塊即可持續迭代。  
> **如何更新**：新增模組時補「系統脈絡圖」的節點；API 或批次流程變更時改「資料流」sequence；資料表增刪時同步「ERD」區塊。圖與文字來源以根目錄 `project.md` / `structrure.md` / `ERD.md` 為準。

---

## 1. 系統脈絡（誰跟誰說話）

高階元件與責任分界；對齊 `project.md`「整體架構」。

```mermaid
flowchart TB
  subgraph Users["使用者"]
    U[Engineer / Operator]
  end

  subgraph FE["Frontend（React + TypeScript）"]
    UI["Dashboard / Defect Review / 查詢 / 文件問答"]
  end

  subgraph Core["Core Backend（C# / ASP.NET Core Web API）"]
    API["REST API：帳號、查詢、Review / Alarm workflow"]
  end

  subgraph DB["Database"]
    PG[(PostgreSQL lot · wafer · tool · recipe · alarm · defect · review · documents)]
  end

  subgraph PY["Python Services"]
    SIM["Data Simulator 模擬 tool/lot/wafer/AOI/yield/alarm"]
    VIS["Vision Helper OpenCV 前處理 · 相似圖"]
    COP["AI Copilot 切塊 · 檢索 · RAG · 摘要"]
  end

  subgraph Infra["Infra"]
    DC["Docker Compose"]
  end

  U --> UI
  UI -->|HTTPS JSON| API
  API -->|讀寫| PG
  SIM -->|寫入製造/AOI 假資料| PG
  API <-->|必要時呼叫| VIS
  UI -->|上傳文件 / 問答| COP
  API <-->|索引與 RAG 整合（規劃）| COP
  DC -.-> FE
  DC -.-> Core
  DC -.-> DB
  DC -.-> PY
```

**備註**：此圖以 PostgreSQL 為結構化資料庫；資料流方向與角色分工不因儲存引擎而改變。

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
  end

  B --> Backend
  Api --> App
  App --> Dom
  Inf --> Dom
  Api --> Inf
```

**依賴方向（初學者記這句就好）**：`Api` 組裝一切；`Application` 寫用例流程；`Domain` 放業務模型與規則；`Infrastructure` 實作 DB、外部服務。**內層（Domain）不依賴外層**。

---

## 3. 主要資料流：模擬資料 → DB → 前端

對齊 `project.md`「資料流」前半段。

```mermaid
sequenceDiagram
  autonumber
  participant SIM as Data Simulator（Python）
  participant API as ASP.NET Core API
  participant DB as PostgreSQL
  participant UI as React 前端

  Note over SIM: 定時或批次產生<br/>正常/異常/漂移/誤判情境
  SIM->>DB: 寫入 lot · wafer · process_run · defect · alarm 等
  alt 若改由 API 接收再寫入
    SIM->>API: POST 批次/事件
    API->>DB: 驗證後寫入
  end
  UI->>API: GET 查詢（dashboard / review / 趨勢）
  API->>DB: 讀取彙總與明細
  DB-->>API: 結果集
  API-->>UI: JSON 回應
```

---

## 4. 資料流：文件上傳與 Copilot 問答

對齊 `project.md`「資料流」後半段與 Knowledge Copilot 模組。

```mermaid
sequenceDiagram
  autonumber
  participant UI as React 前端
  participant API as ASP.NET Core API（可選：metadata）
  participant COP as AI Copilot（Python）
  participant DB as PostgreSQL
  participant FS as 物件儲存/檔案路徑（規劃）

  UI->>COP: 上傳 SOP / recipe / 異常手冊
  COP->>COP: 切塊 · embedding · 索引
  COP->>DB: 寫入 documents · document_chunks（與 ERD 一致）
  UI->>COP: 問答（可帶 defect_id / alarm_id context）
  COP->>DB: 檢索 chunks · 關聯 copilot_queries 紀錄
  COP-->>UI: 回答 + 來源引用（source_refs）
  opt 平台統一帳號與稽核
    UI->>API: 登入 / 權限檢查
    API-->>UI: Token / 角色
  end
```

---

## 5. 功能模組與資料領域（鳥瞰）

對齊 `project.md`「功能模組」。

```mermaid
mindmap
  root((AOI Ops Platform))
    Defect Review
      影像與 metadata
      True / False 標記
      分類與 review history
      相似案例查詢
    Fab Monitoring
      Tool / Lot / Wafer / Recipe
      Yield · Defect · Alarm 趨勢
      異常查詢與報表
    Knowledge Copilot
      文件上傳與索引
      搜尋與問答附來源
      Defect/Alarm 情境建議
    Data Simulation
      假資料流
      定時寫入 DB
```

---

## 6. 實體關係（ERD，對齊 `ERD.md`）

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

  COPILOT_QUERIES }o--o| ALARMS : "related_alarm_id"
  COPILOT_QUERIES }o--o| DEFECTS : "related_defect_id"
```

> `copilot_queries` 與 `alarms` / `defects` 在 `ERD.md` 以欄位描述關聯；實作時請決定是否為可為 NULL 的外鍵，並與此圖同步更新。

---

## 7. 變更紀錄（建議每次改圖順手打一列）

| 日期 | 變更摘要 |
|------|----------|
| 2026-04-12 | 初版：依 `project.md`、`structrure.md`、`ERD.md` 建立脈絡、分層、資料流、ERD |
