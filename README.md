## AOI Ops Platform（MVP）

模擬高科技製造場景的生產資訊系統，
涵蓋設備數據收集（Kafka / RabbitMQ）、製程監控、缺陷管理、業務事件路由（RabbitMQ），並以 **SPC（統計製程管制）** 製作製程監控圖表與異常趨勢偵測。

用 **.NET（分層架構）+ Kafka + RabbitMQ + PostgreSQL + InfluxDB + Python（SPC Service）** 模擬 OT/IT 融合的 MES/AOI 場景，展示「企業後端設計 + 事件驅動架構 + 資料建模 + 可容器化落地」能力。

---

### 系統架構（OT/IT 融合）

```
🏭 AOI Machine / PLC / SCADA
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
   🔌 FastAPI（SPC 計算服務）
                  ▼
   🖥️ React TypeScript（即時監控儀表板）
```

---

### 系統定位

本專案模擬真實 MES/AOI 架構：

- `ingestion`（單一容器）透過 **Kafka** 模擬設備發送製造數據，並同時消費後落地 PostgreSQL（MVP 先求端到端打通）
- Kafka 讓多個消費者（InfluxDB Writer、RabbitMQ Publisher、DB Writer）**各自獨立消費同一份資料流**
- **RabbitMQ** 做業務事件分級路由：`alert` queue 觸發告警記錄，`workorder` queue 觸發工單建立
- **ASP.NET Core API** 整合讀取 PostgreSQL + InfluxDB，提供 REST API
- **React 前端**即時呈現 dashboard、告警、缺陷清單
- **PostgreSQL 開發環境一鍵啟動**：`docker compose up` 直接起 DB，並支援 init script
- **EF Core 資料模型已對齊 ERD**：`AoiOpsDbContext` 使用 Fluent API 管理 table/column/index
- **可快速驗證 DB 連線**：`GET /api/health/db` 回傳 `canConnect` 與 `toolsTableExists`

---

### 技術棧


| 層級          | 技術                        | 說明                          |
| ----------- | ------------------------- | --------------------------- |
| 前端          | React、TypeScript、Vite     | 即時監控儀表板                     |
| 後端          | C# / ASP.NET Core Web API | 主業務邏輯、REST API              |
| 事件串流        | **Kafka（KRaft mode）**     | OT→IT 事件骨幹（新增）              |
| 業務路由        | **RabbitMQ（AMQP）**        | alert / workorder queue（新增） |
| 結構化資料庫      | PostgreSQL                | 業務資料、工單、異常記錄                |
| 時序資料庫       | InfluxDB                  | 機台心跳、良率趨勢                   |
| 視覺化         | Grafana                   | InfluxDB 時序儀表板（規劃）          |
| Python 服務   | FastAPI、psycopg、numpy     | SPC 計算服務、消費者 Workers         |
| 容器化         | Docker Compose            | 全服務一鍵啟動                     |


---

### 功能模組

- **Defect Review**：匯入缺陷資料/影像、標記 True/False、Review history、相似案例查詢
- **Fab Monitoring**：tool/lot/wafer dashboard、yield/alarm/defect trend、異常查詢
- **SPC 統計製程管制（新增）**：Xbar-R/I-MR/P/C 管制圖、八大規則偵測、Ca/Cp/Cpk 製程能力
- **事件驅動告警**：Kafka → RabbitMQ alert queue → 自動寫入告警記錄

---

### 資料流

```
ingestion（Producer + Kafka→DB writer）
  → Kafka publish（aoi.inspection.raw）
  → ingestion consumer group → PostgreSQL（process_runs）
  → spc-service Live API（讀 DB 計算 SPC）
  → React SPC Dashboard（Live 模式）
```

---

### Repo 結構

- `frontend/`：React + TypeScript（Vite）
- `backend/`：ASP.NET Core Web API（Api / Application / Domain / Infrastructure）
- `services/`：Python（`ingestion` / `spc-service`）
  - `ingestion/`：單一容器（Producer + Kafka Consumer + PostgreSQL writer）
  - `spc-service/`：SPC 計算服務（FastAPI，port 8001，含 Live 模式）
- `infra/`：Docker Compose、Kafka、RabbitMQ、InfluxDB、DB migrations
- `docs/`：架構、ERD、API spec、Kafka / RabbitMQ 資料流、事件格式

---

### Quick Start

```bash
docker compose -p aoiops -f infra/docker/docker-compose.yml up -d
```

- **DB 健康檢查**：啟動後打 `GET /api/health/db`
- **前端**：`http://localhost:5173`（含 SPC Dashboard）
- **後端 API**：`http://localhost:8080`
- **SPC Service**：`http://localhost:8001`（FastAPI Docs：`/docs`）
- **Kafka**：`localhost:9092`（開發用）
- **RabbitMQ 管理介面**：`http://localhost:15672`（預設帳密 guest/guest）
- **InfluxDB UI**：`http://localhost:8086`

