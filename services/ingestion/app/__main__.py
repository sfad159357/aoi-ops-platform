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


# ─── tool / station / line / operator mapping ───────────────────────────
#
# 為什麼把這幾組 mapping 寫死在 ingestion：
# - 真實 MES 來源端就是設備站台，會在「機台 → 站別 → 產線」這個路徑上做硬綁定（一台 SPI 不會跑 AOI 程式）。
# - Kafka payload 在「源頭」就要把 line_code / station_code / operator 填齊，
#   下游 SignalR 推給前端，alarms/workorders/defects/spc_measurements 才能直接渲染欄位、不再回頭推測。
# - 這是 demo / 面試示範取向，未來可換成 master config 載入；目前保留可讀字典最直觀。
TOOL_TO_LINE: dict[str, str] = {
    # SMT-A 線（完整 6 站）
    "SPI-A01": "SMT-A",
    "SMT-A01": "SMT-A",
    "REFLOW-A01": "SMT-A",
    "AOI-A01": "SMT-A",
    "ICT-A01": "SMT-A",
    "FQC-A01": "SMT-A",
    # SMT-B 線（部份站）
    "SMT-B01": "SMT-B",
    "AOI-B01": "SMT-B",
    # 沿用舊 demo 名稱，避免歷史 Kafka 訊息找不到 mapping
    "AOI-A": "SMT-A",
    "AOI-B": "SMT-B",
}

TOOL_TO_STATION: dict[str, str] = {
    "SPI-A01": "SPI",
    "SMT-A01": "SMT",
    "REFLOW-A01": "REFLOW",
    "AOI-A01": "AOI",
    "ICT-A01": "ICT",
    "FQC-A01": "FQC",
    "SMT-B01": "SMT",
    "AOI-B01": "AOI",
    "AOI-A": "AOI",
    "AOI-B": "AOI",
}

# 員工池：(operator_code, operator_name, role, shift)
# 為什麼保留 shift 欄位：
# - 真實工廠值班是「3 班 8 小時」，SPC/異常記錄要能依班別歸責；
#   這裡用簡化的「依小時切 3 班」決定可選名單。
OPERATORS: list[tuple[str, str, str, str]] = [
    ("OP-001", "王小明", "operator", "A"),
    ("OP-002", "李大華", "operator", "A"),
    ("OP-003", "張美玲", "operator", "B"),
    ("OP-004", "林志偉", "operator", "B"),
    ("OP-005", "陳怡君", "operator", "C"),
    ("OP-006", "黃文豪", "operator", "C"),
    ("LEADER-A", "周建華", "leader", "A"),
    ("LEADER-B", "吳秀英", "leader", "B"),
    ("LEADER-C", "韓立群", "leader", "C"),
    ("ENG-001", "趙俊傑", "engineer", "A"),
    ("QC-001", "蔡佳玲", "qc", "A"),
]

DEFECT_TYPES: list[str] = [
    "短路",
    "斷路",
    "錫橋",
    "空焊",
    "偏移",
    "缺件",
    "異物",
    "極性反向",
]


def _current_shift() -> str:
    """根據 UTC+8 小時切 3 班（A:08-16 / B:16-24 / C:00-08）。"""
    h = (datetime.now(timezone.utc).hour + 8) % 24
    if 8 <= h < 16:
        return "A"
    if 16 <= h < 24:
        return "B"
    return "C"


def _pick_operator(*, station_code: str | None) -> tuple[str, str]:
    """
    依當下班別 + 站別挑一個 operator，回傳 (operator_code, operator_name)。

    為什麼這樣設計：
    - 同班內的人會分配到不同站；不需要嚴格站對人，只要能在 demo 看出「同班別會輪不同站」即可。
    - 若沒對到 shift，退回 OP-001（避免 random.choice 拋例外讓整個 producer 崩）。
    """
    shift = _current_shift()
    pool = [op for op in OPERATORS if op[3] == shift] or OPERATORS
    # 站別 hash 偏一個 operator，讓同站趨向同人，但同班可變化
    if station_code:
        idx = (sum(ord(c) for c in station_code) + len(pool)) % len(pool)
        # 90% 機率走偏好人選，10% 隨機，避免每次都同一個顯示太假
        if random.random() < 0.9:
            return pool[idx][0], pool[idx][1]
    pick = random.choice(pool)
    return pick[0], pick[1]


