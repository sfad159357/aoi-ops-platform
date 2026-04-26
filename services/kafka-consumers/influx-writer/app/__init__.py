"""
Kafka Consumer Group A — InfluxDB Writer

為什麼需要這個服務：
- W03 目標是把設備事件（Kafka）落地到「時序資料庫」（InfluxDB），讓 dashboard 能做趨勢圖。
- 這個 writer 專注處理「高頻數值」：temperature/pressure/yield_rate，避免跟 business DB（PostgreSQL）混在一起。

解決什麼問題：
- 把 `aoi.inspection.raw` 事件轉成 InfluxDB points，支援依 tool_code 的時間序列查詢與聚合。
"""

