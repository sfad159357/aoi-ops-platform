"""
Ingestion service package

為什麼做成單一 ingestion 容器：
- 你希望「設備送數據」與「落地 DB」能一鍵啟動，不要開兩個 service 才能跑出 SPC Live。
- 這個容器同時負責：
  1) Kafka Producer（模擬設備事件）
  2) Kafka Consumer → PostgreSQL（寫入 process_runs）

解決什麼問題：
- 減少 docker compose service 數量，降低新手啟動與排錯成本。
"""

