"""
Kafka Consumer Group C — DB Writer

目標：
- 從 Kafka topic `aoi.inspection.raw` 消費設備事件
- 落地到 PostgreSQL：`process_runs`（並確保 tools/lots/wafers/recipes 存在）

為什麼要在這裡落地：
- 你希望資料來源是設備事件（Kafka/RabbitMQ），而不是寫死 demo 資料。
- SPC Live 需要可查詢的時間序列；先落 DB 可以確保順序、可追溯、可重算。

解決什麼問題：
- 把「事件流」轉成「查詢友善」的關聯資料，供 .NET API / SPC Service / 前端 Dashboard 使用。

注意（MVP 取捨）：
- 這版先只寫 `process_runs`（temperature/pressure/yield_rate），讓 SPC Live 有真資料可畫。
- defects / alarms / workorders 的完整業務路由可在下一步補齊（仍然不需要 MQTT）。
"""

from __future__ import annotations

import json
import os
import time
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone

import psycopg
from kafka import KafkaConsumer


def _env(name: str, default: str | None = None) -> str:
    v = os.getenv(name)
    if v is None or v.strip() == "":
        if default is not None:
            return default
        raise RuntimeError(f"缺少環境變數：{name}")
    return v


@dataclass(frozen=True)
class InspectionEvent:
    """aoi.inspection.raw 事件（只保留落地需要的欄位）"""

    event_id: str
    tool_code: str
    lot_no: str
    wafer_no: str
    timestamp: datetime
    temperature: float | None
    pressure: float | None
    yield_rate: float | None


def _parse_event(raw: dict) -> InspectionEvent:
    """
    把 Kafka JSON payload 解析成 InspectionEvent。

    為什麼要顯式解析：
    - DB 欄位型別需要一致（尤其 timestamp 與數值欄位），不然落地會一直踩型別錯。
    """

    ts = raw.get("timestamp")
    if isinstance(ts, str):
        # ISO-8601
        timestamp = datetime.fromisoformat(ts.replace("Z", "+00:00"))
    else:
        timestamp = datetime.now(timezone.utc)

    return InspectionEvent(
        event_id=str(raw.get("event_id") or uuid.uuid4()),
        tool_code=str(raw.get("tool_code") or "AOI-A"),
        lot_no=str(raw.get("lot_no") or "LOT-001"),
        wafer_no=str(raw.get("wafer_no") or "1"),
        timestamp=timestamp,
        temperature=(float(raw["temperature"]) if raw.get("temperature") is not None else None),
        pressure=(float(raw["pressure"]) if raw.get("pressure") is not None else None),
        yield_rate=(float(raw["yield_rate"]) if raw.get("yield_rate") is not None else None),
    )


def _get_or_create_tool_id(cur: psycopg.Cursor, tool_code: str) -> str:
    """
    取得或建立 tools.id（以 tool_code 為唯一鍵）。

    為什麼要 upsert：
    - data-simulator 可能送出新的 tool_code；若 DB 沒 seed，直接寫 process_runs 會 FK 失敗。
    """

    cur.execute("SELECT id FROM tools WHERE tool_code = %s", (tool_code,))
    row = cur.fetchone()
    if row:
        return str(row[0])

    tool_id = str(uuid.uuid4())
    cur.execute(
        """
        INSERT INTO tools (id, tool_code, tool_name, tool_type, status, location, created_at)
        VALUES (%s, %s, %s, %s, %s, %s, now())
        """,
        (tool_id, tool_code, f"{tool_code} (auto)", "AOI", "online", "FAB-1"),
    )
    return tool_id


def _get_or_create_recipe_id(cur: psycopg.Cursor) -> str:
    """
    取得一個 recipe id（MVP：若不存在就自動建立 baseline）。

    為什麼先用單一 recipe：
    - SPC demo 目前重點在設備數據 → process_runs → SPC 圖表；recipe 分流後面再加。
    """

    cur.execute("SELECT id FROM recipes ORDER BY created_at LIMIT 1")
    row = cur.fetchone()
    if row:
        return str(row[0])

    recipe_id = str(uuid.uuid4())
    cur.execute(
        """
        INSERT INTO recipes (id, recipe_code, recipe_name, version, description, created_at)
        VALUES (%s, %s, %s, %s, %s, now())
        """,
        (recipe_id, "RCP-AUTO", "Auto Recipe", "v1", "Auto-created for simulator"),
    )
    return recipe_id


