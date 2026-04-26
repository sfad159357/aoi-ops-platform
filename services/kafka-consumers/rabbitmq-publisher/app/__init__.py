"""
Kafka Consumer Group B — RabbitMQ Publisher

為什麼需要這個服務：
- Kafka 是設備事件匯流排；RabbitMQ 是業務事件路由（告警/工單）。
- 我們用這個 publisher 把「缺陷/異常事件」轉成「可由多系統消費的業務 queue」。

解決什麼問題：
- 讓後續 alarm/workorder pipeline 可以獨立演進（例如：告警抑制、工單分派）而不影響設備數據主流。
"""

