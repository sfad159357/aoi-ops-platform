# docs/ 文件索引

> 為什麼要有這份索引：文件多了之後容易迷路；這裡用「我現在想知道什麼」來導覽。

---

## 我想 30 秒理解整個系統

- [架構總覽（Architecture）](architecture.md)
- 視覺化：[../graph.md](../graph.md)
- 專案定位：[../project.md](../project.md)
- Repo 結構：[../structrure.md](../structrure.md)

## 我要把專案啟動起來

- **[Getting Started — 30 秒啟動](getting-started.md)**：從 git clone 到看到 SPC 圖
- **[Demo Script — 6 分鐘走完整條 PCB 產線](demo-script.md)**：分段腳本 + 念稿 + 預期畫面
- **[Acceptance Checklist](acceptance.md)**：v2 重新對齊驗收清單（40+ 條）

## 我要做即時推播相關

- **[即時推播 — SignalR](realtime-signalr.md)**：4 個 Hub、訊息格式、group 訂閱、注意事項

## 我要做 SPC 圖 / 八大規則

- **[SPC 技術與原理](spc.md)**：管制圖（Xbar-R/I-MR/P/C/U）、八大規則、Ca/Cp/Cpk
- 引擎實作：`backend/src/Application/Spc/`（C#）
- 批次 / 歷史：`services/spc-service/`（Python FastAPI port 8001）

## 我要做物料追溯 / 6 站時間軸

- **[物料追溯查詢](traceability.md)**：API 結構、3 張新表、前端頁邏輯

## 我要切 PCB / 半導體 demo

- **[Domain Profile 機制](domain-profile.md)**：JSON schema、切換指令、新增第三產業

## 我想看 logs / metrics / 情境演練

- **[觀測 — Logging + Metrics + 情境演練](observability.md)**：Serilog JSON、`/api/metrics`、SIM_SCENARIO

## 我要看 API / 資料模型

- [API Spec（REST + SignalR Hub）](api-spec.md)
- [ERD（PostgreSQL + InfluxDB）](../ERD.md)

## 我想理解 Clean Architecture 分層

- **[Clean Architecture 分層指南](clean-architecture.md)**：Domain / Application / Infrastructure / Api 依賴方向 + 常見反例

## 我卡住了

- [開發除錯筆記](debug-notes.md)
- [Troubleshooting：ingestion / SPC Live](troubleshooting-ingestion-process-runs.md)
- [Troubleshooting：Backend API 全掛（沒有 500）](troubleshooting-backend-api-down.md)

## 既有的 W02 開發指南

- [week2-w02-guide.md](week2-w02-guide.md)