def _get_or_create_lot_id(cur: psycopg.Cursor, lot_no: str) -> str:
    cur.execute("SELECT id FROM lots WHERE lot_no = %s", (lot_no,))
    row = cur.fetchone()
    if row:
        return str(row[0])

    lot_id = str(uuid.uuid4())
    cur.execute(
        """
        INSERT INTO lots (id, lot_no, product_code, quantity, start_time, end_time, status, created_at)
        VALUES (%s, %s, %s, %s, now(), NULL, %s, now())
        """,
        (lot_id, lot_no, "PROD-A", 25, "in_progress"),
    )
    return lot_id


def _get_or_create_wafer_id(cur: psycopg.Cursor, lot_id: str, wafer_no: str) -> str:
    cur.execute("SELECT id FROM wafers WHERE lot_id = %s AND wafer_no = %s", (lot_id, wafer_no))
    row = cur.fetchone()
    if row:
        return str(row[0])

    wafer_id = str(uuid.uuid4())
    cur.execute(
        """
        INSERT INTO wafers (id, lot_id, wafer_no, status, created_at)
        VALUES (%s, %s, %s, %s, now())
        """,
        (wafer_id, lot_id, wafer_no, "in_progress"),
    )
    return wafer_id


def _insert_process_run(cur: psycopg.Cursor, ev: InspectionEvent) -> None:
    """
    寫入 process_runs。

    對齊 EF Core schema（AoiOpsDbContext）：
    - temperature/pressure/yield_rate 在 DB 是 decimal，所以這裡用 float 也會被 postgres 轉型成功。
    """

    tool_id = _get_or_create_tool_id(cur, ev.tool_code)
    recipe_id = _get_or_create_recipe_id(cur)
    lot_id = _get_or_create_lot_id(cur, ev.lot_no)
    wafer_id = _get_or_create_wafer_id(cur, lot_id, ev.wafer_no)

    run_id = str(uuid.uuid4())
    cur.execute(
        """
        INSERT INTO process_runs (
            id, tool_id, recipe_id, lot_id, wafer_id,
            run_start_at, run_end_at,
            temperature, pressure, yield_rate,
            result_status, created_at
        )
        VALUES (
            %s, %s, %s, %s, %s,
            %s, %s,
            %s, %s, %s,
            %s, now()
        )
        """,
        (
            run_id,
            tool_id,
            recipe_id,
            lot_id,
            wafer_id,
            ev.timestamp,
            ev.timestamp,  # MVP：用同一個時間（後續可改成 run_start/run_end）
            ev.temperature,
            ev.pressure,
            (ev.yield_rate / 100.0 if ev.yield_rate is not None and ev.yield_rate > 1 else ev.yield_rate),
            "pass",
        ),
    )


def main() -> None:
    bootstrap = _env("KAFKA_BOOTSTRAP_SERVERS", "kafka:9092")
    topic = _env("KAFKA_TOPIC_INSPECTION", "aoi.inspection.raw")
    group_id = _env("KAFKA_CONSUMER_GROUP", "aoiops-db-writer")
    db_conn = _env("DB_CONNECTION")

    consumer = KafkaConsumer(
        topic,
        bootstrap_servers=[bootstrap],
        group_id=group_id,
        enable_auto_commit=True,  # MVP：先求能跑；正式可改成手動 commit
        auto_offset_reset="latest",
        value_deserializer=lambda b: json.loads(b.decode("utf-8")),
        key_deserializer=lambda b: b.decode("utf-8") if b else None,
    )

    print(f"[db-writer] bootstrap={bootstrap} topic={topic} group={group_id}")

    # 長連線（重用 connection 比每筆重連快很多）
    with psycopg.connect(db_conn) as conn:
        while True:
            # poll 一批訊息
            records = consumer.poll(timeout_ms=1000, max_records=50)
            if not records:
                continue

            try:
                with conn.cursor() as cur:
                    for _tp, msgs in records.items():
                        for m in msgs:
                            ev = _parse_event(m.value)
                            _insert_process_run(cur, ev)
                conn.commit()
            except Exception as e:
                conn.rollback()
                # MVP：先印出錯誤，避免 silent failure
                print(f"[db-writer] ERROR: {e}")
                time.sleep(1.0)


if __name__ == "__main__":
    main()

