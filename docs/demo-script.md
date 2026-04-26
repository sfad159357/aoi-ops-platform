# Demo Script — 6 分鐘走完整條 PCB 產線

> W12 整理：用來在面試 / 內部 review / 錄影時，依時間順序示範
> 「產線即時推播 → SPC 違規 → 異常 → 工單 → 物料追溯 → profile 切換」一條龍。

---

## 0. 開場前 30 秒（不錄）

```bash
# 1) 確保乾淨環境
make seed

# 2) 等到 backend healthy（約 30 秒）
docker compose -p aoiops -f infra/docker/docker-compose.yml ps

# 3) 預先把以下分頁打開
#  - http://localhost:5173/spc
#  - http://localhost:5173/alarms
#  - http://localhost:5173/workorders
#  - http://localhost:5173/trace
#  - http://localhost:8080/api/metrics
#  - http://localhost:15672  (guest / guest)
```

---

## 1. 系統概覽（30 秒）

> 「這是模擬 **PCB SMT 產線（錫膏印刷 → 貼片 → 回焊 → AOI → ICT → FQC）** 的 MES 品質模組。
> 主軸是 **即時生產 → 儲存 → 消費 → API 回傳 → 即時監控 → 即時運算 → 業務模組 → 可追溯查表**。」

打開 [graph.md](../graph.md) 第一張 mermaid 系統脈絡圖，
重點念出來：

- **Kafka** = 設備感測層 fan-out（aoi.inspection.raw / aoi.defect.event）
- **RabbitMQ** = 業務層分級路由（alert / workorder）
- **.NET SignalR** = 唯一推播給前端的入口（SpcHub / AlarmHub / WorkorderHub）
- **PostgreSQL** = 業務寫入 + 物料追溯
- **InfluxDB** = 時序

---

## 2. SPC 即時管制圖 — 正常情境（45 秒）

```bash
make scenario-normal
```

切到瀏覽器 <http://localhost:5173/spc>：

- 看到 4 KPI（總點數 / 違規數 / Cpk / 合格率）每秒跳動
- X̄ 圖 / R 圖 雙圖每秒新增一點，落在 ±3σ 內
- 切換站別 / 機台 / 參數 → SignalR 自動 unsubscribe + subscribe group

念稿：
> 「資料來自 ingestion 模擬器發到 Kafka，.NET 的 `SpcRealtimeWorker` 消費後跑八大規則 + Cpk，
> 透過 SignalR group `line:{lineCode}|param:{parameterCode}` 推給前端。
> 整段沒有 polling，沒有 demo 假資料。」

---

## 3. 觸發 SPC 違規（90 秒）

```bash
make scenario-spike
```

10 秒後在 <http://localhost:5173/spc>：

- X̄ 圖出現紅點（Rule 1：超過 ±3σ）
- 違規表新增一列 `RuleId=1, severity=red, 描述=單點超出 3σ`
- KPI「違規數」+1

切到 <http://localhost:8080/api/metrics>：
```json
{
  "spc": { "violations": 12, "p95LatencyMs": 3.18 }
}
```

念稿：
> 「即時違規不是查表算的，是消費 Kafka 訊息當下就算完，
> 透過 SignalR 推到前端，整條 push 路徑量到的 p95 延遲是個位數毫秒。」

繼續展示 drift / misjudge：
```bash
make scenario-drift     # Rule 2 / Rule 3
make scenario-misjudge  # Rule 5 / Rule 6
```

---

## 4. 異常記錄事件驅動（45 秒）

切到 <http://localhost:5173/alarms>：

- 觀察右上方有「[1] 新異常」flash 動畫
- 列表頂端滑入新的 high / medium / low 異常列

念稿：
> 「這條路徑是 **Kafka aoi.defect.event → Python rabbitmq-publisher → RabbitMQ alert queue → .NET AlarmRabbitWorker**。
> .NET 寫入 PostgreSQL alarms 表後，**同一個 method 內**再透過 SignalR 推給前端，
> 不會有『DB 已寫入但前端沒收到』的時間窗。」

切到 RabbitMQ 管理介面 <http://localhost:15672> → Queues → `alert`，看到 message rate 起伏。

---

## 5. 工單即時新增（30 秒）

切到 <http://localhost:5173/workorders>：

- 偶爾有新工單列從上方滑入

念稿：
> 「同一套機制，high severity 的 defect 會在 rabbitmq-publisher 加路由到 `workorder` queue，
> .NET WorkorderRabbitWorker 消費後寫 DB + 推 SignalR。事件驅動，不靠 cron。」

---

## 6. 物料追溯查詢（60 秒）

```bash
# 從 API 拿一個近期 panel_no
curl -s 'http://localhost:8080/api/trace/panels/recent?take=3' | jq
```

複製 `panel_no` 例如 `PCB-20260426-0042`，貼到 <http://localhost:5173/trace>：

預期顯示：

- **板資訊**：panel_no / 工單 / 產品料號 / 狀態
- **6 站時間軸**：SPI → SMT → REFLOW → AOI → ICT → FQC，每站都有 in/out 時間 + 機台
- **使用物料批號**：錫膏 / 電容 / 主晶片 / FR4 / 助焊劑
- **同批次列表**：點進去可跳到別片板

念稿：
> 「這是 W08 加的功能。新增 3 張表（material_lots / panel_material_usage / panel_station_log）+
> wafers.panel_no 對外可讀識別。一張表查不到的問題，串起來就有了。」

---

## 7. Domain Profile 切換（45 秒）

```bash
# 切到半導體
make profile-semiconductor
```

刷新前端：

- 選單變「Wafer 追溯」、SPC 參數變 temperature / pressure / yield_rate、廠區名變了
- DB 表結構**完全沒動**

念稿：
> 「這就是 W05 加的 Domain Profile 機制。`shared/domain-profiles/{pcb,semiconductor}.json`
> 控制中文標籤、站別、量測參數（含 USL/LSL/Target）、選單。
> 同一份 codebase，可以對應 PCB 廠 / 半導體廠不同產業。」

```bash
# 切回 PCB
make profile-pcb
```

---

## 8. 觀測收尾（15 秒）

```bash
curl -s http://localhost:8080/api/metrics | jq
docker logs -f aoiops-backend-1 | head -3
```

念稿：
> 「demo 過程的所有事件數、延遲、Serilog JSON log 都在這裡，
> 之後可以直接接 Prometheus / Grafana / Loki，不用改程式。」

---

## 9. 結束

```bash
make down
```

> 「整套架構：Kafka 設備層 / RabbitMQ 業務層 / SignalR 推播 / PostgreSQL 業務 + 追溯 / InfluxDB 時序 /
> Domain Profile 多廠切換 / 觀測 metrics + Serilog。
> 沒有 ML、沒有 RAG、沒有 MQTT、沒有 OPC-UA — 專注在『真實 MES 品質模組』本身。」
