"""
PostgreSQL 資料存取（SPC Live 模式）

為什麼要在 SPC Service 直接讀 DB：
- 你希望 SPC 圖表的資料來源是「真實製程資料」，而不是前端寫死或 demo payload。
- SPC 計算服務本質就是「把一段時間的量測序列 → 算出管制線/八大規則/能力指數」；
  直接在這裡查 DB，可以把資料與統計計算封裝在同一個服務，前端只要打 1 支 API。

解決什麼問題：
- 前端不需要知道資料表結構（process_runs、tools、lots…），避免耦合到 DB schema。
- 未來 Kafka/MQTT 進來的資料，只要落到同一張表/欄位，SPC API 不用改就能吃到新來源。

注意：
- 這裡使用 psycopg3（psycopg[binary]），原因是 macOS/CI 環境通常不想編譯依賴，
  binary wheel 安裝成功率最高。
"""

from __future__ import annotations

import os
from dataclasses import dataclass
from typing import Literal, Sequence

import psycopg


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
    return conn


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
        sql = f"""
        SELECT pr.run_start_at, pr.{column}
        FROM process_runs pr
        LEFT JOIN tools t ON t.id = pr.tool_id
        WHERE t.tool_code = %s
          AND pr.{column} IS NOT NULL
        ORDER BY pr.run_start_at DESC
        LIMIT %s
        """
    else:
        sql = f"""
        SELECT pr.run_start_at, pr.{column}
        FROM process_runs pr
        WHERE pr.{column} IS NOT NULL
        ORDER BY pr.run_start_at DESC
        LIMIT %s
        """

    params.append(limit)

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

