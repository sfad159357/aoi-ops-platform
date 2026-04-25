"""
Data Simulator 入口（Kafka Producer）

為什麼要取消 MQTT、改用 Kafka 當設備傳輸協議：
- 你希望整個平台只保留 Kafka + RabbitMQ，避免 MQTT broker/bridge 的額外維運與複雜度。
- Kafka 本身具備可重播（offset）、多消費者 fan-out、以及較完整的追溯能力，適合作為設備事件匯流排。

解決什麼問題：
- 讓設備模擬器可以直接送出 `aoi.inspection.raw` / `aoi.defect.event`，供後續 consumer 寫入 DB/Influx，並進一步生成 SPC 圖表。

注意：
- 這裡先做 MVP：固定頻率送 inspection raw，並在特定條件下送 defect event。
- 後續可擴充：加入漂移情境、批次（lot/wafer）切換、以及可配置的異常注入比例。
"""

from __future__ import annotations

import json
import os
import random
import time
import uuid
from datetime import datetime, timezone

from kafka import KafkaProducer


def _env(name: str, default: str) -> str:
    """
    讀取環境變數（帶預設值）。

    為什麼要集中成 function：
    - 避免每個地方都寫一堆 `os.getenv`，可讀性差且容易打錯 key。
    """

    return os.getenv(name, default)


def _now_iso() -> str:
    """回傳 ISO-8601 時間字串（UTC），讓 downstream 好排序與對齊。"""

    return datetime.now(timezone.utc).isoformat()


def build_inspection_event(*, tool_code: str, lot_no: str, wafer_no: int) -> dict:
    """
    建立 aoi.inspection.raw 事件 payload。

    為什麼要做成獨立 function：
    - 後續要加入「漂移/異常注入」時，只需要在這個 function 裡調整規則即可。
    """

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


def build_defect_event(*, tool_code: str, defect_code: str, severity: str) -> dict:
    """
    建立 aoi.defect.event 事件 payload。

    解決什麼問題：
    - 讓 RabbitMQ publisher / alarm pipeline 可以根據 severity 做業務路由（alert/workorder）。
    """

    return {
        "event_id": str(uuid.uuid4()),
        "tool_code": tool_code,
        "defect_code": defect_code,
        "severity": severity,
        "timestamp": _now_iso(),
    }


def main() -> None:
    """
    主迴圈：定期送 Kafka 事件。

    參數由環境變數控制：
    - KAFKA_BOOTSTRAP_SERVERS：Kafka broker 位址（compose 內為 kafka:9092）
    - KAFKA_TOPIC_INSPECTION / KAFKA_TOPIC_DEFECT：topic 名稱
    """

    bootstrap = _env("KAFKA_BOOTSTRAP_SERVERS", "kafka:9092")
    topic_inspection = _env("KAFKA_TOPIC_INSPECTION", "aoi.inspection.raw")
    topic_defect = _env("KAFKA_TOPIC_DEFECT", "aoi.defect.event")

    tool_codes = ["AOI-A", "AOI-B"]
    lot_nos = ["LOT-001", "LOT-002", "LOT-003"]

    producer = KafkaProducer(
        bootstrap_servers=[bootstrap],
        value_serializer=lambda v: json.dumps(v).encode("utf-8"),
        key_serializer=lambda v: v.encode("utf-8") if isinstance(v, str) else None,
        acks="all",
        linger_ms=20,
    )

    print(f"[data-simulator] bootstrap={bootstrap}")
    print(f"[data-simulator] topic_inspection={topic_inspection} topic_defect={topic_defect}")

    i = 0
    while True:
        tool = random.choice(tool_codes)
        lot = random.choice(lot_nos)
        wafer = random.randint(1, 25)

        evt = build_inspection_event(tool_code=tool, lot_no=lot, wafer_no=wafer)
        producer.send(topic_inspection, key=tool, value=evt)

        # 每 15 筆注入一次 defect event（MVP 版本，讓告警路由可 demo）
        if i % 15 == 0 and i > 0:
            defect = build_defect_event(
                tool_code=tool,
                defect_code=f"DEF-{random.randint(1, 9999):04d}",
                severity=random.choice(["low", "medium", "high"]),
            )
            producer.send(topic_defect, key=tool, value=defect)

        producer.flush(timeout=2)
        i += 1

        time.sleep(1.0)


if __name__ == "__main__":
    main()

