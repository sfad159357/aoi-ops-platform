-- 為什麼需要這個檔案：
-- PostgreSQL 在第一次初始化資料目錄（volume 是全新）時，會自動執行 /docker-entrypoint-initdb.d 內的 .sql。
-- 我們在 MVP 階段先啟用常用 extension，避免後續功能（例如需要 UUID/加密相關函數）時又要手動進容器處理。

CREATE EXTENSION IF NOT EXISTS pgcrypto;

