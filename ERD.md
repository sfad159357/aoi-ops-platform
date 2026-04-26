# ERD — AOI Ops Platform

> 本文件分兩部分：
> 1. **PostgreSQL 關聯資料表**（業務核心）
> 2. **InfluxDB Measurement**（時序資料）
>
> Kafka / RabbitMQ 是訊息基礎設施，不存永久資料，故不在 ERD 列出；
> 相關事件欄位見最後一節「事件欄位說明」。
>
> **2026-04 v2 重新對齊**：`documents` / `document_chunks` / `copilot_queries` 標 deprecated；
> 新增 `material_lots` / `panel_material_usage` / `panel_station_log` 三表；
> `wafers` 加 `panel_no varchar UNIQUE` 作為對外可讀識別。

---

## 一、PostgreSQL 關聯資料表

### 1. tools（機台 / 站別）
| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| tool_code | varchar | 機台代號（唯一） |
| tool_name | varchar | 顯示名稱 |
| tool_type | varchar | SPI / SMT / REFLOW / AOI / ICT / FQC（PCB profile）/ AOI / PLC / SCADA（semiconductor profile） |
| status | varchar | online / offline / alarm |
| location | varchar | 廠區位置 |
| created_at | timestamptz | |

### 2. recipes（製程配方）
| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| recipe_code | varchar | 配方代號（唯一） |
| recipe_name | varchar | |
| version | varchar | |
| description | text | |
| created_at | timestamptz | |

### 3. lots（批次）
| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| lot_no | varchar | 批次號（唯一，例：LOT-001） |
| product_code | varchar | |
| quantity | int | panel / wafer 數量 |
| start_time | timestamptz | |
| end_time | timestamptz | |
| status | varchar | in_progress / completed / hold |
| created_at | timestamptz | |

### 4. wafers（板 / 晶圓）
> 表名沿用 `wafers` 不變；對外顯示由 domain profile 決定（PCB → 「板」 / Semiconductor → 「晶圓」）。

| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| lot_id | uuid FK → lots | |
| wafer_no | int | 片號 / 板號（同 lot 內唯一） |
| **panel_no** | varchar | **W08 新增**：對外可讀識別（例：`PCB-20240422-LOT-001-1`），全表 UNIQUE，nullable |
| status | varchar | |
| created_at | timestamptz | |

### 5. process_runs（跑機記錄）
| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| tool_id | uuid FK → tools | |
| recipe_id | uuid FK → recipes | |
| lot_id | uuid FK → lots | |
| wafer_id | uuid FK → wafers | |
| run_start_at | timestamptz | |
| run_end_at | timestamptz | |
| temperature | float | °C |
| pressure | float | mTorr |
| yield_rate | float | 0–100 % |
| result_status | varchar | pass / fail / partial |
| created_at | timestamptz | |

### 6. alarms（告警）
| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| tool_id | uuid FK → tools | |
| process_run_id | uuid FK → process_runs | nullable |
| alarm_code | varchar | |
| alarm_level | varchar | info / warning / high / critical |
| message | text | |
| triggered_at | timestamptz | |
| cleared_at | timestamptz | nullable |
| status | varchar | active / cleared |
| source | varchar | kafka / rabbitmq / manual（W04 起 RabbitMQ 來源由 .NET worker 寫入） |

### 7. defects（缺陷）
| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| tool_id | uuid FK → tools | |
| lot_id | uuid FK → lots | |
| wafer_id | uuid FK → wafers | |
| process_run_id | uuid FK → process_runs | nullable |
| defect_code | varchar | |
| defect_type | varchar | |
| severity | varchar | low / medium / high |
| x_coord | float | 座標 |
| y_coord | float | 座標 |
| detected_at | timestamptz | |
| is_false_alarm | bool | |
| kafka_event_id | varchar | 來源 Kafka event id，方便追溯 |

### 8. defect_images（缺陷影像）
| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| defect_id | uuid FK → defects | |
| image_path | text | |
| thumbnail_path | text | |
| width | int | px |
| height | int | px |
| created_at | timestamptz | |

### 9. defect_reviews（人工複判）
| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| defect_id | uuid FK → defects | |
| reviewer | varchar | |
| review_result | varchar | confirmed / false_alarm / escalated |
| review_comment | text | |
| reviewed_at | timestamptz | |

### 10. workorders（工單）
> 來源：RabbitMQ `workorder` queue → .NET `WorkorderRabbitWorker`。

| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| lot_id | uuid FK → lots | nullable |
| workorder_no | varchar | 唯一工單號（例：WO-20260422-1234） |
| priority | varchar | normal / urgent |
| status | varchar | pending / in_progress / done |
| source_queue | varchar | 固定為 'workorder' |
| created_at | timestamptz | |

