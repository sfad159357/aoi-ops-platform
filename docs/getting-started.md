# Getting Started — 30 秒啟動 AOI Ops Platform

> W12 整理：把「下載 repo → 看到 SPC 即時管制圖」拆成 3 段，每段都有預期輸出。
> 為什麼要有這份文件：避免新成員在「等 Kafka KRaft 選舉」、「DB 還沒 init」、「DOMAIN_PROFILE 沒帶到」這些隱藏的時間差吃虧。

---

## 0. 系統需求

| 項目 | 最低版本 | 為什麼 |
|---|---|---|
| Docker Desktop / Engine | 24+ | KRaft Kafka image 需要新版 compose v2 |
| docker compose | v2.20+ | 用 `condition: service_healthy` 等 broker ready |
| GNU Make（可選） | 3.81+ | macOS 預設即可；Windows 可用 WSL2 |
| 可用 RAM | 4GB+ | 含 Kafka、Postgres、Influx、RabbitMQ、frontend |
| 可用 port | 5173 / 8080 / 8001 / 5432 / 9092 / 15672 / 8086 | 衝突時改 `infra/docker/docker-compose.yml` |

---

## 1. 第一次啟動（包含建 DB）

```bash
git clone https://github.com/<你>/aoi-ops-platform.git
cd aoi-ops-platform

# 為什麼用 make seed：第一次啟動需要先把 init SQL 跑進空 volume
make seed
```

`make seed` 做了 4 件事：

1. `docker compose down`（保險）
2. 砍 `aoiops_aoiops_postgres_data` volume（重新觸發 `infra/db/init/*.sql`）
3. 先單獨 `up -d db`，等 `pg_isready` healthcheck 通過
4. 才 `up -d` 全部容器（避免 backend 比 DB 早起床卡 EF Core migration）

預期 console 看到：

```
>> DB healthy
>> 已完成 seed + 啟動全部服務
```

---

## 2. 確認骨架活著

```bash
make smoke
```

預期所有 7 條都 `200 OK`（health / meta/profile / lots / alarms / workorders / trace / metrics）。
如果 `metrics` 顯示 `total: 0`，是因為 ingestion 還沒開始送資料 → 等 30 秒再打一次。

`docker logs aoiops-backend-1 -f` 應該每秒看到一條 Serilog JSON：

```json
{"@t":"2026-04-26T12:00:01.234Z","@l":"Information","@mt":"...","service":"aoiops-backend"}
```

---

## 3. 打開前端 4 大頁

| 頁面 | URL | 預期 |
|---|---|---|
| SPC 即時管制圖 | <http://localhost:5173/spc> | 30 秒內每秒 +1 點，X̄/R 雙圖更新 |
| 工單管理 | <http://localhost:5173/workorders> | 偶爾有新工單從上方滑入 |
| 異常記錄 | <http://localhost:5173/alarms> | 偶爾有 high/medium 異常進來 |
| 物料追溯 | <http://localhost:5173/trace> | 輸入 panel_no（從 `/api/trace/panels/recent` 取） |

---

## 4. 切換情境（W11）

| 指令 | 用來 demo |
|---|---|
| `make scenario-normal` | 正常營運（少違規） |
| `make scenario-drift` | 偏移 → SPC Rule2 / Rule3 |
| `make scenario-spike` | 突波 → SPC Rule1 ±3σ |
| `make scenario-misjudge` | 噪音放大 → Rule5 / Rule6 |

每次切完約 10 秒後，前端 SPC 圖才會反映新情境（因為要先把 `_windows` 的舊 25 點推完）。

---

## 5. 切換 Domain Profile

```bash
# 切到半導體（重啟 backend / frontend）
make profile-semiconductor

# 切回 PCB
make profile-pcb
```

驗證：

```bash
curl -s http://localhost:8080/api/meta/profile | jq .profile_id
# 應顯示 "semiconductor" 或 "pcb"
```

前端的選單名稱、KPI 標籤、SPC 參數選項會跟著切（不需 hard reload，但要重連 SignalR）。

---

## 6. 收尾

```bash
make down            # 關掉容器，保留 DB volume
make seed            # 完全清空（demo 完想還原乾淨環境）
```

---

## 7. 進階

- 看 metrics：`watch -n 2 'curl -s http://localhost:8080/api/metrics | jq .spc'`
- 看 backend JSON log：`docker logs -f aoiops-backend-1 | jq -c '{t:."@t", l:."@l", m:."@mt"}'`
- 看 RabbitMQ queue：<http://localhost:15672>（guest / guest）→ Queues → `alert` / `workorder`
- 看 Kafka topic：`docker exec -it aoiops-kafka-1 kafka-console-consumer --bootstrap-server localhost:9092 --topic aoi.inspection.raw --from-beginning`

卡住了？先看 [troubleshooting-ingestion-process-runs.md](troubleshooting-ingestion-process-runs.md)。
