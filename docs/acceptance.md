# Acceptance Checklist — AOI Ops Platform v2

> W12 整理：v2 重新對齊 PCB MES 後的「驗收清單」，每一條都是「能執行、能看到、能截圖」的具體驗證。
> 為什麼要這份文件：避免「demo 跑得起來」≠「需求都對得上」；用清單把每個需求綁到一個可驗證指令或 URL。

打勾規則：每條提供「驗證方式」+「預期結果」+「不通過時看哪份文件」。

---

## A. 啟動與容器

| # | 驗證項目 | 驗證方式 | 預期結果 | 失敗看 |
|---|---|---|---|---|
| A1 | 一鍵啟動 | `make seed && make up` | 全部容器在 `make ps` 都是 healthy | [getting-started.md](getting-started.md) |
| A2 | 7 大 API 通 | `make smoke` | 7 條都 `200 OK`，無 connection refused | [api-spec.md](api-spec.md) |
| A3 | 容器互相 healthcheck | `docker inspect --format '{{.State.Health.Status}}' aoiops-db-1 aoiops-kafka-1 aoiops-rabbitmq-1` | 三個都 `healthy` | [observability.md §容器層 healthcheck](observability.md) |

---

## B. SPC 即時推播（核心）

| # | 驗證項目 | 驗證方式 | 預期結果 | 失敗看 |
|---|---|---|---|---|
| B1 | 前端連得到 SignalR `/hubs/spc` | DevTools → Network → WS → 過濾 `negotiate` | 200 OK + `connectionToken`，後續 WS upgrade 成功 | [realtime-signalr.md](realtime-signalr.md) |
| B2 | SPC 點每秒進來 | <http://localhost:5173/spc> 觀察 30 秒 | X̄ 圖每秒至少 +1 點，KPI「總點數」遞增 | [realtime-signalr.md](realtime-signalr.md) |
| B3 | filter 切換會切 group | DevTools Network 看 SignalR 訊息 | 切站別 → 看到 `RemoveFromGroup` + `AddToGroup` 訊息 | [realtime-signalr.md §SpcHub 群組](realtime-signalr.md) |
| B4 | 違規會即時高亮 | `make scenario-spike` 後等 10 秒 | X̄ 圖出現紅點 + 違規表新增 RuleId=1 列 | [observability.md §SIM_SCENARIO](observability.md) |
| B5 | 八大規則 C# 版正確 | `cd backend && dotnet test` | 6 個 SpcRulesEngineTests 全綠 | `backend/tests/AOIOpsPlatform.Api.Tests/SpcRulesEngineTests.cs` |
| B6 | 違規記入 metrics | `curl -s :8080/api/metrics \| jq .spc.violations` | spike 情境下持續遞增 | [observability.md §metrics endpoint](observability.md) |

---

## C. 異常記錄（事件驅動）

| # | 驗證項目 | 驗證方式 | 預期結果 | 失敗看 |
|---|---|---|---|---|
| C1 | RabbitMQ alert queue 有訊息 | <http://localhost:15672> → Queues → `alert` | message rate > 0 | [api-spec.md §RabbitMQ](api-spec.md) |
| C2 | .NET AlarmRabbitWorker 寫 DB | `psql -h localhost -U postgres -d aoiops -c 'select count(*) from alarms;'` | 數字隨時間遞增 | [getting-started.md §卡住了](getting-started.md) |
| C3 | 前端 SignalR 收得到 alarm | <http://localhost:5173/alarms> 觀察 1 分鐘 | 至少新增一筆 + 上方滑入動畫 | [realtime-signalr.md §AlarmHub](realtime-signalr.md) |
| C4 | 沒有「DB 寫了但前端沒收到」 | DevTools console 看時間戳 | DB insert log 與 SignalR 推播時間差 < 100ms | [observability.md](observability.md) |

---

## D. 工單管理

| # | 驗證項目 | 驗證方式 | 預期結果 | 失敗看 |
|---|---|---|---|---|
| D1 | RabbitMQ workorder queue 有訊息 | <http://localhost:15672> → Queues → `workorder` | high severity defect 會路由到此 | [project.md](../project.md) |
| D2 | .NET WorkorderRabbitWorker 寫 DB + 推播 | <http://localhost:5173/workorders> | 偶爾有新工單列滑入 | [realtime-signalr.md §WorkorderHub](realtime-signalr.md) |
| D3 | REST 預載歷史 | F5 重整頁面 | 過去 50 筆工單秒載入（不靠輪詢） | [api-spec.md](api-spec.md) |

---

## E. 物料追溯查詢

