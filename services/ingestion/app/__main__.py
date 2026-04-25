"""
Ingestion（單一容器：Producer + DB Writer）

功能：
- Producer：定期送出 `aoi.inspection.raw` / `aoi.defect.event`
- Writer：消費 `aoi.inspection.raw` 並寫入 PostgreSQL `process_runs`

為什麼要合併：
- 你希望用「一個容器」就能跑出 SPC Live 所需的真資料（Kafka→DB→SPC）。
- MVP 階段把 ingestion 集中，可大幅降低 compose 啟動步驟與名稱衝突問題。

注意：
- 這是 MVP ingestion（開發用），正式環境建議拆成獨立 producer/consumer 以便水平擴展與獨立重啟。
"""

from __future__ import annotations

import json
import os
import random
import signal
import threading
import time
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone

import psycopg
from kafka import KafkaConsumer, KafkaProducer
from kafka.errors import KafkaTimeoutError, NoBrokersAvailable


def _env(name: str, default: str | None = None) -> str:
    v = os.getenv(name)
    if v is None or v.strip() == "":
        if default is not None:
            return default
        raise RuntimeError(f"缺少環境變數：{name}")
    return v


def _now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def _sleep_with_backoff(*, attempt: int, base: float = 1.0, cap: float = 15.0) -> None:
    """
    為什麼需要：Kafka/DB 在 docker compose 啟動時常常「先起容器、後 ready」。
    - 如果第一次連線失敗就讓 thread 直接崩潰，整個 ingestion 會「看似還活著」但其實不再寫資料。
    - 所以這裡用簡單的 backoff 讓它自動重試，避免你每次都要手動 docker restart。
    """
    delay = min(cap, base * (2**max(0, attempt - 1)))
    time.sleep(delay)


def _new_producer(*, bootstrap: str) -> KafkaProducer:
    """
    為什麼獨立成 function：把 Producer 建立參數集中，方便在連線中斷時重建。
    解決什麼問題：Kafka 在重啟/網路瞬斷後，舊的 producer 可能卡 metadata，不重建就不會恢復送資料。
    """
    return KafkaProducer(
        bootstrap_servers=[bootstrap],
        value_serializer=lambda v: json.dumps(v).encode("utf-8"),
        key_serializer=lambda v: v.encode("utf-8") if isinstance(v, str) else None,
        acks="all",
        linger_ms=20,
    )


def _new_consumer(*, bootstrap: str, topic: str, group_id: str) -> KafkaConsumer:
    """
    為什麼獨立成 function：把 Consumer 建立參數集中，方便在連線中斷時重建。
    解決什麼問題：Kafka 在啟動未 ready 或重啟後，consumer 會拋 NoBrokersAvailable；重試即可恢復。
    """
    return KafkaConsumer(
        topic,
        bootstrap_servers=[bootstrap],
        group_id=group_id,
        enable_auto_commit=True,
        auto_offset_reset="latest",
        value_deserializer=lambda b: json.loads(b.decode("utf-8")),
        key_deserializer=lambda b: b.decode("utf-8") if b else None,
    )


def _normalize_conninfo(conn: str) -> str:
    """
    支援 `.NET ConnectionString` 與 libpq conninfo。
    - .NET: Host=...;Port=...;Database=...;Username=...;Password=...;
    - libpq: host=... port=... dbname=... user=... password=...
    """
    s = conn.strip()
    if ";" not in s:
        lowered = s.lower()
        if "host=" in lowered or "dbname=" in lowered or "user=" in lowered:
            return s

    parts = [p for p in s.split(";") if p.strip()]
    kv: dict[str, str] = {}
    for p in parts:
        if "=" not in p:
            continue
        k, v = p.split("=", 1)
        kv[k.strip().lower()] = v.strip()

    host = kv.get("host") or kv.get("server")
    port = kv.get("port")
    dbname = kv.get("database") or kv.get("dbname")
    user = kv.get("username") or kv.get("user") or kv.get("uid")
    password = kv.get("password") or kv.get("pwd")

    items: list[str] = []
    if host:
        items.append(f"host={host}")
    if port:
        items.append(f"port={port}")
    if dbname:
        items.append(f"dbname={dbname}")
    if user:
        items.append(f"user={user}")
    if password:
        items.append(f"password={password}")
    return " ".join(items) if items else s


def build_inspection_event(*, tool_code: str, lot_no: str, wafer_no: int) -> dict:
    temperature = 180 + random.gauss(0, 1.2)
    pressure = 120 + random.gauss(0, 2.5)
    yield_rate = max(0.0, min(100.0, 97 + random.gauss(0, 0.8)))
    return {
        "event_id": str(uuid.uuid4()),
        "tool_code": tool_code,
        "lot_no": lot_no,
        "wafer_no": wafer_no,
        "timestamp": _now_iso(),
        "temperature": round(temperature, 3),
        "pressure": round(pressure, 3),
        "yield_rate": round(yield_rate, 3),
        "defects": [],
    }


def _get_or_create_tool_id(cur: psycopg.Cursor, tool_code: str) -> str:
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
        (recipe_id, "RCP-AUTO", "Auto Recipe", "v1", "Auto-created for ingestion"),
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


@dataclass(frozen=True)
class InspectionEvent:
    event_id: str
    tool_code: str
    lot_no: str
    wafer_no: str
    timestamp: datetime
    temperature: float | None
    pressure: float | None
    yield_rate: float | None


def _parse_event(raw: dict) -> InspectionEvent:
    ts = raw.get("timestamp")
    if isinstance(ts, str):
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


