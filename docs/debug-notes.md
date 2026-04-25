# 開發除錯筆記（給自己與面試官看的）

> **為什麼要有這份文件**：把「會卡住的關鍵問題」記下來，避免下次重踩；面試官問到時也能講清楚「你怎麼定位問題、怎麼修」。
>
> **收錄原則**：只記「會讓功能整段卡死」或「容易反覆踩到」的問題；太小的 typo、低級小失誤就不寫。

---

## 2026-04-24｜W01 開發容器驗收（frontend/backend/db）關鍵除錯

### 1) Docker 在 IDE/腳本環境無法連線（Docker daemon socket 路徑差異）

- **症狀**
  - 執行 `docker ps` 出現：`Cannot connect to the Docker daemon at unix:///Users/apple/.docker/run/docker.sock`
- **為什麼會發生（根因）**
  - 同一台機器可能同時存在兩個 socket 路徑：
    - `~/.docker/run/docker.sock`（Docker Desktop 使用者路徑）
    - `/var/run/docker.sock`（系統常見預設路徑）
  - 某些執行環境（例如 IDE 內的 sandbox / 權限隔離）會連不到使用者路徑，但能連系統 socket。
- **怎麼解（解法）**
  - 明確指定 `DOCKER_HOST`：

```bash
DOCKER_HOST=unix:///var/run/docker.sock docker ps
```

- **如何避免下次再踩**
  - 把常用命令包成腳本（例如 `scripts/dev/docker.sh`），統一指定 `DOCKER_HOST`。

---

### 2) Backend 容器起不來：Npgsql 套件版本不存在 → NuGet 自動跳版造成 EF Core 衝突

- **症狀**
  - `aoiops-backend` 容器 `Exited (1)`
  - `dotnet restore` 失敗，log 顯示：
    - `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.13 was not found`
    - `Detected package downgrade: Microsoft.EntityFrameworkCore from 9.0.0 to 8.0.13 (NU1605)`
- **為什麼會發生（根因）**
  - 專案指定 `Npgsql.EntityFrameworkCore.PostgreSQL` 為 `8.0.13`，但 NuGet 上根本沒有這個版本。
  - NuGet 會「自動找最接近版本」→ 解析到 `9.0.0`，而 `Npgsql 9.x` 依賴 `EF Core 9.x`，就跟專案的 `.NET 8 / EF Core 8` 打架。
- **怎麼解（解法）**
  - 把 Npgsql 版本改成真正存在且相容 EF Core 8 的版本（此 repo 使用 `8.0.11`）。
- **改了哪些檔案**
  - `backend/src/Infrastructure/AOIOpsPlatform.Infrastructure.csproj`
  - `backend/src/Api/AOIOpsPlatform.Api.csproj`
- **如何避免下次再踩**
  - 看到 restore 類錯誤先檢查「該版本是否存在」；不要只看本機 cache 是否能還原。

---

### 3) Backend 綁錯 port：compose 設定的 `ASPNETCORE_URLS` 被 `launchSettings.json` 覆蓋

- **症狀**
  - Docker Compose 已設定：
    - `ASPNETCORE_URLS=http://0.0.0.0:8080`
    - port mapping `8080:8080`
  - 但後端 log 顯示：
    - `Now listening on: http://localhost:5124`
  - 結果就是：瀏覽器打 `http://localhost:8080` 可能打不到（或打到空 port）。
- **為什麼會發生（根因）**
  - `dotnet watch run` 在某些情境會讀取 `Properties/launchSettings.json` 的 profile。
  - repo 的 `launchSettings.json` 裡 `http` profile 寫死 `applicationUrl=http://localhost:5124`，導致容器裡也跟著綁到 5124（而且是 localhost）。
- **怎麼解（解法）**
  - 在 compose 的後端啟動命令加入 `--no-launch-profile`，強制忽略 `launchSettings.json`，改以 `ASPNETCORE_URLS` 為準。
  - 同時補一個 `Docker` profile（方便本機開發者理解 docker 該用的 URL）。
