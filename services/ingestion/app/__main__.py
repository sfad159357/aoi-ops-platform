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
import pytds
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


@dataclass(frozen=True)
class _SqlServerConn:
    host: str
    port: int
    database: str
    user: str
    password: str


def _db_provider() -> str:
    """
    取得 DB provider。

    為什麼要有這個判斷：
    - 本專案第一階段用 PostgreSQL（psycopg）；第二階段要支援 SQL Server（Azure SQL Edge）。
    - 透過 env `DB_PROVIDER=postgres|sqlserver` 讓 docker-compose 不改程式碼就能切換。
    """
    return _env("DB_PROVIDER", "postgres").strip().lower()


def _parse_sqlserver_conn_str(conn: str) -> _SqlServerConn:
    """
    解析 SQL Server 的 .NET ConnectionString。

    支援格式：
    - Server=mssql,1433;Database=AOIOpsPlatform_MSSQL;User Id=sa;Password=...;TrustServerCertificate=True;

    為什麼要自己 parse：
    - 我們選用 pytds（純 Python），避免在 linux/arm64 內裝 ODBC driver；
    - 但 pytds 需要 host/port/database/user/password 拆開。
    """
    s = conn.strip().rstrip(";")
    kv: dict[str, str] = {}
    for p in [x for x in s.split(";") if x.strip()]:
        if "=" not in p:
            continue
        k, v = p.split("=", 1)
        kv[k.strip().lower()] = v.strip()

    server = kv.get("server") or kv.get("data source") or kv.get("addr") or kv.get("address") or kv.get("network address")
    if not server:
        raise RuntimeError("SQL Server connection string 缺少 Server=... 欄位")

    host = server
    port = 1433
    if "," in server:
        host, port_s = server.split(",", 1)
        host = host.strip()
        port = int(port_s.strip())

    database = kv.get("database") or kv.get("initial catalog") or kv.get("dbname") or kv.get("db")
    user = kv.get("user id") or kv.get("uid") or kv.get("username") or kv.get("user")
    password = kv.get("password") or kv.get("pwd")

    if not database:
        raise RuntimeError("SQL Server connection string 缺少 Database=... 欄位")
    if not user or not password:
        raise RuntimeError("SQL Server connection string 缺少 User Id / Password 欄位")

    return _SqlServerConn(host=host, port=port, database=database, user=user, password=password)


def _sql_now(provider: str) -> str:
    # 為什麼抽出來：now() / SYSUTCDATETIME() 是 DB 方言差異，集中管理避免散落。
    return "SYSUTCDATETIME()" if provider in ("sqlserver", "mssql") else "now()"


def build_inspection_event(*, tool_code: str, lot_no: str, wafer_no: int) -> dict:
    """
    建立 inspection 事件（aoi.inspection.raw）。

    為什麼要在 ingestion 內實作情境規則：
    - W03 的目標是「端到端資料流可以 demo」：Kafka→DB/Influx→SPC，以及 Kafka→RabbitMQ→DB。
    - 事件源如果只有完全隨機的白噪音，控制圖很難展示「漂移/異常/誤判」。
    - 所以這裡用可切換的 scenario 來注入可預期的型態（方便你驗收與 demo）。

    解決什麼問題：
    - 讓同一套部署在「不連真機」時，也能重現常見製程問題：漂移（drift）、突波（spike）、誤判（misjudge）。
    """

    scenario = _env("SIM_SCENARIO", "normal").strip().lower()
    # 為什麼用 step counter：漂移/鋸齒這類情境需要「跨事件的狀態」，才能形成連續趨勢。
    step = int(_env("SIM_STEP", "0"))

    base_temp = 180.0
    base_pressure = 120.0
    base_yield = 97.0

    # ─── 正常情境：小幅隨機波動 ─────────────────────────────────────────
    temperature = base_temp + random.gauss(0, 1.2)
    pressure = base_pressure + random.gauss(0, 2.5)
    yield_rate = base_yield + random.gauss(0, 0.8)

    # ─── 漂移（drift）：平均值逐步偏移，方便 SPC 顯示趨勢違規 ───────────
    if scenario == "drift":
        # 每 1 點讓溫度 +0.05，壓力 +0.08，良率 -0.02（可用來 demo trend）
        temperature += step * 0.05
        pressure += step * 0.08
        yield_rate -= step * 0.02

    # ─── 突波（spike）：偶發超出 3σ 的點，方便 demo rule 1（超出 UCL/LCL） ─
    if scenario == "spike":
        if step % 25 == 0 and step > 0:
            temperature += random.choice([8.0, -8.0])
            pressure += random.choice([15.0, -15.0])
            yield_rate += random.choice([3.5, -3.5])

    # ─── 誤判（misjudge）：量測噪音變大（系統誤判/感測器不穩） ───────────
    if scenario == "misjudge":
        temperature += random.gauss(0, 3.5)
        pressure += random.gauss(0, 6.0)
        yield_rate += random.gauss(0, 2.0)

    yield_rate = max(0.0, min(100.0, yield_rate))
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


