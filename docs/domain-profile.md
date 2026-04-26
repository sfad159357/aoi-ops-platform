# Domain Profile 機制

> 為什麼需要：DB schema（`tools / lots / wafers / process_runs`）沿用半導體用語；
> 但 demo 場景可能是 **PCB SMT 產線**、也可能是 **半導體前段**。
> 加一層「語意對照 JSON」把實體名稱、站別、量測參數、選單、KPI 門檻集中在 profile 檔，
> **DB 不變、UI 與報表跟著 profile 走**。

## 檔案位置

```
shared/domain-profiles/
  pcb.json              ← 預設（PCB SMT 6 站）
  semiconductor.json    ← 半導體前段
  README.md             ← 怎麼新增第三產業
```

前端離線預設：`frontend/src/domain/fallback/pcb.json`（後端 `/api/meta/profile` 不可達時降級用）。

## Schema（節選）

```json
{
  "profile_id": "pcb",
  "display_name": "PCB SMT 產線 MES",
  "factory": { "name": "範例：華通電腦", "site": "桃園廠" },
  "entities": {
    "panel": { "table": "wafers", "label_zh": "板", "id_prefix": "PCB" },
    "lot":   { "table": "lots",   "label_zh": "工單批次" },
    "tool":  { "table": "tools",  "label_zh": "機台" }
  },
  "stations": [
    { "code": "SPI",    "label_zh": "錫膏印刷",    "seq": 1 },
    { "code": "SMT",    "label_zh": "零件貼片",    "seq": 2 },
    { "code": "REFLOW", "label_zh": "回焊爐",      "seq": 3 },
    { "code": "AOI",    "label_zh": "AOI 光學檢測","seq": 4 },
    { "code": "ICT",    "label_zh": "電性測試",    "seq": 5 },
    { "code": "FQC",    "label_zh": "最終檢驗",    "seq": 6 }
  ],
  "lines": [
    { "code": "SMT-A", "label_zh": "SMT 線 A" }
  ],
  "parameters": [
    { "code": "solder_thickness", "label_zh": "錫膏厚度", "unit": "μm", "usl": 242, "lsl": 228, "target": 235 }
  ],
  "menus": [
    { "id": "spc",   "label_zh": "SPC 統計製程管制" },
    { "id": "wo",    "label_zh": "工單管理" },
    { "id": "alarm", "label_zh": "異常記錄" },
    { "id": "trace", "label_zh": "物料追溯查詢" }
  ],
  "kpi": { "yieldGreen": 99.0, "yieldYellow": 97.0, "cpkGood": 1.33 }
}
```

## 切換機制（環境變數）

```yaml
# infra/docker/docker-compose.yml
backend:
  environment:
    - DOMAIN_PROFILE=${DOMAIN_PROFILE:-pcb}
frontend:
  environment:
    - VITE_DOMAIN_PROFILE=${DOMAIN_PROFILE:-pcb}
```

切換指令（重啟 backend / frontend 才生效）：

```bash
DOMAIN_PROFILE=semiconductor \
  docker compose -p aoiops -f infra/docker/docker-compose.yml up -d --force-recreate backend frontend
```

## 後端讀取流程

1. `Program.cs` 註冊 `DomainProfileService` 為 singleton
2. `DomainProfileService` 啟動時根據 `Domain:Profile` 設定，從 `Domain:ProfilesDirectory`（容器內為 `/src/shared/domain-profiles`）讀對應 `{profile}.json`，反序列化成 `DomainProfile` record
3. `MetaController.GET /api/meta/profile` 直接回 profile JSON
4. `SpcRulesEngine` 透過 profile 取得參數的 USL / LSL / target，避免寫死

## 前端讀取流程

1. `main.tsx` 用 `<ProfileProvider>` 包住 `<App>`
2. `useProfile` hook 啟動時 `fetch /api/meta/profile`，存入 React Context
3. 失敗時 fallback 到 `frontend/src/domain/fallback/pcb.json`
4. 所有頁面的中文文案、站別、參數選項、USL/LSL 線、KPI 門檻都從 `useProfile()` 取得

## 不被 profile 影響的部分（避免過度抽象）

- 資料表名稱（保留 `wafers`，profile 只改顯示）
- API path（`/api/trace/panel/{panelNo}` 一律用 `panel` 語境，內部讀 `wafers`）
- Kafka topic / RabbitMQ queue 名稱（基礎設施層中性）

## 新增第三個產業

1. 複製 `shared/domain-profiles/pcb.json` → `aerospace.json`
2. 改 `profile_id` / `display_name` / `factory` / `stations` / `parameters` / `menus`
3. 啟動時 `DOMAIN_PROFILE=aerospace docker compose up -d --force-recreate backend frontend`
4. 不需要改 DB schema、後端程式碼、前端組件

## CI 建議（待實作）

用 JSON Schema 在 CI 驗證所有 `*.json` 結構一致，避免新加的 profile 漏欄位導致前端 runtime crash。
