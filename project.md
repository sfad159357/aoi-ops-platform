# AOI Ops Platform

## 定位
半導體 / 高科技製造場景的 AOI 缺陷管理、製程監控、工程知識助理平台。
主體用 C# / ASP.NET Core，輔助模組用 Python。

### 整體架構
Frontend
React
TypeScript
負責 dashboard、defect review、查詢頁、文件問答頁
Core Backend
C# / ASP.NET Core Web API
負責主業務邏輯、帳號、查詢、review workflow、alarm workflow、API
這是你主要拿來對標企業 C#/.NET 技術棧的核心
Database
PostgreSQL
存 lot、wafer、tool、recipe、alarm、defect、review、documents 等資料
如果你想更貼 .NET 企業感，也可改 SQL Server
Python Services
Data Simulator
模擬 tool / lot / wafer / AOI defect / yield / alarm 資料
Vision Helper
用 OpenCV 做基本影像前處理、相似圖輔助
AI Copilot Service
做文件切塊、檢索、RAG、摘要
Infra
Docker Compose
一鍵拉起前端、後端、DB、Python service

### 功能模組
Defect Review Module
匯入 defect image 與 metadata
defect list / detail
true defect / false alarm 標記
defect 分類
相似案例查詢
review history
Fab Monitoring Module
tool / lot / wafer / recipe dashboard
yield trend
defect trend
alarm list
異常查詢
summary report
Knowledge Copilot Module
上傳 SOP、recipe 文件、異常手冊
文件搜尋
問答附來源
根據 defect / alarm 給 troubleshooting 建議
shift summary
Data Simulation Module
自動產生製造資料流
模擬正常 / 異常 / 漂移 / 誤判情境
定時寫入 DB

### 建議資料表
tools
lots
wafers
recipes
process_runs
alarms
defects
defect_images
defect_reviews
documents
document_chunks
copilot_queries

### 資料流
Python simulator 產生製造與 AOI 假資料
C# backend 接收並寫入 DB
React 前端讀取 API 顯示 dashboard 與 review 頁
使用者上傳文件後，Python AI service 建索引
Copilot 根據文件與 defect/alarm context 回答問題
### 目錄切分建議
frontend/
backend/
services/data-simulator/
services/vision-helper/
services/ai-copilot/
infra/
docs/

### 技術分工
C#：平台主體、企業系統感、履歷主訊號
Python：AI、影像、模擬、自動化
React：展示你原本前端能力，但主角不是前端本身

