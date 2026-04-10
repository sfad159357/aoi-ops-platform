## Product Scope（MVP）

此文件用來固定 MVP 的功能範圍，避免實作過程無限制膨脹。

### In scope
- Dashboard（yield/alarm/defect summary + 基本 trend）
- 查詢頁（lot/tool/defect）
- Defect Review（list/detail、true/false、分類、review history、similar v1）
- Knowledge Copilot（文件上傳、檢索問答、回覆附來源）

### Out of scope（先不做）
- 進階權限/簽核流程（多角色、多站點）
- 真正的視覺 ML 訓練/部署（MVP 只做規則式/輕量相似）
- 大規模即時串流與訊息佇列（先用 DB/排程）