- **改了哪些檔案**
  - `infra/docker/docker-compose.yml`
  - `backend/src/Api/Properties/launchSettings.json`
- **如何避免下次再踩**
  - 容器化時：**不要依賴 launchSettings 決定 port**；用環境變數與 compose/yaml 管理。

---

### 4) Health API 回傳 500：EF Core `SqlQuery<bool>` 的 SQL 結尾分號造成 Postgres 語法錯誤

- **症狀**
  - 打 `GET /api/health/db` 出現 Postgres error：
    - `42601: syntax error at or near ";"`（通常會看到 stack trace）
- **為什麼會發生（根因）**
  - EF Core `SqlQuery<T>` 會把你提供的 SQL 包進子查詢（概念上類似：`SELECT ... FROM (<你的SQL>) t`）。
  - 如果你在 `<你的SQL>` 結尾寫了分號 `;`，就會破壞外層 SQL 的語法結構，導致 42601。
- **怎麼解（解法）**
  - 移除 SQL 結尾分號，讓 EF Core 自行控制 SQL 邊界。
- **改了哪些檔案**
  - `backend/src/Api/Controllers/HealthController.cs`
- **如何避免下次再踩**
  - 在 EF Core 的 raw SQL（`SqlQuery` / `FromSql`）裡，**避免把分號當成「句尾」**。

---

## 2026-04-25｜SPC Live 卡住：ingestion 沒有持續寫入 `process_runs`

### 1) 症狀：SPC Live 回 422（資料不足），DB `process_runs` 筆數不增加

- **症狀**
  - `GET /api/spc/live/imr` 回：`資料不足（至少需要 10 點才能計算 I-MR）`
  - DB 查詢 `select count(*) from process_runs;` 長期停在 1（或很低）
- **代表什麼**
  - Kafka→DB 的落地鏈路沒有真的在跑（producer/consumer 任一段出問題都會造成 DB 沒新增）

### 2) 關鍵根因：Kafka 單 broker 未調整 offsets/transaction replication factor

- **症狀特徵**
  - consumer group `poll` 永遠是 0
  - consumer `assignment` 永遠是空集合（代表分配不到 partition）
- **為什麼會發生（根因）**
  - 單 broker 開發環境若沒把 `__consumer_offsets` 相關 replication factor 調成 1，consumer group 協調可能失敗
- **怎麼解（解法）**
  - 在 `infra/docker/docker-compose.yml` 的 Kafka 增加：
    - `KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1`
    - `KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR=1`
    - `KAFKA_TRANSACTION_STATE_LOG_MIN_ISR=1`
    - `KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS=0`

### 3) 加強可靠性：ingestion 增加自動重試/重連

- **為什麼要做**
  - docker compose 啟動時「容器 started」不等於 Kafka ready，第一次連不上就 crash 會讓你誤以為服務還活著
- **怎麼解**
  - `services/ingestion/app/__main__.py` 加入 backoff 重試，並在 logs 打 `sent/inserted` 低頻計數方便驗證

### 4) 另一個常見坑：compose 專案名稱不同造成 port 衝突

- **症狀**
  - `Bind for 0.0.0.0:5173/5672/8080/8001 failed: port is already allocated`
- **怎麼避免**
  - 固定使用 `docker compose -p aoiops ...`
  - 若其他專案佔用 port，停掉非 `aoiops-*` 的容器

> 更完整的時間線與命令範本請看：`troubleshooting-ingestion-process-runs.md`

---

## 常用驗收指令（下次照抄）

```bash
# 1) 起服務（只起 W01 需要的三個）
DOCKER_HOST=unix:///var/run/docker.sock docker compose -f infra/docker/docker-compose.yml up -d db backend frontend

# 2) 檢查容器狀態
DOCKER_HOST=unix:///var/run/docker.sock docker ps

# 3) Backend Swagger
curl -I http://localhost:8080/swagger/index.html

# 4) DB 健康檢查（後端是否能連 DB、schema 是否存在）
curl http://localhost:8080/api/health/db

# 5) Frontend 是否可連
curl -I http://localhost:5173
```

