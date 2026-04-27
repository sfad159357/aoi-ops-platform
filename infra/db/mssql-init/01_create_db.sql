-- ============================================================================
-- AOI Ops Platform - SQL Server 初始化腳本
--
-- 為什麼有這支腳本：
--   azure-sql-edge image 沒有 postgres 的 /docker-entrypoint-initdb.d 機制，
--   必須由 mssql-init sidecar 在主容器 healthy 之後，透過 sqlcmd -i 執行此檔。
--
-- 為什麼用 IF DB_ID IS NULL：
--   保持 idempotent；這個 sidecar 每次 docker compose up 都會被當成 one-shot 執行，
--   重複跑時不能噴錯讓 init 容器以非 0 退出，否則會造成「明明 DB 已存在卻顯示失敗」的誤判。
--
-- 為什麼資料庫命名 AOIOpsPlatform_MSSQL：
--   與 postgres 上的 AOIOpsPlatform 區分，避免日後若有人接服務時混淆來源。
-- ============================================================================

IF DB_ID(N'AOIOpsPlatform_MSSQL') IS NULL
BEGIN
    CREATE DATABASE [AOIOpsPlatform_MSSQL];
    PRINT 'Database AOIOpsPlatform_MSSQL created.';
END
ELSE
BEGIN
    PRINT 'Database AOIOpsPlatform_MSSQL already exists, skip.';
END
GO
