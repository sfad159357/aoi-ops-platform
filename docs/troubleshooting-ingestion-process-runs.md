# Troubleshooting：`ingestion` 沒有把資料寫進 `process_runs`

本文件紀錄一次「SPC Live 模式一直顯示資料不足」的長時間除錯過程：我們遇到什麼問題、如何發現線索、怎麼一步步排除、最後的根因是什麼、以及修正與驗證方式。

---

## 背景與目標

### 目標資料流（MVP）

- **設備模擬/Producer**：`ingestion` 送事件到 Kafka topic `aoi.inspection.raw`
- **DB Writer/Consumer**：`ingestion` 消費 `aoi.inspection.raw` 寫入 PostgreSQL `process_runs`
- **SPC Live**：`spc-service` 從 PostgreSQL 讀 `process_runs` 計算 I-MR 等控制圖

### 現象

- 前端（或直接 curl）呼叫：

```bash
curl -s "http://127.0.0.1:8001/api/spc/live/imr?metric=temperature&limit=60"
```

- 回傳：

```json
{"detail":"資料不足（至少需要 10 點才能計算 I-MR）"}
```

### 初步確認（DB 端）

`process_runs` 筆數長期停在 1（或非常低），表示 ingestion 並沒有持續落地資料：

```bash
docker exec aoiops-db-1 psql -U postgres -d AOIOpsPlatform -c "select count(*) from process_runs;"
```

---

## Troubleshooting 過程（按時間線整理）

> 這段刻意用「假設 → 驗證 → 結論」的方式寫，因為這次卡最久的點不是單一錯誤，而是多個因素疊在一起。

### 1) 假設 A：Producer 根本沒有送到 Kafka

#### 驗證方法

- 先看 `ingestion` logs 是否有 Kafka 連線錯誤（如 `NoBrokersAvailable`）。
- 再用容器內直接消費 topic，確認 topic 是否真的有訊息。

#### 觀察到的線索

`ingestion` logs 多次出現：

- `kafka.errors.NoBrokersAvailable`
- `KafkaTimeoutError: Failed to update metadata after 60.0 secs.`

這代表 **Producer/Consumer 端在 Kafka 未就緒或 Kafka 設定異常時，無法拿到 broker metadata**。

#### 結論

- Producer/Consumer 都可能在「Kafka 還沒 ready 或設定錯」時就初始化失敗。
- 即使 Docker 容器看起來在跑，thread 一旦崩潰就不會再寫入 DB（造成「容器活著、資料不動」）。

---

### 2) 假設 B：Kafka 起來了，但 hostname/DNS 指向錯誤

#### 觀察到的線索（Kafka logs）

Kafka logs 曾出現類似：

- `UnknownHostException: aoiops-kafka`

原因是 compose 移除了固定 `container_name` 之後，Kafka 內部設定仍引用舊 hostname（例如 `aoiops-kafka`），導致 controller/broker 的自我連線失敗。

#### 修正

把 Kafka 設定中的 controller quorum host 改成 **compose service name `kafka`**：

- `KAFKA_CONTROLLER_QUORUM_VOTERS=1@kafka:9093`

#### 結論

這一步讓 Kafka 能正常進入 RUNNING，但仍未完全解決「consumer 永遠拿不到資料」的問題（因為後面還有更關鍵的根因）。

---

### 3) 假設 C：consumer group 協調/分配出了問題（poll 永遠 0 筆）

#### 症狀特徵

在 ingestion 容器內測試 consumer group 時：

- `poll` 一直是 0
- `assignment` 一直是空集合 `set()`

這通常代表 **consumer group 連上了 bootstrap，但無法完成 group 協調 / partition 分配**。在單 broker 的 Kafka 開發環境，最常見原因之一是 `__consumer_offsets` topic 的 replication factor 不符合單 broker 條件。

---

### 4) 關鍵根因：Kafka 單 broker 的 offsets/transaction topic replication factor 預設不適用

#### 為什麼會造成「poll 永遠 0」「writer 永遠不寫 DB」

- consumer group 需要使用 `__consumer_offsets` 來管理 group state、offset commit。
- Kafka 在很多預設設定下，`__consumer_offsets` 的 replication factor 可能是 3。
- **單 broker**（只有 1 台 broker）時，RF=3 會導致 offsets topic 無法正確建立/維持，進而讓 group 協調與分配失敗。
- 結果就是：看似 consumer 存在、程式也沒一直噴錯，但 **永遠沒有 assignment**，所以 writer loop 永遠拿不到 records。

#### 修正（docker-compose.yml）

在 `infra/docker/docker-compose.yml` 的 `kafka` service 追加（單 broker 必備）：

- `KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1`
- `KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR=1`
- `KAFKA_TRANSACTION_STATE_LOG_MIN_ISR=1`
- `KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS=0`（開發體驗：縮短 rebalance 等待）

---

### 5) 同步修正：ingestion 增加「自動重試/重連」避免 Kafka 未 ready 時直接崩潰

#### 為什麼要做這個修正

這次除錯過程中反覆看到：

- Kafka 容器「已啟動」不等於「可用」
- ingestion 一開始連不上 Kafka 時，`KafkaProducer/KafkaConsumer` 初始化會拋 `NoBrokersAvailable`
- 原本 ingestion 的 thread 會直接 crash，導致後續 Kafka 就算變正常，**也不會自動恢復**

#### 修正（ingestion）

在 `services/ingestion/app/__main__.py` 做了兩個關鍵調整：

- **Producer/Consumer 建立失敗時不退出 thread，而是 backoff 後重試**
- **KafkaTimeout / NoBrokersAvailable 時丟棄舊 client 並重建**（避免卡在 metadata）

另外加上低頻率的計數 log：

- Producer：每 10 筆印一次 `sent=...`
- Writer：每 10 筆印一次 `inserted=...`

這樣可以用 logs 快速判斷：

- 是否真的在送 Kafka（sent 有在跑）
- 是否真的有落地 DB（inserted 有在跑）

---

## 最終驗證（確認修好）

### 1) DB 筆數會持續上升

```bash
docker exec aoiops-db-1 psql -U postgres -d AOIOpsPlatform -c "select count(*) from process_runs;"
```

預期：`count` 會從 1 → 10+ → 持續增加。

### 2) ingestion logs 會看到 writer inserted 計數

```bash
docker logs --since 1m aoiops-ingestion-1
```

預期會看到類似：

- `[ingestion:producer] sent=10 ...`
- `[ingestion:writer] inserted=10`

### 3) SPC Live IMR 不再回 422

```bash
curl -s "http://127.0.0.1:8001/api/spc/live/imr?metric=temperature&limit=60" | head -c 200
```

預期：回傳包含 `x_points` / `mr_points` 等計算結果的 JSON。

---

## 本次學到的教訓（下次更快）

- **單 broker Kafka 一定要把 offsets/transaction replication factor 調成 1**，否則 consumer group 可能「看似正常但永遠不分配」。
- **container started ≠ service ready**：Kafka/DB 常會比 app 晚 ready，MVP ingestion 必須具備重試能力，不然很容易出現「必須手動 restart 才恢復」。
- **加「低頻率健康指標 log」很重要**：用 `sent/inserted` 就能在 10 秒內判斷卡在 Kafka、卡在 consumer group、還是卡在 DB。