def _resolve_panel_no(*, lot_no: str, wafer_no: int | str) -> str:
    """產生 panel_no（外部端 / Worker 端統一用同一個 format）。"""
    return f"{lot_no}-{wafer_no}"


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

    # 為什麼源頭就帶齊 line/station/operator/panel：
    # - 下游 SpcRealtimeWorker / RabbitMQ Worker 不需要再「猜」這些欄位；
    # - 前端 4 大頁面（SPC/異常/工單/物料追溯）所有需要的關聯欄位都從同一份 Kafka payload 出發，最不容易對不上。
    line_code = TOOL_TO_LINE.get(tool_code)
    station_code = TOOL_TO_STATION.get(tool_code)
    operator_code, operator_name = _pick_operator(station_code=station_code)
    panel_no = _resolve_panel_no(lot_no=lot_no, wafer_no=wafer_no)
    return {
        "event_id": str(uuid.uuid4()),
        "tool_code": tool_code,
        "line_code": line_code,
        "station_code": station_code,
        "lot_no": lot_no,
        "wafer_no": wafer_no,
        "panel_no": panel_no,
        "operator_code": operator_code,
        "operator_name": operator_name,
        "timestamp": _now_iso(),
        "temperature": round(temperature, 3),
        "pressure": round(pressure, 3),
        "yield_rate": round(yield_rate, 3),
        # 與後端 SpcInspectionEvent.InspectedQty 對齊：一則事件對應的檢驗件數（與 KPI 累積/速率對帳）。
        # 預設 1；真機可改為 AOI 一次掃到的板數等。
        "inspected_qty": 1,
        "defects": [],
    }


