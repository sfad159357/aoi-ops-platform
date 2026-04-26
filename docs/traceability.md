# 物料追溯查詢

> 為什麼需要：當 FQC 抽到不良板，產線品保第一時間要回答「這張板走過哪些站、哪一站可能有問題、用了哪批錫膏 / FR4 / 電容、同批同物料還有哪些板」。
> 這頁把 PCB SMT 6 站時間軸 + 物料批號 + 同批/同物料相關板，一次以單一 panel_no 查出來。

## 入口

`GET /api/trace/panel/{panelNo}`

`panelNo` 是 `wafers.panel_no`（W08 新增的欄位，全表 UNIQUE，例：`PCB-20240422-LOT-001-1`）。
通常會印在 PCB 上的 QR Code，掃進來就送進這個 API。

## 資料模型

### W08 新增的 3 張表

| 表 | 用途 |
|---|---|
| `material_lots` | 物料批號（錫膏 / FR4 / 電容 / 主晶片 / 助焊劑），含供應商與入廠時間 |
| `panel_material_usage` | 板與物料批號多對多 join，記錄該板用了哪批物料、用量、時間 |
| `panel_station_log` | 板在每一站（SPI / SMT / REFLOW / AOI / ICT / FQC）的進站 / 出站時間與結果 |

`wafers` 加 `panel_no varchar`，全表 UNIQUE 並建 filtered index。

詳細欄位見 [../ERD.md](../ERD.md) §11–13。

## API 回傳結構

```jsonc
{
  "panel": {
    "id": "uuid",
    "panelNo": "PCB-20240422-LOT-001-1",
    "lotId": "uuid",
    "lotNo": "LOT-001",
    "status": "in_progress",
    "createdAt": "2026-04-22T08:00:00Z"
  },
  "stationTimeline": [
    {
      "stationCode": "SPI",
      "label": "錫膏印刷",         // 從 domain profile 補
      "seq": 1,
      "enteredAt": "2026-04-22T08:05:00Z",
      "exitedAt":  "2026-04-22T08:09:00Z",
      "result": "pass",
      "operator": "OP-101"
    }
    // ... SMT / REFLOW / AOI / ICT / FQC
  ],
  "materials": [
    {
      "materialLotId": "uuid",
      "materialLotNo": "SP-2024W17-001",
      "materialType": "solder_paste",
      "supplier": "ABC Solder",
      "quantity": 12.4,
      "usedAt": "2026-04-22T08:05:30Z"
    }
  ],
  "sameLotPanels": [
    { "panelNo": "PCB-20240422-LOT-001-2", "lotNo": "LOT-001", "status": "in_progress" }
  ],
  "sameMaterialPanels": [
    { "panelNo": "PCB-20240421-LOT-099-7", "lotNo": "LOT-099", "status": "completed" }
  ]
}
```

`stationTimeline` 的 `label` 在前端用 `useProfile().stations` lookup（PCB 顯示「錫膏印刷」、半導體 profile 會自動顯示對應的中文名）。

## 前端頁面（`TraceabilityPage.tsx`）

1. 入口輸入框：可貼 `panelNo`，或從 dropdown 選最近 20 張（`GET /api/trace/panels/recent`）
2. 板資訊面板：panel_no / lot_no / status / created_at
3. **6 站時間軸**：SPI → SMT → REFLOW → AOI → ICT → FQC，每站顯示進出時間 + 結果 badge
4. **使用物料批號**：列出所有 material_lots，含供應商與用量
5. **相關板列表**：
   - 同 lot：點擊可跳到該板的追溯頁
   - 同物料：橫跨不同 lot，可協助定位「物料瑕疵」型不良

## 為什麼這樣設計

- **以 panel_no 為入口**：QR Code 一掃即查，產線常見作業；不用先記 lot_no / wafer_no
- **6 站時間軸**：時間軸而不是表格，能立刻看出哪一站「停太久」或「跳站」
- **物料批號 + 同物料相關板**：當判定為「物料造成的不良」，一次抓出所有受影響的板，不用再寫 SQL
- **不過度關聯 RabbitMQ alert**：alert 已經在「異常記錄」頁；這裡只做「以板為主軸」的追溯查詢

## seed 資料

`AoiOpsDbInitializer.cs` 在啟動時：

- 為既有 wafers 補 `panel_no`
- 建立 5 個 material_lots（錫膏 / FR4 / 電容 / 主晶片 / 助焊劑）
- 為一張 demo panel 寫完整 6 站 `panel_station_log`
- 寫 `panel_material_usage` 連結
