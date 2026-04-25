"""
Data Simulator package

為什麼要做成可用 `python -m app` 啟動：
- docker-compose 裡面直接跑 `python -m app`，不需要記額外的檔名。
- 把入口集中在 `app/__main__.py`，方便未來加參數或切換不同情境（正常/異常/漂移）。
"""