def build_defect_event(*, tool_code: str, lot_no: str, wafer_no: int, severity: str) -> dict:
    """
    建立 defect 事件（aoi.defect.event）。

    為什麼要有 defect event：
    - W03 需要示範 Kafka→RabbitMQ 的業務路由（alert/workorder）。
    - 我們用 severity 來模擬「告警」與「開工單」的優先級。

    為什麼 payload 加 panel_no / station_code / operator / x_coord / y_coord：
    - alarms 與 defects 表現在都需要冗餘 panel_no / station_code / operator；
      讓 Worker 端寫 DB 時免再 JOIN，也讓前端列表能直接顯示。
    - 缺陷座標（x/y）是 PCB AOI 的標準欄位，雖然 alarm 列表不會渲染，但 defects 表的追溯查詢會用到。
    """

    line_code = TOOL_TO_LINE.get(tool_code)
    station_code = TOOL_TO_STATION.get(tool_code)
    operator_code, operator_name = _pick_operator(station_code=station_code)
    panel_no = _resolve_panel_no(lot_no=lot_no, wafer_no=wafer_no)
    return {
        "event_id": str(uuid.uuid4()),
        "tool_code": tool_code,
        "line_code": line_code,
        "station_code": station_code,
        "lot_no": lot_no,
        "wafer_no": wafer_no,
        "panel_no": panel_no,
        "operator_code": operator_code,
        "operator_name": operator_name,
        "defect_code": f"DEF-{random.randint(1, 9999):04d}",
        "defect_type": random.choice(DEFECT_TYPES),
        "x_coord": round(random.uniform(0.0, 250.0), 2),
        "y_coord": round(random.uniform(0.0, 200.0), 2),
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


def _get_or_create_panel_id(cur, lot_id: str, lot_no: str, wafer_no: str, *, provider: str) -> tuple[str, str]:
    """
    依 (lot_id, panel_no) 查 panels；找不到就建立一筆。

    為什麼要把 wafer_id 改名 panel_id：
    - 系統聚焦 PCB 高階製程後，DB 已將 wafers 表改名 panels；
      這裡同步換掉，避免 INSERT 失敗或寫到不存在的表。

    為什麼回傳 (panel_id, panel_no)：
    - 後續 INSERT process_runs 時要冗餘 panel_no，避免 query 還要 JOIN 回 panels。
    """
    panel_no = f"{lot_no}-{wafer_no}"
    cur.execute("SELECT id FROM panels WHERE lot_id = %s AND panel_no = %s", (lot_id, panel_no))
    row = cur.fetchone()
    if row:
        return str(row[0]), panel_no

    panel_id = str(uuid.uuid4())
    # 為什麼這裡同時寫入 lot_no 冗餘：
    # - panels 表已新增 lot_no 必填欄位，作為 controllers/前端的可讀字串；
    #   建立板號時就帶入 lot_no，未來查詢免 JOIN lots。
    cur.execute(
        """
        INSERT INTO panels (id, lot_id, lot_no, panel_no, status, created_at)
        VALUES (%s, %s, %s, %s, %s, %s)
        """,
        (panel_id, lot_id, lot_no, panel_no, "in_progress", datetime.now(timezone.utc)),
    )
    return panel_id, panel_no


@dataclass(frozen=True)
class InspectionEvent:
    event_id: str
    tool_code: str
    lot_no: str
    wafer_no: str
    panel_no: str | None
    line_code: str | None
    station_code: str | None
    operator_code: str | None
    operator_name: str | None
    timestamp: datetime
    temperature: float | None
    pressure: float | None
    yield_rate: float | None


def _parse_event(raw: dict) -> InspectionEvent:
    """
    解析 Kafka inspection raw 事件。

    為什麼要把 line/station/operator/panel_no 也吃進來：
    - 源頭 producer 已經把這些欄位寫齊，下游 writer 接著要把它們落到 process_runs / panel_station_log；
    - 這個 dataclass 是 producer 與 writer 的「跨 thread contract」，任何新欄位先在這裡集中。
    """
    ts = raw.get("timestamp")
    if isinstance(ts, str):
        timestamp = datetime.fromisoformat(ts.replace("Z", "+00:00"))
    else:
        timestamp = datetime.now(timezone.utc)
    tool_code = str(raw.get("tool_code") or "AOI-A")
    lot_no = str(raw.get("lot_no") or "LOT-001")
    wafer_no = str(raw.get("wafer_no") or "1")
    return InspectionEvent(
        event_id=str(raw.get("event_id") or uuid.uuid4()),
        tool_code=tool_code,
        lot_no=lot_no,
        wafer_no=wafer_no,
        panel_no=(str(raw["panel_no"]) if raw.get("panel_no") else _resolve_panel_no(lot_no=lot_no, wafer_no=wafer_no)),
        line_code=(str(raw["line_code"]) if raw.get("line_code") else TOOL_TO_LINE.get(tool_code)),
        station_code=(str(raw["station_code"]) if raw.get("station_code") else TOOL_TO_STATION.get(tool_code)),
        operator_code=(str(raw["operator_code"]) if raw.get("operator_code") else None),
        operator_name=(str(raw["operator_name"]) if raw.get("operator_name") else None),
        timestamp=timestamp,
        temperature=(float(raw["temperature"]) if raw.get("temperature") is not None else None),
        pressure=(float(raw["pressure"]) if raw.get("pressure") is not None else None),
        yield_rate=(float(raw["yield_rate"]) if raw.get("yield_rate") is not None else None),
    )


def _upsert_panel_station_log(cur, ev: InspectionEvent, panel_id: str, *, provider: str) -> None:
    """
    將「板過站事件」寫入 panel_station_log。

    為什麼要做：
    - 物料追溯 / 板級時間軸頁面需要「這張板經過哪些站」；
    - 站別歷程不該交給人手動 seed，而要從 ingestion 即時長出來，與 Kafka 同步。

    為什麼用 (panel_id, station_code) 唯一性 + ExitedAt update 模式：
    - 同一張板可能在同站多次量測（複測），但站別歷程通常只記錄「進站時間 + 結果 + 出站時間」；
    - 若已存在同站紀錄則 update exited_at / result，這樣才不會重覆塞 log。
    """
    if not ev.station_code:
        return

    cur.execute(
        """
        SELECT TOP 1 id FROM panel_station_log
        WHERE panel_id = %s AND station_code = %s
        ORDER BY entered_at DESC
        """ if provider in ("sqlserver", "mssql") else
        """
        SELECT id FROM panel_station_log
        WHERE panel_id = %s AND station_code = %s
        ORDER BY entered_at DESC
        LIMIT 1
        """,
        (panel_id, ev.station_code),
    )
    row = cur.fetchone()
    if row:
        cur.execute(
            """
            UPDATE panel_station_log
            SET exited_at = %s,
                result = %s,
                operator = %s,
                operator_name = %s,
                tool_code = %s
            WHERE id = %s
            """,
            (
                ev.timestamp,
                "pass",
                ev.operator_code,
                ev.operator_name,
                ev.tool_code,
                str(row[0]),
            ),
        )
        return

    cur.execute(
        """
        INSERT INTO panel_station_log (
            id, panel_id, panel_no, station_code,
            entered_at, exited_at, result,
            operator, operator_name, tool_code, note
        )
        VALUES (
            %s, %s, %s, %s,
            %s, %s, %s,
            %s, %s, %s, %s
        )
        """,
        (
            str(uuid.uuid4()),
            panel_id,
            ev.panel_no or _resolve_panel_no(lot_no=ev.lot_no, wafer_no=ev.wafer_no),
            ev.station_code,
            ev.timestamp,
            ev.timestamp,
            "pass",
            ev.operator_code,
            ev.operator_name,
            ev.tool_code,
            None,
        ),
    )


def _insert_process_run(cur, ev: InspectionEvent, *, provider: str) -> None:
    """
    把一筆 inspection 事件寫入 process_runs，並同步更新 panel_station_log。

    為什麼此處同步寫冗餘欄位（tool_code/lot_no/panel_no）：
    - process_runs 表已新增這三個必填字串欄位，DTO 投影直接吃 entity 屬性即可；
    - 寫入時複製一次，後續查詢免 JOIN tools/lots/panels。

    為什麼這裡順手寫 panel_station_log：
    - 板過站是 inspection 的「副作用」事件；放同一個 transaction 內，
      可以保證「process_run 有值 → 站別歷程也一定有對應 row」。
    """
    tool_id = _get_or_create_tool_id(cur, ev.tool_code, provider=provider)
    recipe_id = _get_or_create_recipe_id(cur, provider=provider)
    lot_id = _get_or_create_lot_id(cur, ev.lot_no, provider=provider)
    panel_id, panel_no = _get_or_create_panel_id(cur, lot_id, ev.lot_no, ev.wafer_no, provider=provider)

    cur.execute(
        """
        INSERT INTO process_runs (
            id, tool_id, recipe_id, lot_id, panel_id,
            tool_code, lot_no, panel_no,
            run_start_at, run_end_at,
            temperature, pressure, yield_rate,
            result_status, created_at
        )
        VALUES (
            %s, %s, %s, %s, %s,
            %s, %s, %s,
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
            panel_id,
            ev.tool_code,
            ev.lot_no,
            panel_no,
            ev.timestamp,
            ev.timestamp,
            ev.temperature,
            ev.pressure,
            (ev.yield_rate / 100.0 if ev.yield_rate is not None and ev.yield_rate > 1 else ev.yield_rate),
            "pass",
            datetime.now(timezone.utc),
        ),
    )

    _upsert_panel_station_log(cur, ev, panel_id, provider=provider)


def producer_loop(stop: threading.Event) -> None:
    bootstrap = _env("KAFKA_BOOTSTRAP_SERVERS", "kafka:9092")
    topic_inspection = _env("KAFKA_TOPIC_INSPECTION", "aoi.inspection.raw")
    topic_defect = _env("KAFKA_TOPIC_DEFECT", "aoi.defect.event")

    # 為什麼 tool / lot 池擴大：
    # - 4 大頁面要展示「機台、產線、站別、批次、板號、人員」全欄位渲染；
    #   只有 2 機台 3 lots 會讓 demo 看起來像玩具，列表內容也單調。
    # - 工單號（lot_no）採 WO-yyyymmdd-NNN 格式比較貼近真實 MES 慣例。
    tool_codes = list(TOOL_TO_STATION.keys())
    today = datetime.now(timezone.utc).strftime("%Y%m%d")
    lot_nos = [f"WO-{today}-{i:03d}" for i in range(1, 9)]

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

            # 每 12 筆注入一次 defect event（用來 demo Kafka→RabbitMQ 的 alert/workorder 路由）
            # 為什麼把頻率從 20→12：4 頁面 demo 時 alarms / workorders 列表能比較快累積，主管看得到變化。
            if step % 12 == 0 and step > 0:
                sev = random.choices(["low", "medium", "high"], weights=[0.5, 0.35, 0.15], k=1)[0]
                defect = build_defect_event(tool_code=tool, lot_no=lot, wafer_no=wafer, severity=sev)
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