| # | 驗證項目 | 驗證方式 | 預期結果 | 失敗看 |
|---|---|---|---|---|
| E1 | 拿到近期 panel_no | `curl -s :8080/api/trace/panels/recent?take=3 \| jq` | 3 筆 panel_no 格式 `PCB-YYYYMMDD-NNNN` | [traceability.md](traceability.md) |
| E2 | 6 站時間軸 | `curl -s :8080/api/trace/panel/<panelNo> \| jq .stations` | 6 條，code 為 SPI/SMT/REFLOW/AOI/ICT/FQC | [traceability.md §資料模型](traceability.md) |
| E3 | 物料批號可查 | 同上 | `materials` 欄位包含錫膏 / 電容 / 主晶片 / FR4 / 助焊劑 | [traceability.md §資料模型](traceability.md) |
| E4 | 同批次列表可跳板 | 前端 <http://localhost:5173/trace> | 點「同 lot 其他板」可切換 | [traceability.md §前端](traceability.md) |

---

## F. Domain Profile 多廠切換

| # | 驗證項目 | 驗證方式 | 預期結果 | 失敗看 |
|---|---|---|---|---|
| F1 | 後端 profile 載得到 | `curl -s :8080/api/meta/profile \| jq .profile_id` | `pcb` 或 `semiconductor` | [domain-profile.md](domain-profile.md) |
| F2 | 切換指令會改 profile | `make profile-semiconductor` 後再 F1 | 變成 `semiconductor` | [domain-profile.md §切換](domain-profile.md) |
| F3 | 前端文案跟著切 | F5 重整 <http://localhost:5173/spc> | 標題、KPI 名稱、SPC 參數選項全變 | [domain-profile.md §前端](domain-profile.md) |
| F4 | DB schema 不動 | `psql -c '\\dt'` 比對 | 表名不變（仍是 wafers / lots / tools） | [domain-profile.md §不被影響](domain-profile.md) |

---

## G. 觀測（Logging + Metrics）

| # | 驗證項目 | 驗證方式 | 預期結果 | 失敗看 |
|---|---|---|---|---|
| G1 | Serilog JSON | `docker logs aoiops-backend-1 \| head -3 \| jq` | 每行有 `@t / @l / @mt / service` | [observability.md §Serilog](observability.md) |
| G2 | metrics endpoint | `curl -s :8080/api/metrics \| jq` | spc / alarm / workorder 三組數字 | [observability.md §metrics endpoint](observability.md) |
| G3 | push 延遲可查 | `curl :8080/api/metrics \| jq .spc.p95LatencyMs` | 個位數 ms（單機 demo） | [observability.md](observability.md) |
| G4 | 違規累計遞增 | `make scenario-spike` 後等 30 秒 + G3 | `spc.violations` 明顯 +N | [observability.md §SIM_SCENARIO](observability.md) |

---

## H. 已移除清單（不該再出現）

| # | 驗證項目 | 驗證方式 | 預期結果 |
|---|---|---|---|
| H1 | 沒有 MQTT / Mosquitto | `rg -n 'mosquitto\|mqtt' --hidden -g '!*.lock'` | 只剩 docs 中歷史敘述（明確標 `已移除`） |
| H2 | 沒有 OPC-UA | `rg -n 'opc[-_]?ua'` | 同上 |
| H3 | 沒有 Knowledge Copilot / RAG | `rg -n 'copilot\|rag'` -i | 沒有實作層引用，只在 ERD/docs 標 deprecated |
| H4 | README 不再是「面試作品」語氣 | 開 [../README.md](../README.md) | 沒有「面試 / 履歷主訊號 / 展示你能…」字眼 |
| H5 | 沒有 services/data-simulator | `ls services/` | 不存在；功能已併入 ingestion |
| H6 | 沒有 services/kafka-consumers/db-writer | `ls services/kafka-consumers/` | 不存在；功能已併入 ingestion |

---

## I. 一次跑完的驗收指令

```bash
# A1 / A2
make seed
sleep 30
make smoke

# B5
docker run --rm -v "$PWD":/src -w /src/backend mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet test tests/AOIOpsPlatform.Api.Tests/AOIOpsPlatform.Api.Tests.csproj --logger:"console;verbosity=minimal"

# F1 / F2
curl -s :8080/api/meta/profile | jq .profile_id
make profile-semiconductor && sleep 5
curl -s :8080/api/meta/profile | jq .profile_id
make profile-pcb && sleep 5

# G2
curl -s :8080/api/metrics | jq

# B4 + G4
make scenario-spike
sleep 60
curl -s :8080/api/metrics | jq .spc.violations
```

全部通過 → v2 重新對齊計畫驗收完成。