def build_defect_event(*, tool_code: str, lot_no: str, severity: str) -> dict:
    """
    建立 defect 事件（aoi.defect.event）。

    為什麼要有 defect event：
    - W03 需要示範 Kafka→RabbitMQ 的業務路由（alert/workorder）。
    - 我們用 severity 來模擬「告警」與「開工單」的優先級。
    """

    return {
        "event_id": str(uuid.uuid4()),
        "tool_code": tool_code,
        "lot_no": lot_no,
        "defect_code": f"DEF-{random.randint(1, 9999):04d}",
        "severity": severity,
        "timestamp": _now_iso(),
    }


def _get_or_create_tool_id(cur, tool_code: str, *, provider: str) -> str:
    cur.execute("SELECT id FROM tools WHERE tool_code = %s", (tool_code,))
    row = cur.fetchone()
    if row:
        return str(row[0])
    tool_id = str(uuid.uuid4())
    cur.execute(
        f"""
        INSERT INTO tools (id, tool_code, tool_name, tool_type, status, location, created_at)
        VALUES (%s, %s, %s, %s, %s, %s, {_sql_now(provider)})
        """,
        (tool_id, tool_code, f"{tool_code} (auto)", "AOI", "online", "FAB-1"),
    )
    return tool_id


def _get_or_create_recipe_id(cur, *, provider: str) -> str:
    # LIMIT 在 SQL Server 不存在，改用 TOP 1
    if provider in ("sqlserver", "mssql"):
        cur.execute("SELECT TOP 1 id FROM recipes ORDER BY created_at")
    else:
        cur.execute("SELECT id FROM recipes ORDER BY created_at LIMIT 1")
    row = cur.fetchone()
    if row:
        return str(row[0])
    recipe_id = str(uuid.uuid4())
    cur.execute(
        f"""
        INSERT INTO recipes (id, recipe_code, recipe_name, version, description, created_at)
        VALUES (%s, %s, %s, %s, %s, {_sql_now(provider)})
        """,
        (recipe_id, "RCP-AUTO", "Auto Recipe", "v1", "Auto-created for ingestion"),
    )
    return recipe_id


def _get_or_create_lot_id(cur, lot_no: str, *, provider: str) -> str:
    cur.execute("SELECT id FROM lots WHERE lot_no = %s", (lot_no,))
    row = cur.fetchone()
    if row:
        return str(row[0])
    lot_id = str(uuid.uuid4())
    cur.execute(
        f"""
        INSERT INTO lots (id, lot_no, product_code, quantity, start_time, end_time, status, created_at)
        VALUES (%s, %s, %s, %s, {_sql_now(provider)}, NULL, %s, {_sql_now(provider)})
        """,
        (lot_id, lot_no, "PROD-A", 25, "in_progress"),
    )
    return lot_id


def _get_or_create_wafer_id(cur, lot_id: str, lot_no: str, wafer_no: str, *, provider: str) -> str:
    cur.execute("SELECT id FROM wafers WHERE lot_id = %s AND wafer_no = %s", (lot_id, wafer_no))
    row = cur.fetchone()
    if row:
        wafer_id = str(row[0])
        # 為什麼在「已存在」時也補 panel_no：
        # - 實務上 lot/wafer 可能先被寫入（早期版本沒有 panel_no），
        #   之後才升級 schema / 程式；
        # - 若只在 INSERT 時寫 panel_no，舊資料會永遠是 NULL，追溯頁的 recent list 仍會是空的。
        panel_no = f"{lot_no}-{wafer_no}"
        cur.execute(
            """
            UPDATE wafers
            SET panel_no = %s
            WHERE id = %s AND panel_no IS NULL
            """,
            (panel_no, wafer_id),
        )
        return wafer_id
    wafer_id = str(uuid.uuid4())
    # 為什麼要補 panel_no：
    # - Traceability 的 RecentPanels endpoint 會用 `WHERE panel_no IS NOT NULL` 當作「可追溯的板」條件；
    # - 先前 ingestion 只寫 wafer_no，沒有寫 panel_no，導致追溯頁「最近板」永遠是空的，看起來像沒預設資料。
    # - 這裡用 lot_no + wafer_no 組一個可讀的板號，兼顧 demo 與真實情境（板號通常可由批次+序號推得）。
    panel_no = f"{lot_no}-{wafer_no}"
    cur.execute(
        """
        INSERT INTO wafers (id, lot_id, wafer_no, panel_no, status, created_at)
        VALUES (%s, %s, %s, %s, %s, %s)
        """,
        (wafer_id, lot_id, wafer_no, panel_no, "in_progress", datetime.now(timezone.utc)),
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


def _insert_process_run(cur, ev: InspectionEvent, *, provider: str) -> None:
    tool_id = _get_or_create_tool_id(cur, ev.tool_code, provider=provider)
    recipe_id = _get_or_create_recipe_id(cur, provider=provider)
    lot_id = _get_or_create_lot_id(cur, ev.lot_no, provider=provider)
    wafer_id = _get_or_create_wafer_id(cur, lot_id, ev.lot_no, ev.wafer_no, provider=provider)

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
            %s, %s
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
            datetime.now(timezone.utc),
        ),
    )


