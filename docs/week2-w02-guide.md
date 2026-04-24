# W02（第 2 週）新手開發指南：Schema + Seed + Lots List API + Kafka/RabbitMQ/InfluxDB

> **這份文件寫給小白**：你不需要一次懂 Kafka/RabbitMQ/InfluxDB 的全部，只要照順序做，就能把「資料有了、API 有了、畫面看得到」先完成。  
> **對齊文件**：`ERD.md`（資料表）、`graph.md`（資料流）、`project.md`（分層責任）、`README.md`（快速啟動）、`structrure.md`（目錄結構）。

---

## 你這週要交付什麼（最小完成標準）

### A. PostgreSQL（Schema + Seed）
- 你能在資料庫看到至少這些表：`tools`、`lots`、`wafers`、`process_runs`、`alarms`、`defects`、`workorders`
- 資料庫裡至少有：
  - 2 台 tool（AOI-A / AOI-B）
  - 5 個 lot（LOT-001～LOT-005）
  - 1 筆 defect + 1 筆 alarm + 1 張 workorder（用來 demo）

### B. 後端 API（Lots list）
- `GET /api/lots` 會回傳 lot 清單（至少看得到 5 筆）
- `GET /api/health/db` 回傳 `canConnect=true`、`toolsTableExists=true`

### C. Infra（先求能啟動）
- `kafka`、`rabbitmq`、`influxdb` 容器能成功 `Up`
  - Kafka: `localhost:9092`
  - RabbitMQ 管理頁: `http://localhost:15672`（guest/guest）
  - InfluxDB UI: `http://localhost:8086`（admin/adminadminadmin）

---

## 為什麼這週要這樣排（新手版解釋）

- **先 Schema + Seed**：沒有資料，你做任何 UI/API 都像在「對空氣寫程式」。
- **再 Lots list API**：lots 是製造場景最常用的查詢入口，先打通「DB → API → UI」會最快看到成果。
- **Kafka/RabbitMQ/InfluxDB 先求能起**：事件流很容易一次做太多；本週先把基礎設施容器跑起來，下一週再把 consumer 寫進去。

---

## Step 0：重要提醒（如果你剛改過 schema）

> **為什麼要重置 DB**：我們把主鍵改成 Guid（對齊 `ERD.md`），這會影響資料表型別；舊資料庫不可能自動「無痛升級」。  
> **你什麼時候需要做**：如果你 DB 是舊的（之前已經跑過），看到 EF/SQL 錯誤或資料表型別不對，就重置一次。

```bash
# 會刪除 Postgres volume（開發用 OK，上線絕對不要這樣做）
DOCKER_HOST=unix:///var/run/docker.sock docker compose -f infra/docker/docker-compose.yml down -v
```

---

## Step 1：啟動 W02 需要的服務

```bash
DOCKER_HOST=unix:///var/run/docker.sock docker compose -f infra/docker/docker-compose.yml up -d \
  db backend frontend kafka rabbitmq influxdb
```

你可以用這個指令確認容器都起來：

```bash
DOCKER_HOST=unix:///var/run/docker.sock docker ps --filter name=aoiops- --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
```

---

## Step 2：確認 DB schema + seed 是否成功

### 2-1 用 Health API 先驗證（最省事）

```bash
curl -s http://localhost:8080/api/health/db; echo
```

你應該看到：
- `canConnect: true`
- `toolsTableExists: true`

### 2-2 用 Lots API 看 seed（最直覺）

```bash
curl -s http://localhost:8080/api/lots | head -c 500; echo
```

你應該看到 `LOT-001`～`LOT-005`。

---

## Step 3：前端驗收（畫面要看得到資料）

打開：
- `http://localhost:5173`

你應該看到「前端最小驗收」頁面顯示 DB 連線成功。  
下一步你可以把前端再加一段「Lots 清單」顯示（先用 `<pre>` 印出 JSON 也可以）。

---

## Step 4：Kafka / RabbitMQ / InfluxDB（本週只做啟動 + 會登入）

### RabbitMQ
- 管理頁：`http://localhost:15672`
- 帳密：`guest` / `guest`

為什麼要先學會看管理頁：  
新手在做事件驅動時，最常遇到「訊息到底有沒有進 queue」；管理頁就是最直覺的儀表板。

### InfluxDB
- UI：`http://localhost:8086`
- 帳密（compose 內的 init）：`admin` / `adminadminadmin`
- bucket：`aoiops`

### Kafka
- broker：`localhost:9092`

為什麼本週先不寫 consumer：  
consumer 寫起來會牽涉「序列化格式、重試、offset、死信、資料一致性」，新手一口氣做很容易爆炸；下週再做會比較穩。

---

## 本週你學到的「特殊技術/原理」（面試可講）

- **EF Core `EnsureCreated`（MVP 快速打通）**：先用 model 直接建表，讓新手快速驗收端到端；正式環境改 migrations。
- **Guid 主鍵（分散式追溯）**：事件可能先在 Kafka 流動，再落 DB；Guid 方便跨系統追溯與避免撞號。
- **事件串流 vs 業務路由**：
  - Kafka：讓多個 consumer group 各自獨立消費同一份資料（fan-out）
  - RabbitMQ：把「業務事件」路由到不同 queue（alert / workorder）
- **時序資料分庫（InfluxDB）**：高頻寫入、時間區間查詢，用專用時序 DB 更合理。

