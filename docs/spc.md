## SPC（Statistical Process Control）技術與原理文件

本文件目標是把 **SPC 的統計原理** 與 **本專案的落地方式** 串在一起，讓你能回答兩個問題：

- **為什麼要做 SPC**：不是只畫圖，而是用統計規則提早抓到製程偏移，降低不良擴散。
- **這個專案怎麼做 SPC**：資料從哪裡來、怎麼算、怎麼做告警優先級、怎麼顯示在 Dashboard。

---

## 系統設計：資料從哪裡來（你希望的「後者」）

### 目標資料流（建議順序：先 DB，再事件流）

1. **DB Live 模式（先做、最穩）**
   - 來源：PostgreSQL `process_runs`（已有 `temperature / pressure / yield_rate`）
   - 優點：落地快、資料一致、容易做篩選（tool/lot/時間區間）
   - SPC Service 做的事：**從 DB 拉序列 → 算管制圖 + 八大規則 + Ca/Cp/Cpk → 回前端**

2. **事件流模式（Kafka，已套用）**
   - 來源：設備/模擬器 → Kafka → Consumer 落 DB（或寫 InfluxDB）
   - 重點：批次 / 歷史 SPC 報表 **走 PostgreSQL / InfluxDB 查詢**（避免 offset、重放、補點等一致性問題）
   - 即時 SPC 監控由 .NET `SpcRealtimeWorker` 直接消費 Kafka `aoi.inspection.raw`，計算後推 SignalR `/hubs/spc`。前端不直接吃 Kafka。

### 目前狀態（本 repo 到目前這一步）

- **Demo 模式**：前端/後端都有 demo data，用於 UI 展示與開發驗收（不是最終資料來源）。
- **你要的 Live**：下一步要把 SPC Service 接到 DB（PostgreSQL）或 TSDB（InfluxDB）。

---

## SPC 基礎：規格線 vs 製程管制線

### 規格線（Spec）

- **USL / LSL / Target**：由產品設計/客戶規格決定，屬於「可不可以交貨」。
- 規格寬度：\(USL - LSL\)

### 管制線（Control Limits）

- **UCL / CL / LCL**：由製程歷史數據估計，屬於「製程有沒有失控」。
- 一般使用 \(CL \pm 3\sigma\)（\(\sigma\) 是製程變異的估計）

> 重要觀念：**超規格不一定失控、失控也不一定超規格**。  
> SPC 的價值在於「在超規格前」就抓到趨勢與偏移。

---

## 計量型管制圖（Variable Charts）

### 1) Xbar-R（子群平均與全距）

#### 適用情境
- 每次抽樣會取 **n=2~10** 個量測值（子群）
- 例如：每批/每片取 5 點厚度、5 點電阻

#### 計算流程（核心）

1. 把資料切成子群（每組 n 個）
2. 每組算：
   - \(\\bar{X}_i\)：第 i 組平均
   - \(R_i\)：第 i 組全距（max-min）
3. 再算：
   - \(\\bar{\\bar{X}}\)：所有子群平均的平均（中心線）
   - \(\\bar{R}\)：所有全距的平均
4. 用常數（AIAG 表）算管制線：
   - \(UCL_{\\bar{X}} = \\bar{\\bar{X}} + A_2\\bar{R}\)
   - \(LCL_{\\bar{X}} = \\bar{\\bar{X}} - A_2\\bar{R}\)
   - \(UCL_R = D_4\\bar{R}\)
   - \(LCL_R = D_3\\bar{R}\)
5. 估計製程標準差（用於能力指數與 zone 判斷）：
   - \(\\hat{\\sigma}_{process} = \\bar{R}/d_2\)
   - \(\\hat{\\sigma}_{\\bar{X}} = \\hat{\\sigma}_{process}/\\sqrt{n}\)

> 為什麼不用樣本標準差直接算：  
> Xbar-R 用 \(\\bar{R}/d_2\) 估計 \(\sigma\)，通常更符合「短期製程變異」的習慣做法。

### 2) I-MR（Individuals + Moving Range）

#### 適用情境
- 子群大小 = 1（每次只有一個值）
- 例如：每片晶圓的平均厚度、每批次的平均良率

#### 計算流程

1. \(MR_i = |X_i - X_{i-1}|\)
2. \(\\bar{X}\) 與 \(\\overline{MR}\)
3. \(\\hat{\\sigma} = \\overline{MR} / d_2\)（\(d_2=1.128\)，n=2）
4. 管制線：
   - \(UCL_X = \\bar{X} + 3\\hat{\\sigma}\)
   - \(LCL_X = \\bar{X} - 3\\hat{\\sigma}\)
   - \(UCL_{MR} = D_4\\overline{MR}\)（\(D_4=3.267\)）
   - \(LCL_{MR} = D_3\\overline{MR}\)（\(D_3=0\)）

---

## 計數型管制圖（Attribute Charts）

### P 圖（不良率）

- \(p_i = d_i/n_i\)
- \(\\bar{p} = \\sum d_i / \\sum n_i\)
- \(UCL = \\bar{p} + 3\\sqrt{\\bar{p}(1-\\bar{p})/n}\)（n 可用平均樣本數做展示）
- \(LCL = \\max(0, \\bar{p} - 3\\sqrt{\\bar{p}(1-\\bar{p})/n})\)

### Np 圖（不良品數，樣本大小固定）

- \(\\overline{np} = n\\bar{p}\)
- \(\sigma_{np} = \\sqrt{n\\bar{p}(1-\\bar{p})}\)
- \(UCL/LCL = \\overline{np} \\pm 3\sigma_{np}\)

### C 圖（缺陷數，固定單位）

