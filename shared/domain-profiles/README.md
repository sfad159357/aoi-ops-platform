# Domain Profiles

> 同一份 codebase 透過 JSON profile 切換 PCB / 半導體 / 其他產業的 UI 與標籤。

## 為什麼存在

不同工廠 demo 需要把「板 / 晶圓」「站別」「量測參數」換成該產業的常用詞，
但底層 schema (`tools / lots / wafers / process_runs`) 不應該為了名詞跟著動。
這個資料夾就是用來放每個產業專屬的「語意對照表」。

## 結構

```
shared/domain-profiles/
  pcb.json              # 預設：PCB SMT 產線 MES
  semiconductor.json    # 半導體保留原語境
  README.md             # 本文件
```

每份 profile 必填欄位：

| key            | 說明 |
| -------------- | ---- |
| `profile_id`   | 唯一識別字（與檔名相同） |
| `display_name` | 前端 page header 顯示用 |
| `factory`      | demo 標題顯示的工廠名 / 廠區 |
| `entities`     | `panel / lot / tool` 對應的 DB table 與顯示標籤 |
| `stations`     | 站別清單 + 顯示順序，用於 traceability 時間軸 |
| `lines`        | 產線代碼，給 SPC dashboard filter |
| `parameters`   | SPC 參數清單，含 USL / LSL / target |
| `menus`        | 左側 / 上方選單字串 |
| `kpi`          | 4 個 dashboard KPI 卡的閾值 |
| `wording`      | 散落在 UI 的單行 wording（板號 / 工單號…） |

## 切換方式

由環境變數 `DOMAIN_PROFILE` 控制：

```bash
# 預設
docker compose -p aoiops -f infra/docker/docker-compose.yml up -d

# 切到半導體
DOMAIN_PROFILE=semiconductor docker compose -p aoiops -f infra/docker/docker-compose.yml up -d
```

後端 (`DomainProfileService`) 啟動載入 `shared/domain-profiles/{DOMAIN_PROFILE}.json`，
透過 `GET /api/meta/profile` 給前端 fetch。
前端啟動後把 profile 放進 React Context，所有頁面從 context 取文案。

## 新增第三產業

1. 複製 `pcb.json` 改成 `<your-profile>.json`，所有欄位填好。
2. 確保 `parameters[].code` 跟 ingestion 端的 payload key 對得上（例如 `solder_thickness` ↔ Python publisher 寫入的欄位）。
3. 部署或啟動容器時設定 `DOMAIN_PROFILE=<your-profile>`。

> 注意：不會自動幫你改資料表名稱；`entities` 只影響「顯示」與「對外 API 路徑語意」，
> 內部 schema 永遠是 `wafers / lots / tools / process_runs`。
