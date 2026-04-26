# Troubleshooting：Backend API 全掛（連不到、沒有 500）

> 本文件記錄一次真實除錯案例：DB 有資料、前端 UI 正常，但 **Backend API 全部連不上**，
> 甚至連 500 都沒有（因為根本沒建立 HTTP listener）。
>
> 症狀很像「程式當機」，但 container 又顯示 running；這類問題最容易浪費時間。

---

## 1) 症狀（Symptoms）

### 1.1 從 host 端打 API

- `curl http://localhost:8080/api/health/db` 回：
  - `Recv failure: Connection reset by peer` 或 `Connection refused`
- `http://localhost:8080/swagger` 也同樣連不上

### 1.2 docker compose 狀態

- `docker compose ps` 顯示：
  - `backend` container `Up` 但 `unhealthy`

> 為什麼看起來「沒有 500」：  
> 500 代表「HTTP request 已經進到 ASP.NET Core pipeline」。  
> 這次是 **Kestrel 根本沒有 listen 任何 port**，所以請求完全進不來，自然不會產生 500。

---

## 2) 快速定位（先判斷是不是 HTTP listener 沒起來）

### 2.1 從 container 內打 localhost

```bash
docker exec aoiops-backend-1 bash -lc "curl -v --max-time 2 http://localhost:8080/api/health/db"
```

若 container 內都 `Connection refused`，通常是 **沒有 listener**（或 listen 在別的 port）。

### 2.2 檢查 8080 是否真的在 listen（/proc/net/tcp）

```bash
docker exec aoiops-backend-1 bash -lc "grep -i ':1F90' /proc/net/tcp || echo 'NO 8080 LISTEN'"
```

`1F90` 是 8080 的 16 進位。若顯示 `NO 8080 LISTEN`，代表 **Kestrel 沒起來**。

---

## 3) 根因（Root Cause）

### 3.1 `BackgroundService.ExecuteAsync` 沒有先 `await`

專案中 `KafkaConsumerHostedService` 是 `BackgroundService`。  
在 .NET Host 啟動流程中，會呼叫各 `IHostedService.StartAsync()`，而 `BackgroundService` 的預設實作會啟動 `ExecuteAsync()`。

如果 `ExecuteAsync()` 在進入長迴圈前 **沒有任何 await**（例如立刻進入 `while` / `Consume()`），
就有機會在啟動階段「同步占住」啟動執行緒，造成：

- background worker log 看起來正常（Kafka/RabbitMQ 在跑）
- 但 **Kestrel 沒機會完成啟動 → 沒有任何 port listen**
- 對外所有 API 都是 reset/refused（看起來像整個後端死掉）

> 這種 bug 的可怕點：  
> 沒有 exception、container 不會退出、log 也不一定會提示「Web server 沒啟動」。

---

## 4) 修正（Fix）

### 4.1 在 consume loop 前加入 `await Task.Yield()`

檔案：`backend/src/Infrastructure/Messaging/KafkaConsumerHostedService.cs`

做法：在 `ExecuteAsync()` 的 consume loop 前加：

- `await Task.Yield();`

為什麼有效：

- `Task.Yield()` 會把後續工作切到 thread pool
- 讓 Host 啟動流程先完成（Kestrel 先 listen）
- 然後 Kafka consume loop 再持續跑（不互相卡住）

### 4.2 backend 改成 publish 後的 runtime 執行（更貼近企業部署）

新增：`backend/Dockerfile`

- build stage：`dotnet publish`
- runtime stage：`mcr.microsoft.com/dotnet/aspnet:8.0` 執行 `AOIOpsPlatform.Api.dll`
- 同時把 `shared/domain-profiles` 打包到 image 內，避免 runtime 讀不到 profile
- 安裝 `curl`，避免 docker compose healthcheck 因「image 不含 curl」永遠失敗

### 4.3 docker-compose 讓 backend 用 image build

檔案：`infra/docker/docker-compose.yml`

- `backend` 改用：
  - `build: { context: ../../, dockerfile: backend/Dockerfile }`
- `Domain__ProfilesDirectory` 改成 container 內路徑：
  - `/app/shared/domain-profiles`

---

## 5) 驗證（Verify）

```bash
curl -fsS --max-time 5 http://localhost:8080/api/health/db
```

預期回：

```json
{"canConnect":true,"toolsTableExists":true}
```

同時：

- `docker compose ps backend` 顯示 `healthy`
- `http://localhost:8080/swagger` 打得開

---

## 6) 後續建議（避免再踩一次）

- **規範**：所有 `BackgroundService.ExecuteAsync` 在進入長迴圈前，至少要有一次 `await`（例如 `Task.Yield()` 或短 delay）。
- **觀測**：啟動階段最好印出「Now listening on ...」或 `app.Urls`（避免「以為起來了其實沒 listen」）。
- **部署一致**：開發環境也盡量用 publish/runtime 的方式跑，減少 dev tooling 差異。

