3 個月 MVP 順序

第 1 月
目標：平台骨架跑起來

第 1 週
建 monorepo
建 React 前端
建 ASP.NET Core Web API
建 PostgreSQL + Docker Compose
接好基本連線
第 2 週
建 tools/lots/wafers/process_runs/alarms/defects schema
做 seed data
做 list API
第 3 週
做 Python data-simulator
定義假資料規則
寫入 DB 或透過 API 匯入
第 4 週
做 dashboard 首頁
顯示 yield、alarm、defect summary
做 lot / tool / defect 查詢頁
第 2 月
目標：完成 AOI Defect Review 主流程

第 5 週
defect detail 頁
defect image upload
defect metadata 顯示
第 6 週
defect review flow
true defect / false alarm 標記
review history
第 7 週
similar defect 功能第一版
先不用 ML，先做 rule-based 或 metadata 相似查詢
第 8 週
dashboard 補 lot/tool/recipe filter
補 alarm list 與 basic trend chart
第 3 月
目標：補 AI 文件助理，讓專案變完整

第 9 週
建 documents / document_chunks
做文件上傳與切塊
第 10 週
做 Python ai-copilot service
接 embedding / retrieval / answer generation
第 11 週
前端加 copilot 問答頁
顯示 source refs
支援帶 defect/alarm context 詢問
第 12 週
補 README、架構圖、假資料說明
錄 demo
整理履歷文案
