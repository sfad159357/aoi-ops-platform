"""
RabbitMQ Consumers — DB Sink（alert/workorder → PostgreSQL）

為什麼需要這個服務：
- RabbitMQ 是「業務事件路由」，但 dashboard/查詢需要落在 PostgreSQL 才能持久化、查詢與稽核。
- 我們把 alert/workorder queue 的訊息落地到 `alarms` / `workorders`。

解決什麼問題：
- 形成「告警閉環」的資料基礎：事件進來 → DB 有紀錄 → 前端可查 → 後續可追蹤狀態。
"""

