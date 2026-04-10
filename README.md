## AOI Ops Platform（MVP）

此 repo 依照 `project.md` / `schedule.md` / `structrure.md` / `ERD.md` 的最初版規格建置，並在建構過程允許逐步增刪調整。

### 目標
- **Defect Review**：匯入缺陷資料/影像、清單/細節、標記 True/False、Review history、相似案例查詢（MVP 先 metadata 規則式）
- **Fab Monitoring**：tool/lot/wafer/recipe dashboard、yield/alarm/defect trend、異常查詢
- **Knowledge Copilot**：文件上傳、檢索問答（OpenAI）、回覆附來源

### Repo 結構（與 `structrure.md` 對齊）
- `frontend/`：React + TypeScript（Vite）
- `backend/`：ASP.NET Core Web API（分層：Api/Application/Domain/Infrastructure）
- `services/`：Python（data-simulator / vision-helper / ai-copilot）
- `infra/`：Docker Compose、DB init/migrations
- `docs/`：架構、ERD、API spec、scope

### 開發與啟動（之後會補齊）
> 這裡後續會補上 `docker compose up` 的一鍵啟動方式，以及本機開發指令。

