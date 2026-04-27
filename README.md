# AOI Ops Platform

PCB SMT 產線 MES 品質模組：以 **Kafka 即時推播 + .NET Core SignalR** 為骨幹，
即時計算 SPC 八大規則 / Cpk，並以事件驅動方式串接 **工單管理 / 異常記錄 / 物料追溯查詢**。

主軸：**即時生產 → 儲存 → 消費 → API 回傳 → 即時監控 → 即時運算 → 業務模組 → 可追溯查表**。

> 同一份 codebase 透過 Domain Profile 機制，可在 **PCB / 半導體** 兩種產業 demo 之間切換用語與規格，
> 不需改 schema，也不需改前端文案。詳見 [docs/domain-profile.md](docs/domain-profile.md)。

---

## 一、四大功能模組

| 模組 | 來源 | 推送機制 | 對應前端頁面 |
|---|---|---|---|
| ① **SPC 統計製程管制** | Kafka `aoi.inspection.raw` | SignalR `/hubs/spc`（spcPoint / spcViolation） | `SpcDashboard` |
| ② **工單管理** | RabbitMQ `workorder` | SignalR `/hubs/workorder` + REST 預載 | `WorkordersPage` |
| ③ **異常記錄** | RabbitMQ `alert`（由 Kafka `aoi.defect.event` 路由而來） | SignalR `/hubs/alarm` + REST 預載 | `AlarmsPage` |
| ④ **物料追溯查詢** | SQL Server（panel_no / 物料批號 / 同 lot） | REST `GET /api/trace/panel/{panelNo}` | `TraceabilityPage` |

---

## 二、技術棧

| 層級 | 技術 | 用途 |
|---|---|---|
| 前端 | React 19 + TypeScript + Vite + recharts | 4 大模組儀表板，連 SignalR 收推播 |
| 後端 | ASP.NET Core 8（Api / Application / Domain / Infrastructure） | REST API、SignalR Hub、Kafka / RabbitMQ consumer |
| 即時推送 | **.NET Core SignalR**（WebSocket，自動降級 SSE / Long Polling） | 4 個 Hub：spc / alarm / workorder / trace |
| 事件串流 | Apache Kafka 3.x（KRaft mode） | 設備感測層 fan-out |
| 業務路由 | RabbitMQ 3 + AMQP | alert / workorder 業務佇列 |
| 結構化資料庫 | SQL Server（Azure SQL Edge） | 業務資料、SPC 計算來源、物料追溯 |
| 時序資料庫 | InfluxDB 2.7 | 機台心跳、良率趨勢 |
| Python 服務 | FastAPI（SPC 報表）/ ingestion / kafka-influx-writer / kafka-rabbitmq-publisher | 模擬器、Kafka 消費端、批次 SPC 報表 |
| 容器化 | Docker Compose | 一鍵啟動 |

> **不做**：MQTT / Mosquitto / OPC-UA / 機器學習 / RAG / Knowledge Copilot。
> 這些功能在 v2 重新對齊時被移除，定位回到「真實 MES 品質模組」。

---

## 二點五、版本對照（.NET 8 一致性）

為了確保「本機開發 / Docker / CI」一致，本專案 **統一使用 .NET 8**：

| 場景 | 使用版本 | 設定位置 |
|---|---|---|
| 本機 `dotnet` SDK | **8.0.420** | `global.json`（固定 SDK） |
| Docker build（編譯） | **`mcr.microsoft.com/dotnet/sdk:8.0`** | `backend/Dockerfile`（build stage） |
| Docker runtime（執行） | **`mcr.microsoft.com/dotnet/aspnet:8.0`** | `backend/Dockerfile`（runtime stage） |
| Docker Compose backend | **同上（由 Dockerfile 決定）** | `infra/docker/docker-compose.yml` → `backend.build` |

> 你可以在 repo 根目錄用 `dotnet --version` 確認目前被 `global.json` 鎖定的 SDK 版本。

---

## 三、即時資料流

```
🏭 ingestion（Python，模擬 AOI 設備）
   │
   ▼ Kafka publish
⚡ Kafka（aoi.inspection.raw / aoi.defect.event）
   │
   ├─▶ Python kafka-influx-writer        ──▶ InfluxDB（時序）
   ├─▶ Python kafka-rabbitmq-publisher   ──▶ RabbitMQ（alert / workorder）
   ├─▶ .NET SpcRealtimeWorker（SignalR） ──▶ /hubs/spc（spcPoint / spcViolation）
   └─▶ .NET DefectRealtimeWorker（規劃）

🐇 RabbitMQ
   ├─ alert     ─▶ .NET AlarmRabbitWorker     ──▶ SQL Server alarms     + /hubs/alarm
   └─ workorder ─▶ .NET WorkorderRabbitWorker ──▶ SQL Server workorders + /hubs/workorder

🗄️ SQL Server ─▶ ASP.NET Core API（REST：/api/lots / alarms / workorders / trace ...）
                                       ─▶ /hubs/* SignalR 推播（事件驅動）
                                            ─▶ React 前端 4 大頁
```

詳細 sequence 圖見 [graph.md](graph.md)；SignalR 訊息格式見 [docs/realtime-signalr.md](docs/realtime-signalr.md)。