> **常見啟動衝突（很重要）**  
> - 如果你本機已經有跑 `npm run dev`（佔用 `5173`），請不要用 Docker 再起 `frontend`，否則會看到 `Bind for 0.0.0.0:5173 failed: port is already allocated`。  
> - 如果你已經有另一套 RabbitMQ（佔用 `5672`），再起第二套會看到 `Bind for 0.0.0.0:5672 failed: port is already allocated`。  
> - 建議一律用 `-p aoiops`，避免不同 compose 專案名稱互相佔 port。

> **為什麼有時候你會看到 backend/spc-service 沒起來？**  
> 這次遇到的狀況是：同一台機器上先前有其他 compose 專案（例如 `docker-*`）佔住 `8080/8001/5173/5672`，導致部分服務在「第一次 up」時沒有成功建立/綁定；後續再跑 `up -d backend spc-service` 時，compose 可能會判定容器已存在但狀態不完整，所以用 `--force-recreate` 重新建立一次最穩。

---

### 啟動指令整合（推薦照抄）

#### A) 全部都用 Docker（前端 + 後端 + SPC + ingestion 全包）

```bash
cd /Users/apple/Documents/aoi-ops-platform
docker compose -p aoiops -f infra/docker/docker-compose.yml up -d
```

#### B) 本機跑前端（`npm run dev`），其他用 Docker（避免 5173 衝突）

```bash
cd /Users/apple/Documents/aoi-ops-platform
docker compose -p aoiops -f infra/docker/docker-compose.yml up -d \
  db backend kafka rabbitmq influxdb spc-service ingestion
```

#### C) 如果 backend / spc-service 沒綁到 port（最穩補救）

```bash
docker compose -p aoiops -f infra/docker/docker-compose.yml up -d --force-recreate backend spc-service
```

---

### 開發者操作手冊（啟動 / 驗收 / 測試）

> **寫給新手的原則**：先把「端到端能動」驗收過，再去做更複雜的功能（consumer、趨勢圖、review flow）。

#### 1) 啟動（只跑 W01/W02 需要的服務）

> 如果你在某些環境遇到 `Cannot connect to the Docker daemon`，請改用 `DOCKER_HOST=unix:///var/run/docker.sock`（原因見 `docs/debug-notes.md`）。

```bash
# 只起 W02 基礎：DB + API + 前端 + Kafka/RabbitMQ/InfluxDB（全用 Docker）
DOCKER_HOST=unix:///var/run/docker.sock docker compose -f infra/docker/docker-compose.yml up -d \
  db backend frontend kafka rabbitmq influxdb

# 如果你本機已有 `npm run dev`（推薦）：Docker 不再啟動 frontend（避免 5173 衝突）
DOCKER_HOST=unix:///var/run/docker.sock docker compose -f infra/docker/docker-compose.yml up -d \
  db backend kafka rabbitmq influxdb
```

#### 2) 驗收（Smoke Test，照抄即可）

```bash
# Health（後端是否能連 DB、schema 是否存在）
curl -s http://localhost:8080/api/health/db; echo

# Lots list（W02 第一支 list API）
curl -s http://localhost:8080/api/lots | head -c 500; echo

# RabbitMQ 管理頁（應回 200）
curl -s -o /dev/null -w "RabbitMQ HTTP:%{http_code}\n" http://localhost:15672

# InfluxDB UI（應回 200）
curl -s -o /dev/null -w "InfluxDB HTTP:%{http_code}\n" http://localhost:8086
```

#### 3) 重置 DB（只有開發時才用）

> **什麼時候需要**：你改了 schema（例如把主鍵型別改成 Guid）但 DB 還是舊結構，會一直報錯。  
> **為什麼要 -v**：Postgres 的資料存在 volume 裡，不刪 volume 就不會重新初始化。

```bash
DOCKER_HOST=unix:///var/run/docker.sock docker compose -f infra/docker/docker-compose.yml down -v
```

#### 4) 跑後端測試（xUnit）

> 目前用 .NET SDK 容器跑，避免你本機沒有安裝 dotnet 也能測。

```bash
DOCKER_HOST=unix:///var/run/docker.sock docker run --rm \
  -v "$(pwd):/src" -w /src \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  bash -lc "dotnet test backend/tests/AOIOpsPlatform.Api.Tests/AOIOpsPlatform.Api.Tests.csproj"
```

---

### 文件索引（更多教學在 `docs/`）

- 新手 W02 開發指南：`docs/week2-w02-guide.md`
- 開發除錯筆記（面試可講）：`docs/debug-notes.md`
- Troubleshooting（ingestion 沒落地 process_runs）：`docs/troubleshooting-ingestion-process-runs.md`