### 11. material_lots（物料批號，W08 新增）
> 為什麼新增：物料追溯查詢頁需要一張表記錄「錫膏 / FR4 / 電容 / 主晶片 / 助焊劑…」的批號、供應商、入廠時間。

| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| material_lot_no | varchar | 物料批號（唯一） |
| material_type | varchar | solder_paste / fr4 / capacitor / chip / flux ... |
| supplier | varchar | |
| received_at | timestamptz | |
| valid_until | timestamptz | nullable |
| created_at | timestamptz | |

### 12. panel_material_usage（板與物料批號關聯，W08 新增）
> 多對多 join。每張板可能用到多個物料批號；每個物料批號可能跨多張板。

| 欄位 | 類型 | 說明 |
|------|------|------|
| panel_id | uuid FK → wafers | composite PK |
| material_lot_id | uuid FK → material_lots | composite PK |
| quantity | numeric | nullable |
| used_at | timestamptz | 何時被使用 |

### 13. panel_station_log（板的 6 站時間軸，W08 新增）
> 記錄每張板「進站時間 / 出站時間 / 結果」，前端 6 站時間軸資料來源。

| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| panel_id | uuid FK → wafers | |
| station_code | varchar | SPI / SMT / REFLOW / AOI / ICT / FQC |
| entered_at | timestamptz | |
| exited_at | timestamptz | nullable |
| result | varchar | pass / fail / na |
| operator | varchar | nullable |
| created_at | timestamptz | |

### 14~16. documents / document_chunks / copilot_queries（**deprecated**）
> v2 重新對齊時移除 Knowledge Copilot；保留 schema 是因為已寫入的歷史資料還在，但不再有功能讀寫。下一個 schema migration 會 drop。

---

## 二、InfluxDB Measurement（時序資料）

### measurement: tool_metrics（機台即時數值）
| field / tag | 類型 | 說明 |
|-------------|------|------|
| _time | timestamp | InfluxDB 自動欄位 |
| tool_code（tag） | string | 對應 PostgreSQL `tools.tool_code` |
| temperature | float | °C |
| pressure | float | mTorr |
| status | string | online / alarm / offline |
| kafka_offset | int | 來源 Kafka offset，方便 debug |

### measurement: yield_trend（良率趨勢）
| field / tag | 類型 | 說明 |
|-------------|------|------|
| _time | timestamp | |
| lot_no（tag） | string | 對應 PostgreSQL `lots.lot_no` |
| tool_code（tag） | string | |
| yield_rate | float | 0–100 % |
| defect_count | int | 本批次缺陷數 |

---

## 三、關聯圖（PostgreSQL 部分）

```
lots ──────┬──────────────> wafers ───┬──> panel_station_log
           │                  │       │
           │                  │       └──> panel_material_usage <── material_lots
           │                  │
           └──────────────> process_runs <─── tools
                              │   │             │
                              │   └──────────> alarms
                              │
                              └──────────────> defects ──> defect_images
                                                 │
                                                 └──────> defect_reviews

workorders ────> lots（lot_id，nullable）

(deprecated)
documents ────> document_chunks
copilot_queries ──> alarms / defects（nullable）
```

---

## 四、Kafka / RabbitMQ 事件欄位說明

> 這些是訊息 payload 結構，不是 DB 表。

### Kafka topic: aoi.inspection.raw（原始檢測結果）
```json
{
  "event_id": "uuid",
  "tool_code": "AOI-A",
  "lot_no": "LOT-001",
  "wafer_no": 1,
  "timestamp": "ISO-8601",
  "temperature": 180.5,
  "pressure": 120.3,
  "yield_rate": 97.2,
  "defects": []
}
```

消費者：
- Python `kafka-influx-writer` → InfluxDB（`tool_metrics` / `yield_trend`）
- Python `kafka-rabbitmq-publisher` → 判斷後推 RabbitMQ
- **.NET `SpcRealtimeWorker`** → SignalR `/hubs/spc`（即時 SPC 點 + 違規）

### Kafka topic: aoi.defect.event（高嚴重度 defect）
```json
{
  "event_id": "uuid",
  "tool_code": "AOI-B",
  "defect_code": "DEF-0042",
  "severity": "high",
  "timestamp": "ISO-8601"
}
```

消費者：Python `kafka-rabbitmq-publisher` → RabbitMQ exchange → `alert` / `workorder` queue

### RabbitMQ queue: alert
- 消費者：**.NET `AlarmRabbitWorker`** → 寫 `alarms` + 推 SignalR `/hubs/alarm`

### RabbitMQ queue: workorder
- 消費者：**.NET `WorkorderRabbitWorker`** → 寫 `workorders` + 推 SignalR `/hubs/workorder`
