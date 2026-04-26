"""
Kafka Consumer Group A — InfluxDB Writer（tool_metrics / yield_trend）

目標：
- 消費 Kafka `aoi.inspection.raw`
- 寫入 InfluxDB（bucket: aoiops）
  - measurement: tool_metrics（temperature/pressure/yield_rate）
  - measurement: yield_trend（yield_rate 方便單獨畫趨勢）

為什麼要分兩個 measurement：
- tool_metrics 放「多欄位量測」，適合做多軸/多欄位查詢
- yield_trend 放「單欄位趨勢」，後面要做 downsample / 聚合會更直覺

解決什麼問題：
- 讓 dashboard 可以從 InfluxDB 取時間序列，不需要每次都掃 PostgreSQL 大表。
"""

from __future__ import annotations

import json
import os
import time
from datetime import datetime

from influxdb_client import InfluxDBClient, Point, WritePrecision
from influxdb_client.client.write_api import SYNCHRONOUS
from kafka import KafkaConsumer


def _env(name: str, default: str | None = None) -> str:
    v = os.getenv(name)
    if v is None or v.strip() == "":
        if default is not None:
            return default
        raise RuntimeError(f"缺少環境變數：{name}")
    return v


def _as_float(v) -> float | None:
    try:
        return float(v) if v is not None else None
    except Exception:
        return None


def main() -> None:
    bootstrap = _env("KAFKA_BOOTSTRAP_SERVERS", "kafka:9092")
    topic = _env("KAFKA_TOPIC_INSPECTION", "aoi.inspection.raw")
    group_id = _env("KAFKA_CONSUMER_GROUP", "aoiops-influx-writer")

    influx_url = _env("INFLUX_URL", "http://influxdb:8086")
    influx_org = _env("INFLUX_ORG", "aoiops")
    influx_bucket = _env("INFLUX_BUCKET", "aoiops")
    influx_token = _env("INFLUX_TOKEN", "aoiops-dev-token")

    # 為什麼用 enable_auto_commit：MVP 階段先求「穩定寫入」與可 demo，
    # 後續若要做到 exactly-once，可改成手動 commit + idempotent 寫入策略。
    consumer = KafkaConsumer(
        topic,
        bootstrap_servers=[bootstrap],
        group_id=group_id,
        enable_auto_commit=True,
        auto_offset_reset="latest",
        value_deserializer=lambda b: json.loads(b.decode("utf-8")),
        key_deserializer=lambda b: b.decode("utf-8") if b else None,
    )

    print(f"[influx-writer] bootstrap={bootstrap} topic={topic} group={group_id}")
    print(f"[influx-writer] influx_url={influx_url} org={influx_org} bucket={influx_bucket}")

    with InfluxDBClient(url=influx_url, token=influx_token, org=influx_org) as client:
        write_api = client.write_api(write_options=SYNCHRONOUS)

        wrote = 0
        while True:
            records = consumer.poll(timeout_ms=1000, max_records=200)
            if not records:
                continue

            points: list[Point] = []
            for _tp, msgs in records.items():
                for m in msgs:
                    raw = m.value or {}
                    tool_code = str(raw.get("tool_code") or "AOI-A")
                    lot_no = str(raw.get("lot_no") or "LOT-001")
                    ts = raw.get("timestamp")

                    # timestamp 用 ISO-8601（從 ingestion/data-simulator 來）
                    try:
                        t = datetime.fromisoformat(str(ts).replace("Z", "+00:00"))
                    except Exception:
                        t = datetime.utcnow()

                    temperature = _as_float(raw.get("temperature"))
                    pressure = _as_float(raw.get("pressure"))
                    yield_rate = _as_float(raw.get("yield_rate"))

                    # measurement: tool_metrics（多欄位）
                    p1 = (
                        Point("tool_metrics")
                        .tag("tool_code", tool_code)
                        .tag("lot_no", lot_no)
                        .field("temperature", temperature if temperature is not None else 0.0)
                        .field("pressure", pressure if pressure is not None else 0.0)
                        .field("yield_rate", yield_rate if yield_rate is not None else 0.0)
                        .time(t, WritePrecision.NS)
                    )
                    points.append(p1)

                    # measurement: yield_trend（單欄位）
                    if yield_rate is not None:
                        p2 = (
                            Point("yield_trend")
                            .tag("tool_code", tool_code)
                            .tag("lot_no", lot_no)
                            .field("yield_rate", yield_rate)
                            .time(t, WritePrecision.NS)
                        )
                        points.append(p2)

            try:
                write_api.write(bucket=influx_bucket, org=influx_org, record=points)
                wrote += len(points)
                if wrote % 200 == 0:
                    print(f"[influx-writer] wrote_points={wrote}")
            except Exception as e:
                # 為什麼不讓程式 crash：InfluxDB 初始化/重啟時很常短暫不可用，MVP 用重試即可
                print(f"[influx-writer] ERROR: {e}")
                time.sleep(1.0)


if __name__ == "__main__":
    main()

