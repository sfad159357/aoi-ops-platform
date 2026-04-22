# AOI Ops Platform

## 定位
模擬高科技製造場景（PCB / 半導體）的 AOI 缺陷管理、
生產資訊監控、工程知識助理平台。

主體用 C# / ASP.NET Core，輔助模組用 Python。
模擬真實 MES 場景：設備數據收集、製程監控、異常告警、
缺陷追蹤、與 ERP 資料整合。

### 整體架構
Frontend
React
TypeScript
負責 dashboard、defect review、查詢頁、文件問答頁

Core Backend
C# / ASP.NET Core Web API
負責主業務邏輯、帳號、查詢、review workflow、alarm workflow、API
這是你主要拿來對標企業 C#/.NET 技術棧的核心

Message Broker
MQTT（Mosquitto）
模擬設備端數據上傳至平台
Data Simulator 透過 MQTT publish 製造數據
Backend subscribe 後寫入 DB

Database
  PostgreSQL（結構化資料：lot、wafer、alarm、defect、review）
  InfluxDB（時序資料：設備即時數據、yield trend）

存 lot、wafer、tool、recipe、alarm、defect、review、documents 等資料
開發環境以 PostgreSQL 為主，方便跨平台與容器化一鍵啟動

Python Services
  Data Simulator：模擬 tool / lot / wafer / AOI defect /
                  yield / alarm 資料，透過 MQTT 發送
  Vision Helper：OpenCV 基本影像前處理、相似圖輔助
  AI Copilot Service：文件切塊、檢索、RAG、摘要
用 OpenCV 做基本影像前處理、相似圖輔助
AI Copilot Service
做文件切塊、檢索、RAG、摘要

Infra
Docker Compose
一鍵拉起前端、後端、DB、 Mosquitto、InfluxDB、Python service

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

