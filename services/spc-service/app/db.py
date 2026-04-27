"""
PostgreSQL 資料存取（SPC Live 模式）

為什麼要在 SPC Service 直接讀 DB：
- 你希望 SPC 圖表的資料來源是「真實製程資料」，而不是前端寫死或 demo payload。
- SPC 計算服務本質就是「把一段時間的量測序列 → 算出管制線/八大規則/能力指數」；
  直接在這裡查 DB，可以把資料與統計計算封裝在同一個服務，前端只要打 1 支 API。

解決什麼問題：
- 前端不需要知道資料表結構（process_runs、tools、lots…），避免耦合到 DB schema。
- 未來 Kafka/RabbitMQ 進來的資料，只要落到同一張表/欄位，SPC API 不用改就能吃到新來源。

注意：
- 這裡使用 psycopg3（psycopg[binary]），原因是 macOS/CI 環境通常不想編譯依賴，
  binary wheel 安裝成功率最高。
"""

from __future__ import annotations

import os
from dataclasses import dataclass
from typing import Literal

import psycopg
import pytds


MetricField = Literal["temperature", "pressure", "yield_rate"]


@dataclass(frozen=True)
class ProcessRunMetricRow:
    """從 process_runs 取出的單一量測點（含時間戳供排序）"""

    run_start_at: str
    value: float


def _get_db_conn_str() -> str:
    """
    取得 DB 連線字串。

    為什麼用環境變數：
    - docker-compose 內的 db host 是 `db`；本機跑時可能是 `localhost`。
    - 用 env 可以讓同一份程式碼在兩種環境都不必改。
    """

    conn = os.getenv("DB_CONNECTION")
    if not conn:
        raise RuntimeError(
            "缺少 DB_CONNECTION 環境變數。"
            "若用 docker-compose 啟動，請在 spc-service 加上 DB_CONNECTION；"
            "若本機跑，請自行 export DB_CONNECTION='Host=localhost;Port=5432;...'"
        )
    return _normalize_conninfo(conn)


def _normalize_conninfo(conn: str) -> str:
    """
    將 DB_CONNECTION 正規化成 psycopg 可接受的 conninfo 字串。

    為什麼要做這件事：
    - 本 repo 其他服務（.NET / docker-compose）多使用 `.NET ConnectionString` 風格：
      `Host=...;Port=...;Database=...;Username=...;Password=...;`
    - psycopg3 使用 libpq conninfo 風格（小寫鍵）：`host=... port=... dbname=... user=... password=...`
    - 如果不轉換，就會出現你現在看到的錯誤：`invalid connection option "Host"`。
    """

    s = conn.strip()

    # 先判斷是否為 `.NET;` 風格（用分號分隔）。
    # 注意：`.NET` 也會用 `Host=...`，所以不能只用 `host=` 來判斷是不是 libpq conninfo。
    if ";" not in s:
        # 若不是用分號分隔，才判斷是否已經是 libpq conninfo（通常是空白分隔、且帶 host=/dbname=/user=）
        lowered = s.lower()
        if "host=" in lowered or "dbname=" in lowered or "user=" in lowered:
            return s

    # 嘗試解析 `.NET;` 風格 key=value;key=value
    parts = [p for p in s.split(";") if p.strip()]
    kv: dict[str, str] = {}
    for p in parts:
        if "=" not in p:
            continue
        k, v = p.split("=", 1)
        kv[k.strip().lower()] = v.strip()

    # 常見 key 映射（大小寫不敏感）
    host = kv.get("host") or kv.get("server")
    port = kv.get("port")
    dbname = kv.get("database") or kv.get("dbname")
    user = kv.get("username") or kv.get("user") or kv.get("uid")
    password = kv.get("password") or kv.get("pwd")

    # 組成 conninfo（只放有值的欄位）
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

    if not items:
        # 萬一輸入完全無法解析，就原樣回傳，讓 psycopg 自己報更精準的錯
        return s

    return " ".join(items)


def _db_provider() -> str:
    """
    取得 DB provider。

    為什麼要有這個判斷：
    - 本專案第一階段用 PostgreSQL（psycopg）；第二階段要讓 SPC Service Live 端點也能讀 SQL Server。
    - 透過 env `DB_PROVIDER=postgres|sqlserver` 讓 docker-compose 不改程式碼就能切換。
    """
    return (os.getenv("DB_PROVIDER") or "postgres").strip().lower()


@dataclass(frozen=True)
class _SqlServerConn:
    host: str
    port: int
    database: str
    user: str
    password: str


