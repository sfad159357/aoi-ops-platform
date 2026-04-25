"""
Kafka Consumer（DB Writer）package

為什麼要做成 package：
- docker-compose 可以用 `python -m app` 啟動，不需要記檔名。
- 讓 consumer 的入口與 DB helper 拆開，後續擴充（寫 defects/alarms）更乾淨。
"""

