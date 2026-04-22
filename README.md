## AOI Ops Platform（MVP）

模擬高科技製造場景的生產資訊系統，
涵蓋設備數據收集、製程監控、缺陷管理與工程知識助理。

### 一句話總結（給面試官）
用 **.NET（分層架構）+ PostgreSQL + Python AI** 模擬 MES/AOI 場景，展示「企業後端設計 + 資料建模 + 可容器化落地」能力。

### 系統定位
本專案模擬真實 MES 架構：
- Python Data Simulator 透過 **MQTT** 模擬設備發送製造數據
- C# Backend 訂閱 MQTT 訊息，解析後寫入 PostgreSQL 與 InfluxDB
- React 前端即時呈現 dashboard、告警、缺陷清單
- AI Copilot 根據 SOP 文件與異常 context 提供 troubleshooting 建議

### 亮點（MVP 目前已落地）
- **PostgreSQL 開發環境一鍵啟動**：`docker compose up` 直接起 DB，並支援 init script
- **EF Core 資料模型已對齊 ERD**：`AoiOpsDbContext` 使用 Fluent API 管理 table/column/index
- **可快速驗證 DB 連線**：`GET /api/health/db` 回傳 `canConnect` 與 `toolsTableExists`

### 技術棧
| 層級 | 技術 |
|------|------|
| 前端 | React、TypeScript、Vite |
| 後端 | C# / ASP.NET Core Web API |
| 訊息傳輸 | MQTT（Mosquitto） |
| 結構化資料庫 | PostgreSQL |
| 時序資料庫 | InfluxDB |
| 視覺化 | Grafana（設備即時數據儀表板） |
| AI 服務 | Python / FastAPI、OpenAI RAG |
| 容器化 | Docker Compose |

### 目標功能（MVP）
- **Defect Review**：匯入缺陷資料/影像、標記 True/False、
  Review history、相似案例查詢
- **Fab Monitoring**：tool/lot/wafer dashboard、
  yield/alarm/defect trend、異常查詢
- **Knowledge Copilot**：文件上傳、檢索問答、回覆附來源

### 資料流
Python Simulator
→ MQTT publish（模擬設備數據）
→ Mosquitto Broker
→ C# Backend subscribe
→ PostgreSQL（結構化）/ InfluxDB（時序）
→ React Dashboard / Grafana 視覺化

### Repo 結構
- `frontend/`：React + TypeScript（Vite）
- `backend/`：ASP.NET Core Web API
  （Api / Application / Domain / Infrastructure）
- `services/`：Python（data-simulator / vision-helper / ai-copilot）
- `infra/`：Docker Compose、Mosquitto、InfluxDB、DB migrations
- `docs/`：架構、ERD、API spec、MQTT 資料流

### Quick Start（開發者）
```bash
docker compose -f infra/docker/docker-compose.yml up
```

- **DB 健康檢查**：啟動後打 `GET /api/health/db`
