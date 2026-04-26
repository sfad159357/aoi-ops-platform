"""
RabbitMQ Consumers — DB Sink

功能：
- 監聽 RabbitMQ queue：`alert` 與 `workorder`
- 將訊息落地到 PostgreSQL：
  - `alarms`（告警）
  - `workorders`（工單）

注意（MVP 取捨）：
- 這裡先用「至少可查詢」的欄位落地；更完整的狀態機（ack/close/assign）後續再加。
"""

from __future__ import annotations

import json
import os
import time
import uuid
from datetime import datetime, timezone

import pika
import psycopg


def _env(name: str, default: str | None = None) -> str:
    v = os.getenv(name)
    if v is None or v.strip() == "":
        if default is not None:
            return default
        raise RuntimeError(f"缺少環境變數：{name}")
    return v


def _normalize_conninfo(conn: str) -> str:
    """
    為什麼需要這段：compose/.NET 常用 `Host=...;Port=...;Database=...;Username=...;Password=...;`
    psycopg 需要 libpq conninfo（小寫 key），所以這裡做正規化避免踩 `invalid connection option "Host"`。
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


def _parse_ts(ts: str | None) -> datetime:
    if not ts:
        return datetime.now(timezone.utc)
    try:
        return datetime.fromisoformat(ts.replace("Z", "+00:00"))
    except Exception:
        return datetime.now(timezone.utc)


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


def _find_lot_id(cur: psycopg.Cursor, lot_no: str | None) -> str | None:
    if not lot_no:
        return None
    cur.execute("SELECT id FROM lots WHERE lot_no = %s", (lot_no,))
    row = cur.fetchone()
    return str(row[0]) if row else None


def _insert_alarm(cur: psycopg.Cursor, ev: dict) -> None:
    tool_code = str(ev.get("tool_code") or "AOI-A")
    tool_id = _get_or_create_tool_id(cur, tool_code)

    alarm_id = str(uuid.uuid4())
    alarm_code = str(ev.get("defect_code") or "DEF-0000")
    severity = str(ev.get("severity") or "low").lower()
    ts = _parse_ts(ev.get("timestamp"))
    msg = f"defect_event severity={severity}"

    cur.execute(
        """
        INSERT INTO alarms (
            id, tool_id, process_run_id,
            alarm_code, alarm_level, message,
            triggered_at, cleared_at, status, source
        )
        VALUES (
            %s, %s, NULL,
            %s, %s, %s,
            %s, NULL, %s, %s
        )
        """,
        (
            alarm_id,
            tool_id,
            alarm_code,
            severity,
            msg,
            ts,
            "active",
            "rabbitmq",
        ),
    )


def _insert_workorder(cur: psycopg.Cursor, ev: dict) -> None:
    lot_no = ev.get("lot_no")
    lot_id = _find_lot_id(cur, str(lot_no)) if lot_no else None

    wo_id = str(uuid.uuid4())
    severity = str(ev.get("severity") or "low").lower()
    priority = "P1" if severity in ("high", "critical") else ("P2" if severity == "medium" else "P3")

    # 為什麼 workorder_no 不是自增：事件流系統常需要可追溯的可讀識別碼；MVP 用 timestamp+random。
    workorder_no = f"WO-{datetime.now(timezone.utc).strftime('%Y%m%d%H%M%S')}-{str(wo_id)[:8]}"

    cur.execute(
        """
        INSERT INTO workorders (
            id, lot_id, workorder_no, priority, status, source_queue, created_at
        )
        VALUES (
            %s, %s, %s, %s, %s, %s, now()
        )
        """,
        (
            wo_id,
            lot_id,
            workorder_no,
            priority,
            "open",
            "workorder",
        ),
    )


def main() -> None:
    amqp_url = _env("RABBITMQ_URL", "amqp://guest:guest@rabbitmq:5672/")
    queue_alert = _env("RABBITMQ_QUEUE_ALERT", "alert")
    queue_workorder = _env("RABBITMQ_QUEUE_WORKORDER", "workorder")

    db_conn = _normalize_conninfo(_env("DB_CONNECTION"))

    print(f"[rabbitmq-db-sink] amqp_url={amqp_url}")
    print(f"[rabbitmq-db-sink] queues: alert={queue_alert} workorder={queue_workorder}")

    params = pika.URLParameters(amqp_url)
    conn = pika.BlockingConnection(params)
    ch = conn.channel()
    ch.queue_declare(queue=queue_alert, durable=True)
    ch.queue_declare(queue=queue_workorder, durable=True)

    # 為什麼 prefetch：避免一次拉太多訊息進記憶體；同時提升公平分配（若未來 scale out）
    ch.basic_qos(prefetch_count=50)

    db = psycopg.connect(db_conn)

    def _handle(queue_name: str):
        def _cb(_ch, method, _props, body: bytes):
            try:
                ev = json.loads(body.decode("utf-8"))
                with db.cursor() as cur:
                    if queue_name == queue_alert:
                        _insert_alarm(cur, ev)
                    else:
                        _insert_workorder(cur, ev)
                db.commit()
                _ch.basic_ack(delivery_tag=method.delivery_tag)
            except Exception as e:
                db.rollback()
                print(f"[rabbitmq-db-sink] ERROR queue={queue_name}: {e}")
                # MVP：不直接丟棄，稍微等一下再 nack requeue（避免一直高速失敗刷 log）
                time.sleep(1.0)
                _ch.basic_nack(delivery_tag=method.delivery_tag, requeue=True)

        return _cb

    ch.basic_consume(queue=queue_alert, on_message_callback=_handle(queue_alert), auto_ack=False)
    ch.basic_consume(queue=queue_workorder, on_message_callback=_handle(queue_workorder), auto_ack=False)

    print("[rabbitmq-db-sink] consuming...")
    ch.start_consuming()


if __name__ == "__main__":
    main()