def producer_loop(stop: threading.Event) -> None:
    bootstrap = _env("KAFKA_BOOTSTRAP_SERVERS", "kafka:9092")
    topic_inspection = _env("KAFKA_TOPIC_INSPECTION", "aoi.inspection.raw")
    topic_defect = _env("KAFKA_TOPIC_DEFECT", "aoi.defect.event")

    tool_codes = ["AOI-A", "AOI-B"]
    lot_nos = ["LOT-001", "LOT-002", "LOT-003"]

    print(f"[ingestion:producer] bootstrap={bootstrap} topic={topic_inspection} defect_topic={topic_defect}")

    producer: KafkaProducer | None = None
    attempt = 0
    sent = 0
    step = 0
    while not stop.is_set():
        try:
            if producer is None:
                producer = _new_producer(bootstrap=bootstrap)
                attempt = 0

            tool = random.choice(tool_codes)
            lot = random.choice(lot_nos)
            wafer = random.randint(1, 25)
            # 用 env 帶出 step（方便你在 docker logs 看到目前情境進度；也方便未來接成 API/控制台）
            os.environ["SIM_STEP"] = str(step)
            evt = build_inspection_event(tool_code=tool, lot_no=lot, wafer_no=wafer)
            producer.send(topic_inspection, key=tool, value=evt)

            # 每 20 筆注入一次 defect event（用來 demo Kafka→RabbitMQ 的 alert/workorder 路由）
            if step % 20 == 0 and step > 0:
                sev = random.choices(["low", "medium", "high"], weights=[0.6, 0.3, 0.1], k=1)[0]
                defect = build_defect_event(tool_code=tool, lot_no=lot, severity=sev)
                producer.send(topic_defect, key=tool, value=defect)

            producer.flush(timeout=2)
            # 為什麼要打點：用極低頻率印出 sent 計數，快速確認 producer 真的在送資料（不增加太多 log 壓力）。
            sent += 1
            if sent % 10 == 0:
                print(f"[ingestion:producer] sent={sent} last_tool={tool} last_lot={lot}")
            step += 1
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
    provider = _db_provider()
    raw_conn = _env("DB_CONNECTION")
    db_conn = _normalize_conninfo(raw_conn)
    mssql_conn = _parse_sqlserver_conn_str(raw_conn) if provider in ("sqlserver", "mssql") else None

    print(f"[ingestion:writer] bootstrap={bootstrap} topic={topic} group={group_id}")

    consumer: KafkaConsumer | None = None
    attempt = 0
    conn: psycopg.Connection | None = None
    mssql: pytds.Connection | None = None
    inserted = 0

    while not stop.is_set():
        try:
            if consumer is None:
                consumer = _new_consumer(bootstrap=bootstrap, topic=topic, group_id=group_id)
                attempt = 0
            if provider in ("sqlserver", "mssql"):
                if mssql is None:
                    # 為什麼要重連：DB container 重啟時，舊連線會中斷；pytds 也需要重新建立 connection
                    assert mssql_conn is not None
                    mssql = pytds.connect(
                        mssql_conn.host,
                        database=mssql_conn.database,
                        user=mssql_conn.user,
                        password=mssql_conn.password,
                        port=mssql_conn.port,
                        autocommit=False,
                    )
            else:
                if conn is None or conn.closed:
                    # 為什麼要重連：DB container 重啟時，舊連線會變成 broken pipe
                    conn = psycopg.connect(db_conn)

            records = consumer.poll(timeout_ms=1000, max_records=50)
            if not records:
                continue

            try:
                if provider in ("sqlserver", "mssql"):
                    assert mssql is not None
                    cur = mssql.cursor()
                    for _tp, msgs in records.items():
                        for m in msgs:
                            ev = _parse_event(m.value)
                            _insert_process_run(cur, ev, provider=provider)
                            inserted += 1
                    mssql.commit()
                else:
                    assert conn is not None
                    with conn.cursor() as cur:
                        for _tp, msgs in records.items():
                            for m in msgs:
                                ev = _parse_event(m.value)
                                _insert_process_run(cur, ev, provider=provider)
                                inserted += 1
                    conn.commit()
                # 為什麼要打點：確認 consumer→DB writer 真的有落地（否則你只會看到資料一直是 1 筆）。
                if inserted % 10 == 0:
                    print(f"[ingestion:writer] inserted={inserted}")
            except Exception as e:
                if provider in ("sqlserver", "mssql"):
                    if mssql is not None:
                        mssql.rollback()
                else:
                    if conn is not None:
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
        except pytds.OperationalError as e:
            print(f"[ingestion:writer] DB not ready, retrying: {e}")
            attempt += 1
            mssql = None
            _sleep_with_backoff(attempt=attempt)
        except Exception as e:
            print(f"[ingestion:writer] ERROR: {e}")
            attempt += 1
            consumer = None
            conn = None
            mssql = None
            _sleep_with_backoff(attempt=attempt)

    if conn is not None and not conn.closed:
        conn.close()
    if mssql is not None:
        mssql.close()


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