def _insert_process_run(cur: psycopg.Cursor, ev: InspectionEvent) -> None:
    tool_id = _get_or_create_tool_id(cur, ev.tool_code)
    recipe_id = _get_or_create_recipe_id(cur)
    lot_id = _get_or_create_lot_id(cur, ev.lot_no)
    wafer_id = _get_or_create_wafer_id(cur, lot_id, ev.wafer_no)

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
            str(uuid.uuid4()),
            tool_id,
            recipe_id,
            lot_id,
            wafer_id,
            ev.timestamp,
            ev.timestamp,
            ev.temperature,
            ev.pressure,
            (ev.yield_rate / 100.0 if ev.yield_rate is not None and ev.yield_rate > 1 else ev.yield_rate),
            "pass",
        ),
    )


def producer_loop(stop: threading.Event) -> None:
    bootstrap = _env("KAFKA_BOOTSTRAP_SERVERS", "kafka:9092")
    topic_inspection = _env("KAFKA_TOPIC_INSPECTION", "aoi.inspection.raw")

    tool_codes = ["AOI-A", "AOI-B"]
    lot_nos = ["LOT-001", "LOT-002", "LOT-003"]

    print(f"[ingestion:producer] bootstrap={bootstrap} topic={topic_inspection}")

    producer: KafkaProducer | None = None
    attempt = 0
    sent = 0
    while not stop.is_set():
        try:
            if producer is None:
                producer = _new_producer(bootstrap=bootstrap)
                attempt = 0

            tool = random.choice(tool_codes)
            lot = random.choice(lot_nos)
            wafer = random.randint(1, 25)
            evt = build_inspection_event(tool_code=tool, lot_no=lot, wafer_no=wafer)
            producer.send(topic_inspection, key=tool, value=evt)
            producer.flush(timeout=2)
            # 為什麼要打點：用極低頻率印出 sent 計數，快速確認 producer 真的在送資料（不增加太多 log 壓力）。
            sent += 1
            if sent % 10 == 0:
                print(f"[ingestion:producer] sent={sent} last_tool={tool} last_lot={lot}")
            time.sleep(1.0)
        except (NoBrokersAvailable, KafkaTimeoutError) as e:
            # Kafka 還沒 ready / broker 重啟：丟棄舊 producer 並重建（否則會卡 metadata）
            print(f"[ingestion:producer] Kafka not ready, retrying: {e}")
            attempt += 1
            producer = None
            _sleep_with_backoff(attempt=attempt)
        except Exception as e:
            # 任何非預期錯誤也不要讓 thread 死掉，避免「容器活著但不產資料」
            print(f"[ingestion:producer] ERROR: {e}")
            attempt += 1
            producer = None
            _sleep_with_backoff(attempt=attempt)


def writer_loop(stop: threading.Event) -> None:
    bootstrap = _env("KAFKA_BOOTSTRAP_SERVERS", "kafka:9092")
    topic = _env("KAFKA_TOPIC_INSPECTION", "aoi.inspection.raw")
    group_id = _env("KAFKA_CONSUMER_GROUP", "aoiops-ingestion-writer")
    db_conn = _normalize_conninfo(_env("DB_CONNECTION"))

    print(f"[ingestion:writer] bootstrap={bootstrap} topic={topic} group={group_id}")

    consumer: KafkaConsumer | None = None
    attempt = 0
    conn: psycopg.Connection | None = None
    inserted = 0

    while not stop.is_set():
        try:
            if consumer is None:
                consumer = _new_consumer(bootstrap=bootstrap, topic=topic, group_id=group_id)
                attempt = 0
            if conn is None or conn.closed:
                # 為什麼要重連：DB container 重啟時，舊連線會變成 broken pipe
                conn = psycopg.connect(db_conn)

            records = consumer.poll(timeout_ms=1000, max_records=50)
            if not records:
                continue

            try:
                with conn.cursor() as cur:
                    for _tp, msgs in records.items():
                        for m in msgs:
                            ev = _parse_event(m.value)
                            _insert_process_run(cur, ev)
                            inserted += 1
                conn.commit()
                # 為什麼要打點：確認 consumer→DB writer 真的有落地（否則你只會看到資料一直是 1 筆）。
                if inserted % 10 == 0:
                    print(f"[ingestion:writer] inserted={inserted}")
            except Exception as e:
                conn.rollback()
                print(f"[ingestion:writer] DB ERROR: {e}")
                time.sleep(1.0)
        except (NoBrokersAvailable, KafkaTimeoutError) as e:
            print(f"[ingestion:writer] Kafka not ready, retrying: {e}")
            attempt += 1
            consumer = None
            _sleep_with_backoff(attempt=attempt)
        except psycopg.OperationalError as e:
            print(f"[ingestion:writer] DB not ready, retrying: {e}")
            attempt += 1
            conn = None
            _sleep_with_backoff(attempt=attempt)
        except Exception as e:
            print(f"[ingestion:writer] ERROR: {e}")
            attempt += 1
            consumer = None
            conn = None
            _sleep_with_backoff(attempt=attempt)

    if conn is not None and not conn.closed:
        conn.close()


def main() -> None:
    stop = threading.Event()

    def _handle_sig(_sig, _frame):
        stop.set()

    signal.signal(signal.SIGINT, _handle_sig)
    signal.signal(signal.SIGTERM, _handle_sig)

    t1 = threading.Thread(target=producer_loop, args=(stop,), daemon=True)
    t2 = threading.Thread(target=writer_loop, args=(stop,), daemon=True)
    t1.start()
    t2.start()

    while not stop.is_set():
        time.sleep(0.5)


if __name__ == "__main__":
    main()

