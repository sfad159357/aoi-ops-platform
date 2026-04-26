"""
Kafka Consumer Group B — RabbitMQ Publisher（alert / workorder）

目標：
- 消費 Kafka `aoi.defect.event`
- 依 severity 路由到 RabbitMQ：
  - severity=high → alert + workorder
  - severity=medium → alert
  - severity=low →（MVP）只寫 alert（方便 demo 事件流）

為什麼 severity 決定路由：
- 你要做的不是「純資料搬運」，而是貼近 MES 的業務邏輯：高嚴重度缺陷要立刻告警並開工單。
"""

from __future__ import annotations

import json
import os
import time

import pika
from kafka import KafkaConsumer


def _env(name: str, default: str | None = None) -> str:
    v = os.getenv(name)
    if v is None or v.strip() == "":
        if default is not None:
            return default
        raise RuntimeError(f"缺少環境變數：{name}")
    return v


def main() -> None:
    bootstrap = _env("KAFKA_BOOTSTRAP_SERVERS", "kafka:9092")
    topic = _env("KAFKA_TOPIC_DEFECT", "aoi.defect.event")
    group_id = _env("KAFKA_CONSUMER_GROUP", "aoiops-rabbitmq-publisher")

    amqp_url = _env("RABBITMQ_URL", "amqp://guest:guest@rabbitmq:5672/")
    queue_alert = _env("RABBITMQ_QUEUE_ALERT", "alert")
    queue_workorder = _env("RABBITMQ_QUEUE_WORKORDER", "workorder")

    consumer = KafkaConsumer(
        topic,
        bootstrap_servers=[bootstrap],
        group_id=group_id,
        enable_auto_commit=True,
        auto_offset_reset="latest",
        value_deserializer=lambda b: json.loads(b.decode("utf-8")),
        key_deserializer=lambda b: b.decode("utf-8") if b else None,
    )

    print(f"[rabbitmq-publisher] bootstrap={bootstrap} topic={topic} group={group_id}")
    print(f"[rabbitmq-publisher] amqp_url={amqp_url} alert={queue_alert} workorder={queue_workorder}")

    wrote = 0
    published_batches = 0
    while True:
        try:
            params = pika.URLParameters(amqp_url)
            conn = pika.BlockingConnection(params)
            ch = conn.channel()
            ch.queue_declare(queue=queue_alert, durable=True)
            ch.queue_declare(queue=queue_workorder, durable=True)

            while True:
                records = consumer.poll(timeout_ms=1000, max_records=200)
                if not records:
                    continue

                for _tp, msgs in records.items():
                    for m in msgs:
                        ev = m.value or {}
                        severity = str(ev.get("severity") or "low").lower()
                        body = json.dumps(ev, ensure_ascii=False).encode("utf-8")

                        # alert 一律送（MVP：讓你在 DB 端能看到 alarms 持續新增）
                        ch.basic_publish(
                            exchange="",
                            routing_key=queue_alert,
                            body=body,
                            properties=pika.BasicProperties(delivery_mode=2),  # persistent
                        )
                        wrote += 1

                        if severity in ("high", "critical"):
                            ch.basic_publish(
                                exchange="",
                                routing_key=queue_workorder,
                                body=body,
                                properties=pika.BasicProperties(delivery_mode=2),
                            )
                            wrote += 1

                published_batches += 1
                # 為什麼用 batch 計數：缺陷事件是低頻（每 20 筆 inspection 才一筆），用 batch 計數更容易看到服務在工作。
                if published_batches % 5 == 0:
                    print(f"[rabbitmq-publisher] published_messages={wrote} batches={published_batches}")
        except Exception as e:
            print(f"[rabbitmq-publisher] ERROR: {e}")
            time.sleep(2.0)


if __name__ == "__main__":
    main()

