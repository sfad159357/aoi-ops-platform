## Architecture（MVP）

### 目標
此文件用來描述 AOI Ops Platform 的模組切分與資料流，讓後續擴充時維持一致的邊界與責任歸屬。

### High-level
- **Frontend (`frontend/`)**：Dashboard / 查詢 / Defect Review / Copilot UI
- **Core Backend (`backend/`)**：主要業務 API、權限與 workflow（MVP 先不做複雜 RBAC）
- **Database (SQL Server)**：依 `ERD.md` 的 schema 存製造與缺陷資料
- **Python Services (`services/`)**：資料模擬、影像輔助、文件 Copilot（OpenAI）

### 資料流（簡述）
1. `data-simulator` 產生 tool/lot/wafer/process_run/alarm/defect 資料 → 寫入 DB
2. backend 提供查詢與 review API → frontend 顯示 dashboard 與 review UI
3. 使用者上傳文件 → backend 記錄 documents → `ai-copilot` 建索引與檢索 → frontend 問答顯示來源

