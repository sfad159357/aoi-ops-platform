aoi-ops-platform/
  frontend/
    src/
      components/
        spc/            ← SPC 元件（新增）
          ControlChart.tsx         ← 通用管制圖（UCL/CL/LCL + 違規高亮）
          ProcessCapabilityCard.tsx ← Ca/Cp/Cpk 指數卡片
          RulesViolationTable.tsx  ← 八大規則違規清單
      pages/
        SpcDashboard.tsx  ← SPC 主頁面（新增，含 Xbar-R/I-MR/P/C 四個頁籤）
      hooks/            ← 自訂 React hooks（useLotsApi、useDefectsApi…）
      api/
        spc.ts          ← SPC API 客戶端 + DEMO_* 常數（新增）
    public/
    package.json
    vite.config.ts

  backend/
    src/
      Api/              ← Controller、Middleware、Program.cs（組裝進入點）
      Application/      ← UseCase / Service（業務流程，不依賴 DB 細節）
      Domain/           ← Entity、ValueObject、業務規則（最純粹的一層）
      Infrastructure/
        Data/           ← EF Core DbContext、Repository 實作
        Mqtt/           ← MQTT subscriber（訂閱 Mosquitto，解析訊息）
        TimeSeries/     ← InfluxDB 寫入邏輯（讀取 yield_trend / tool_metrics）
        Messaging/      ← RabbitMQ / Kafka 整合（可選：若後端需直接消費）
    tests/
    AOIOpsPlatform.sln

  services/
    data-simulator/
      app/
        mqtt_publisher.py     ← 核心：模擬 AOI 設備透過 MQTT 發送數據
        scenario/             ← 正常 / 異常 / 漂移 / 誤判情境定義
      requirements.txt
      Dockerfile

    kafka-consumers/          ← 新增：Kafka Consumer Workers（三個獨立消費群）
      influx-writer/          ← Consumer Group A：寫 InfluxDB 時序資料
        app/
          consumer.py         ← 從 aoi.inspection.raw 消費 → 寫 tool_metrics / yield_trend
        requirements.txt
        Dockerfile
      rabbitmq-publisher/     ← Consumer Group B：異常事件路由至 RabbitMQ
        app/
          consumer.py         ← 判斷 severity → publish to RabbitMQ exchange
          router.py           ← alert queue / workorder queue 路由邏輯
        requirements.txt
        Dockerfile
      db-writer/              ← Consumer Group C：業務資料寫入 PostgreSQL
        app/
          consumer.py         ← 從 aoi.inspection.raw 消費 → 寫 process_runs / defects
        requirements.txt
        Dockerfile

    spc-service/              ← 新增：SPC 統計製程管制計算服務（port 8001）
      app/
        main.py               ← FastAPI 進入點（CORS + 所有 /api/spc/* 路由）
        models.py             ← Pydantic 輸入/輸出模型（對齊前端型別）
        spc_engine.py         ← 計算引擎（Xbar-R/I-MR/P/Np/C/U + Ca/Cp/Cpk）
        rules.py              ← 八大規則偵測（Western Electric Rules）
        demo_data.py          ← Demo 資料產生器（固定 seed，可重現）
      requirements.txt
      Dockerfile

  infra/
    docker/
      docker-compose.yml      ← 包含：PostgreSQL / InfluxDB / Mosquitto / Kafka / RabbitMQ
                                        / Backend / Frontend / Python Services
    db/
      init/                   ← Postgres init script（建表 + seed，第一次啟動自動跑）
      migrations/             ← EF Core migration 檔案
    mqtt/
      mosquitto.conf          ← Broker 設定（含 MQTT Bridge 到 Kafka 的 bridge 設定）
    kafka/
      server.properties       ← KRaft mode 設定（新增）
    rabbitmq/
      rabbitmq.conf           ← RabbitMQ 設定（新增）
      definitions.json        ← 預建 exchange / queue 設定（新增）
    influxdb/
      influxdb.conf           ← InfluxDB 初始設定（新增）

  docs/
    architecture.md           ← 架構總覽
    erd.md                    ← 資料表定義（PostgreSQL + InfluxDB measurement）
    api-spec.md               ← REST API 規格
    product-scope.md          ← 功能邊界
    mqtt-flow.md              ← MQTT 資料流（設備 → Mosquitto）
    kafka-events.md           ← Kafka topic / payload 格式（新增）
    rabbitmq-routing.md       ← RabbitMQ exchange / queue 路由設計（新增）

  scripts/
    dev/                      ← 開發用一鍵腳本（起服務、重置 DB…）
    seed/                     ← 手動 seed 腳本（補充 init script 之外的測試資料）

  README.md