- \(\\bar{c} = mean(c_i)\)
- \(\sigma_c = \\sqrt{\\bar{c}}\)
- \(UCL/LCL = \\bar{c} \\pm 3\\sqrt{\\bar{c}}\)

### U 圖（單位缺陷數，樣本大小可變）

- \(u_i = c_i/n_i\)
- \(\\bar{u} = \\sum c_i / \\sum n_i\)
- \(\sigma_u = \\sqrt{\\bar{u}/n}\)（n 可用平均樣本數做展示）
- \(UCL/LCL = \\bar{u} \\pm 3\\sqrt{\\bar{u}/n}\)

---

## 八大規則（Western Electric Rules）與 MES 優先級

本專案把規則輸出為 `violations`，每條含：
- **rule_id**：1~8
- **severity**：`red` / `yellow` / `green`（對應處理優先級）
- **points**：觸發索引（0-based）

### 規則與嚴重程度對照

- **Rule 1**：1 點超 ±3σ → `red`（最高）
- **Rule 2**：連 7 點同側 → `yellow`（高）
- **Rule 3**：連 6 點上升/下降 → `yellow`（高）
- **Rule 4**：連 14 點上下交替 → `green`（中）
- **Rule 5**：3 點中 2 點超 ±2σ（同側）→ `yellow`（高）
- **Rule 6**：5 點中 4 點超 ±1σ（同側）→ `green`（中）
- **Rule 7**：連 15 點在 ±1σ 內 → `green`（低，異常穩定）
- **Rule 8**：連 8 點無點在 ±1σ 內 → `green`（中）

> MES 的常見處理策略：  
> `red` 立即處理（停機/切批）→ `yellow` 優先追因（設備/材料/環境）→ `green` 監控與分層分析。

---

## 製程能力指數（Ca / Cp / Cpk）

### 1) Ca（準確度：中心對不對）

- 規格中心：\(C = (USL+LSL)/2\)
- 半寬：\(W = (USL-LSL)/2\)
- \(Ca = (\\bar{X}-C)/W\)

> \(Ca\) 的正負號：  
> 正值代表均值偏高（靠近 USL），負值代表偏低（靠近 LSL）。

### 2) Cp（精密度：波動大不大）

- \(Cp = (USL-LSL)/(6\sigma)\)

### 3) Cpk（綜合：位置＋寬度）

- \(Cpu = (USL-\\bar{X})/(3\sigma)\)
- \(Cpl = (\\bar{X}-LSL)/(3\sigma)\)
- \(Cpk = \\min(Cpu, Cpl)\)

### 等級（本專案輸出 grade）

- A+：Cpk ≥ 1.67
- A：1.33 ≤ Cpk < 1.67
- B：1.00 ≤ Cpk < 1.33
- C：0.67 ≤ Cpk < 1.00
- D：Cpk < 0.67

---

## 本專案的 SPC Service（FastAPI）怎麼用

### 啟動

- 本機：
  - `uvicorn app.main:app --host 0.0.0.0 --port 8001 --reload`
- Swagger：
  - `http://localhost:8001/docs`

### Demo API（只用於展示/測試）

- `GET /api/spc/demo/xbar-r` → 取得 demo payload
- `POST /api/spc/xbar-r`（帶 payload）→ 回傳完整分析結果

### Live（你希望的真資料）

建議規格是新增類似：
- `GET /api/spc/live/process-runs/imr?tool_code=T001&metric=temperature&limit=60`
- `GET /api/spc/live/tools`（供前端下拉）

資料來源：PostgreSQL `process_runs`（或未來切到 InfluxDB bucket）

---

## 為什麼不直接「從 Kafka 即時計算 SPC」

如果直接吃 Kafka 做 SPC，會遇到：
- **重放/重試**：同一筆事件可能被消費兩次，統計結果會飄
- **延遲到達/順序問題**：趨勢規則需要正確順序
- **缺值補齊**：時間序列有洞時，SPC 需要定義補值策略

因此建議是：
- Kafka 只負責 **輸送**
- DB/TSDB 負責 **落地、排序、查詢**
- SPC Service 專注 **統計與規則**

---

## 延伸：怎麼把 Kafka/RabbitMQ 接進來（不改 SPC API）

你只要做到一件事：

- **把事件流資料最後落到同一張量測表（或同一套欄位）**

例如：
- Kafka Consumer 把 `tool_code + metric_name + value + ts` 寫到 `tool_measurements`
- SPC Service 的 Live 查詢改成查 `tool_measurements`
- 前端完全不需要變

> v2 即時 SPC 已改為 .NET 直接從 Kafka 消費 → SignalR push（見 [realtime-signalr.md](realtime-signalr.md)）；
> Python `spc-service` 保留作 **批次 / 歷史 SPC 報表**。

---

## 附錄：本專案目前相關檔案位置

- 即時 SPC（C#）
  - `backend/src/Application/Spc/SpcRulesEngine.cs`：八大規則 + UCL/CL/LCL
  - `backend/src/Application/Spc/SpcWindowState.cs`：滑動視窗
  - `backend/src/Application/Spc/ProcessCapability.cs`：Ca/Cp/Cpk + 等級
  - `backend/src/Application/Workers/SpcRealtimeWorker.cs`：Kafka consumer + SignalR push
- 批次 / 歷史 SPC（Python）
  - `services/spc-service/app/spc_engine.py`：管制圖計算核心
  - `services/spc-service/app/rules.py`：八大規則
  - `services/spc-service/app/main.py`：API 路由
- 前端 Dashboard
  - `frontend/src/pages/SpcDashboard.tsx`
  - `frontend/src/components/spc/*`