def _parse_sqlserver_conn_str(conn: str) -> _SqlServerConn:
    """
    解析 SQL Server 的 .NET ConnectionString。

    例：
      Server=mssql,1433;Database=AOIOpsPlatform_MSSQL;User Id=sa;Password=...;

    為什麼要 parse：
    - 本服務改用 pytds（純 Python），不需要容器內安裝 ODBC driver；
    - 但 pytds connect 需要 host/port/database/user/password 拆開。
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

    database = kv.get("database") or kv.get("initial catalog")
    user = kv.get("user id") or kv.get("uid") or kv.get("username") or kv.get("user")
    password = kv.get("password") or kv.get("pwd")

    if not database:
        raise RuntimeError("SQL Server connection string 缺少 Database=... 欄位")
    if not user or not password:
        raise RuntimeError("SQL Server connection string 缺少 User Id / Password 欄位")

    return _SqlServerConn(host=host, port=port, database=database, user=user, password=password)


def list_tools() -> list[dict]:
    """
    取得工具清單（tool_code/tool_name）。

    為什麼要提供這支：
    - 前端 Live 模式需要下拉選單讓使用者選 tool，避免手動貼 Guid。
    """

    sql = """
    SELECT tool_code, tool_name
    FROM tools
    ORDER BY tool_code
    """

    provider = _db_provider()
    if provider in ("sqlserver", "mssql"):
        raw = os.getenv("DB_CONNECTION") or ""
        c = _parse_sqlserver_conn_str(raw)
        with pytds.connect(c.host, database=c.database, user=c.user, password=c.password, port=c.port) as conn:
            with conn.cursor() as cur:
                cur.execute(sql)
                rows = cur.fetchall()
    else:
        with psycopg.connect(_get_db_conn_str()) as conn:
            with conn.cursor() as cur:
                cur.execute(sql)
                rows = cur.fetchall()

    return [{"tool_code": r[0], "tool_name": r[1]} for r in rows]


def fetch_process_run_metric(
    *,
    tool_code: str | None,
    metric: MetricField,
    limit: int = 60,
) -> list[ProcessRunMetricRow]:
    """
    從 process_runs 取得某個 metric 的最近 N 筆資料（依 run_start_at 排序）。

    為什麼用 process_runs：
    - 目前 DB schema 已存在 temperature/pressure/yield_rate，最容易做出「真資料」SPC demo。
    - 後續要改成 tool_metrics（InfluxDB / Kafka 寫入）也只需換 SQL。
    """

    if limit < 10:
        raise ValueError("limit 至少要 10（不然八大規則與管制線會很不穩）")

    # 安全起見：metric 欄位只允許固定白名單，避免 SQL injection
    column = {
        "temperature": "temperature",
        "pressure": "pressure",
        "yield_rate": "yield_rate",
    }[metric]

    # tool_code 可選：不傳就取全體 process_runs（適合 demo）
    where = ""
    params: list[object] = []
    if tool_code:
        where = "WHERE t.tool_code = %s"
        params.append(tool_code)

    sql = f"""
    SELECT pr.run_start_at, pr.{column}
    FROM process_runs pr
    LEFT JOIN tools t ON t.id = pr.tool_id
    {where}
    AND pr.{column} IS NOT NULL
    ORDER BY pr.run_start_at DESC
    LIMIT %s
    """

    # 如果沒有 tool_code，就 where 會是空字串；此時上面的 AND 會語法錯
    # 因此我們分兩種 SQL 生成（避免在字串拼接上踩坑）。
    if tool_code:
        # SQL Server 不支援 LIMIT，改用 TOP
        if _db_provider() in ("sqlserver", "mssql"):
            sql = f"""
            SELECT TOP (%s) pr.run_start_at, pr.{column}
            FROM process_runs pr
            LEFT JOIN tools t ON t.id = pr.tool_id
            WHERE t.tool_code = %s
              AND pr.{column} IS NOT NULL
            ORDER BY pr.run_start_at DESC
            """
            params = [limit, tool_code]
        else:
            sql = f"""
        SELECT pr.run_start_at, pr.{column}
        FROM process_runs pr
        LEFT JOIN tools t ON t.id = pr.tool_id
        WHERE t.tool_code = %s
          AND pr.{column} IS NOT NULL
        ORDER BY pr.run_start_at DESC
        LIMIT %s
        """
            params = [tool_code, limit]
    else:
        if _db_provider() in ("sqlserver", "mssql"):
            sql = f"""
            SELECT TOP (%s) pr.run_start_at, pr.{column}
            FROM process_runs pr
            WHERE pr.{column} IS NOT NULL
            ORDER BY pr.run_start_at DESC
            """
            params = [limit]
        else:
            sql = f"""
        SELECT pr.run_start_at, pr.{column}
        FROM process_runs pr
        WHERE pr.{column} IS NOT NULL
        ORDER BY pr.run_start_at DESC
        LIMIT %s
        """
            params = [limit]

    provider = _db_provider()
    if provider in ("sqlserver", "mssql"):
        raw = os.getenv("DB_CONNECTION") or ""
        c = _parse_sqlserver_conn_str(raw)
        with pytds.connect(c.host, database=c.database, user=c.user, password=c.password, port=c.port) as conn:
            with conn.cursor() as cur:
                cur.execute(sql, params)
                rows = cur.fetchall()
    else:
        with psycopg.connect(_get_db_conn_str()) as conn:
            with conn.cursor() as cur:
                cur.execute(sql, params)
                rows = cur.fetchall()

    # 回傳需要「時間由舊到新」的序列，SPC 才能正確偵測趨勢規則
    rows = list(reversed(rows))
    result: list[ProcessRunMetricRow] = []
    for run_start_at, val in rows:
        if val is None:
            continue
        result.append(ProcessRunMetricRow(run_start_at=str(run_start_at), value=float(val)))
    return result