---

## 四、Quick Start

最常用 4 個指令（封裝在 `Makefile`，避免每個人記 docker compose 不同變體）：

```bash
make up               # 啟動所有容器（PCB profile）
make smoke            # 打幾個關鍵 API 確認骨架活著
make profile-semiconductor   # 切到半導體 profile（重啟 backend / frontend）
make down             # 關掉
make seed             # 砍掉 DB volume，重新 seed 一次乾淨資料
```

不想用 make 也可以直接打：

```bash
# 預設 PCB profile
docker compose -p aoiops -f infra/docker/docker-compose.yml up -d

# 切半導體 profile
DOMAIN_PROFILE=semiconductor \
  docker compose -p aoiops -f infra/docker/docker-compose.yml up -d --force-recreate backend frontend
```

啟動後可使用：

- 前端：<http://localhost:5173>
- 後端 API：<http://localhost:8080>（Swagger：`/swagger`）
- SPC 報表服務：<http://localhost:8001>（Python FastAPI，批次 / 歷史用）
- Kafka：`localhost:9092`
- RabbitMQ 管理介面：<http://localhost:15672>（guest / guest）
- InfluxDB UI：<http://localhost:8086>

`make smoke` 等同：

```bash
curl -s http://localhost:8080/api/health/db; echo
curl -s http://localhost:8080/api/meta/profile | head -c 200; echo
curl -s http://localhost:8080/api/lots | head -c 200; echo
curl -s http://localhost:8080/api/alarms?take=5 | head -c 200; echo
curl -s http://localhost:8080/api/workorders?take=5 | head -c 200; echo
curl -s "http://localhost:8080/api/trace/panels/recent?take=3"; echo
```

---

### SQL Server（Azure SQL Edge / Apple Silicon ARM64）

> **為什麼用 Azure SQL Edge**：官方 `mcr.microsoft.com/mssql/server` 只發 amd64 image，在 M1/M2 走 Rosetta 模擬常 OOM；`azure-sql-edge` 是 Microsoft 官方 ARM64 原生版，記憶體佔用低、T-SQL 約 95% 相容。
> 本專案以 SQL Server（Azure SQL Edge）為主資料庫。

```bash
make mssql-up      # 拉 image → 啟動 SQL Server → 自動建空 DB AOIOpsPlatform_MSSQL
make mssql-shell   # 進 sqlcmd 互動 shell（sa 帳號）
make mssql-down    # 停掉（保留 volume aoiops_mssql_data）
```

連線資訊（開發環境預設值，詳見 `.env`）：

| 欄位 | 值 |
|---|---|
| Host | `localhost` |
| Port | `1433` |
| 帳號 | `sa` |
| 密碼 | `Your_password123!` |
| 預設 DB | `AOIOpsPlatform_MSSQL` |

.NET 連線字串：
```
Server=localhost,1433;Database=AOIOpsPlatform_MSSQL;User Id=sa;Password=Your_password123!;TrustServerCertificate=True;
```

進 sqlcmd 後常用指令：
```sql
SELECT name FROM sys.databases;         -- 列所有資料庫
USE AOIOpsPlatform_MSSQL;               -- 切換資料庫
SELECT @@VERSION;                        -- 確認版本
GO
```

---

## 五、Repo 結構

```
aoi-ops-platform/
  frontend/             React + Vite 前端（含 SignalR client / 4 大頁）
  backend/              ASP.NET Core 8（Api / Application / Domain / Infrastructure / tests）
  services/             Python 服務
    ingestion/          Kafka producer + DB writer（單一容器）
    kafka-consumers/
      influx-writer/    Kafka → InfluxDB（時序寫入）
      rabbitmq-publisher/ Kafka → RabbitMQ（business 路由）
    spc-service/        FastAPI（批次 / 歷史 SPC 報表，port 8001）
    rabbitmq-consumers/ legacy；W07 起由 .NET 接管，docker compose 預設不啟動
  shared/
    domain-profiles/    pcb.json / semiconductor.json（同 schema 不同產業用語）
  infra/
    docker/             docker-compose.yml + healthcheck
    db/mssql-init/      SQL Server 初始化 SQL
  docs/                 architecture / realtime-signalr / domain-profile / traceability...
  scripts/              開發用 seed / smoke / 一鍵啟動
```

詳見 [structrure.md](structrure.md)。

---

## 六、其他文件

- **新成員第一次啟動**：[docs/getting-started.md](docs/getting-started.md)
- **Demo 腳本（6 分鐘走完整條 PCB 產線）**：[docs/demo-script.md](docs/demo-script.md)
- **驗收清單**：[docs/acceptance.md](docs/acceptance.md)
- 即時推播訊息格式：[docs/realtime-signalr.md](docs/realtime-signalr.md)
- 物料追溯資料模型：[docs/traceability.md](docs/traceability.md)
- Domain Profile 機制：[docs/domain-profile.md](docs/domain-profile.md)
- 觀測（Logging + Metrics + 情境演練）：[docs/observability.md](docs/observability.md)
- 系統架構圖：[graph.md](graph.md)
- ERD：[ERD.md](ERD.md)
- 專案定位：[project.md](project.md)
- 故障排查與開發筆記：[docs/](docs/)
