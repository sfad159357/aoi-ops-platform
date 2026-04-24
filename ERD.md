# ERD — AOI Ops Platform

> 本文件分兩部分：
> 1. **PostgreSQL 關聯資料表**（業務核心）
> 2. **InfluxDB Measurement**（時序資料）
>
> Kafka / RabbitMQ 是訊息基礎設施，不存永久資料，故不在 ERD 列出；
> 相關事件欄位見下方「事件欄位說明」。

---

## 一、PostgreSQL 關聯資料表

### 1. tools（機台）
| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| tool_code | varchar | 機台代號（唯一） |
| tool_name | varchar | 顯示名稱 |
| tool_type | varchar | 機台類型（AOI / PLC / SCADA） |
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
| lot_no | varchar | 批次號（唯一） |
| product_code | varchar | |
| quantity | int | 晶圓數量 |
| start_time | timestamptz | |
| end_time | timestamptz | |
| status | varchar | in_progress / completed / hold |
| created_at | timestamptz | |

### 4. wafers（晶圓）
| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| lot_id | uuid FK → lots | |
| wafer_no | int | 片號（同 lot 內唯一） |
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
| alarm_level | varchar | info / warning / critical |
| message | text | |
| triggered_at | timestamptz | |
| cleared_at | timestamptz | nullable |
| status | varchar | active / cleared |
| source | varchar | **新增**：mqtt / kafka / manual（來源識別） |

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
| x_coord | float | 晶圓座標 |
| y_coord | float | 晶圓座標 |
| detected_at | timestamptz | |
| is_false_alarm | bool | |
| kafka_event_id | varchar | **新增**：來自 Kafka topic 的 event id，方便追溯原始訊息 |

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

### 10. workorders（工單，RabbitMQ Queue: workorder 的目標）
> **為什麼新增這張表**：架構加入 RabbitMQ 的 `workorder` queue 後，需要一張 DB 表承接「工單建立」事件，讓後續 Defect Review 流程能關聯對應工單。

| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| lot_id | uuid FK → lots | nullable |
| workorder_no | varchar | 唯一工單號 |
| priority | varchar | normal / urgent |
| status | varchar | pending / in_progress / done |
| source_queue | varchar | 固定為 'workorder'（來自 RabbitMQ） |
| created_at | timestamptz | |

### 11. documents（文件）
| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| title | varchar | |
| doc_type | varchar | sop / recipe / manual |
| version | varchar | |
| source_path | text | |
| uploaded_at | timestamptz | |

### 12. document_chunks（切塊索引）
| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| document_id | uuid FK → documents | |
| chunk_text | text | |
| chunk_index | int | |
| embedding_id | varchar | 向量 DB 的 id（規劃） |
| created_at | timestamptz | |

### 13. copilot_queries（問答記錄）
| 欄位 | 類型 | 說明 |
|------|------|------|
| id | uuid PK | |
| query_text | text | |
| related_alarm_id | uuid | nullable FK → alarms |
| related_defect_id | uuid | nullable FK → defects |
| answer_text | text | |
| source_refs | jsonb | |
| created_at | timestamptz | |

---

## 二、InfluxDB Measurement（時序資料）

> **為什麼要用 InfluxDB**：機台心跳、即時溫度/壓力、良率趨勢等資料「寫入頻率極高但只取最近幾小時」，用關聯式 DB 儲存會產生大量小筆記錄；InfluxDB 做時序優化，查「過去 1 小時的溫度趨勢」只需一行 Flux query。
>
> **誰寫進來**：Kafka Consumer Group A（InfluxDB Writer Worker）從 Kafka topic `aoi.inspection.raw` 消費後寫入。

### measurement: tool_metrics（機台即時數值）
| field / tag | 類型 | 說明 |
|-------------|------|------|
| _time | timestamp | InfluxDB 自動欄位 |
| tool_code（tag）| string | 機台代號，對應 PostgreSQL `tools.tool_code` |
| temperature | float | °C |
| pressure | float | mTorr |
| status | string | online / alarm / offline |
| kafka_offset | int | 來源 Kafka offset，方便 debug |

### measurement: yield_trend（良率趨勢）
| field / tag | 類型 | 說明 |
|-------------|------|------|
| _time | timestamp | |
| lot_no（tag）| string | 對應 PostgreSQL `lots.lot_no` |
| tool_code（tag）| string | |
| yield_rate | float | 0–100 % |
| defect_count | int | 本批次缺陷數 |

---

## 三、關聯圖（PostgreSQL 部分）

```
lots ──────┬──────────────> wafers
           │                  │
           └──────────────> process_runs <─── tools
                              │   │             │
                              │   └──────────> alarms
                              │
                              └──────────────> defects ──> defect_images
                                                 │
                                                 └──────> defect_reviews

workorders ────> lots（lot_id，nullable）

documents ────> document_chunks

copilot_queries ──> alarms（nullable）
copilot_queries ──> defects（nullable）
```

---

## 四、Kafka / RabbitMQ 事件欄位說明（非 ERD，供開發參考）

> 這些是訊息 payload 結構，不是 DB 表；寫在這裡只是讓你在開發 Kafka Consumer 或 RabbitMQ Consumer 時知道「應該從訊息裡讀哪些欄位、對應寫進哪張 DB 表」。

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
→ Consumer Group A（InfluxDB Writer）：寫 `tool_metrics` + `yield_trend`
→ Consumer Group B（RabbitMQ Publisher）：判斷是否為異常事件，推送至 RabbitMQ
→ Consumer Group C（DB Writer）：寫 PostgreSQL `process_runs` + `defects`

### Kafka topic: aoi.defect.event（異常事件）
```json
{
  "event_id": "uuid",
  "tool_code": "AOI-B",
  "defect_code": "DEF-0042",
  "severity": "high",
  "timestamp": "ISO-8601"
}
```
→ Consumer Group B → RabbitMQ exchange → queue: `alert`

### RabbitMQ Queue: alert（高嚴重度告警通知）
- 消費者：告警通知 Worker → 寫 PostgreSQL `alarms`

### RabbitMQ Queue: workorder（工單觸發）
- 消費者：工單 Worker → 寫 PostgreSQL `workorders`
