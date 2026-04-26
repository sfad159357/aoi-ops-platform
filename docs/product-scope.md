# Product Scope

> 用來固定功能範圍，避免實作過程無限制膨脹。

## In scope（v2 重新對齊）

- ① **SPC 統計製程管制**：Kafka → SignalR 即時 X̄/R 圖、八大規則違規偵測、Cpk/Ca/Cp 等級
- ② **工單管理**：RabbitMQ workorder queue → 列表即時新增動畫
- ③ **異常記錄**：RabbitMQ alert queue → 列表即時新增動畫
- ④ **物料追溯查詢**：依 panel_no 查 6 站時間軸 + 物料批號 + 同 lot/同物料相關板
- Domain Profile 機制：同 codebase 切換 PCB / 半導體用語
- Defect Review（既有功能保留）：list/detail、true/false、分類、review history、similar v1
- Dashboard 基本趨勢圖（既有）

## Out of scope（明確不做）

- MQTT / Mosquitto / OPC-UA
- 機器學習 / 影像分類 / 缺陷影像 ML 訓練
- RAG / 向量檢索 / Knowledge Copilot
- 多角色 RBAC / 簽核流程
- 多 broker Kafka 叢集 / Kafka 認證
- 使用者帳號系統（先寫死 demo user）
- Grafana 儀表板
