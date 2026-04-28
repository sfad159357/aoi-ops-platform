aoi-ops-platform/
  frontend/
    src/
      api/                      ← 前端 REST 客戶端
        spc.ts                  ← SPC 報表 API（批次 / 歷史，連 Python 8001）
        trace.ts                ← 物料追溯 API（連 .NET /api/trace/*）
      components/
        spc/                    ← SPC 元件（W06 重構）
          KpiBar.tsx            ← KPI（良率 / Cpk / 今日違規 / 每小時產出 / 累積產出）
          FilterBar.tsx         ← 站別 / 機台 / 參數 filter（控 SignalR group）
          ControlChartPair.tsx  ← X̄ + R 雙圖（recharts）
          ViolationTable.tsx    ← 八大規則違規清單
      domain/                   ← Domain Profile（W05）
        profile.ts              ← TS 型別
        useProfile.tsx          ← React Context + useProfile hook
        fallback/pcb.json       ← 後端不可達時的離線預設
      realtime/                 ← SignalR client（W04）
        signalr.ts              ← createHubConnection 工廠
        useSpcStream.ts
        useAlarmStream.ts
        useWorkorderStream.ts
        RealtimeStatusBadge.tsx ← Hub 連線狀態 badge
      pages/
        SpcDashboard.tsx        ← W06：4 KPI + X̄/R + 違規表 + filter
        AlarmsPage.tsx          ← W07：異常記錄（事件驅動）
        WorkordersPage.tsx      ← W07：工單管理（事件驅動）
        TraceabilityPage.tsx    ← W08：6 站時間軸 + 物料批號 + 同批次
      App.tsx
      main.tsx                  ← 包 ProfileProvider
    package.json
    vite.config.ts

  backend/                      ← ASP.NET Core 8（Clean Architecture）
    src/
      Api/                      ← REST + SignalR 進入點
        Controllers/
          AlarmsController.cs       GET /api/alarms
          WorkordersController.cs   GET /api/workorders
          TraceController.cs        GET /api/trace/panel/{panelNo}, /api/trace/panels/recent
          MetaController.cs         GET /api/meta/profile
          LotsController.cs / DefectsController.cs / HealthController.cs ...
        Hubs/                       ← SignalR Hubs（W04 / W07）
          SpcHub.cs                 ← /hubs/spc，支援 JoinGroup(line, parameter)
          AlarmHub.cs               ← /hubs/alarm
          WorkorderHub.cs           ← /hubs/workorder
        Realtime/
          SpcHubBroker.cs           ← ISpcHubBroker / IAlarmHubBroker / IWorkorderHubBroker 實作
        Program.cs                  ← DI / Hub 註冊 / hosted services / CORS
      Application/                ← 不依賴 Infrastructure
        Domain/
          DomainProfile.cs / DomainProfileService.cs   ← W05
        Hubs/
          ISpcHubBroker.cs          ← Hub broker 抽象（解耦 Application ↔ SignalR）
        Messaging/
          IMessageHandler.cs        ← IKafkaMessageHandler / IRabbitMessageHandler
        Spc/                        ← W05：SPC 引擎（C# 移植自 Python）
          SpcModels.cs              ← SpcInspectionEvent / SpcPointPayload / SpcRuleViolation
          SpcWindowState.cs         ← 25 點滑動視窗（thread-safe）
          ProcessCapability.cs      ← Ca / Cp / Cpk + 等級
          SpcRulesEngine.cs         ← 八大規則偵測
        Workers/
          SpcRealtimeWorker.cs      ← Kafka aoi.inspection.raw → SignalR /hubs/spc
      Domain/Entities/            ← 純資料模型
        Tool / Lot / Wafer (含 PanelNo) / Recipe / ProcessRun
        Alarm / Defect / DefectImage / DefectReview
        Workorder
        MaterialLot / PanelMaterialUsage / PanelStationLog   ← W08 新增
      Infrastructure/
        Data/
          AoiOpsDbContext.cs
          AoiOpsDbInitializer.cs    ← 含 W08 seed
        Messaging/
          MessagingOptions.cs       ← KafkaOptions / RabbitMqOptions
          KafkaConsumerHostedService.cs
          RabbitMqConsumerHostedService.cs
        Workers/                    ← W07
          AlarmRabbitWorker.cs      ← RabbitMQ alert  → DB + /hubs/alarm
          WorkorderRabbitWorker.cs  ← RabbitMQ workorder → DB + /hubs/workorder
    tests/
      AOIOpsPlatform.Api.Tests/
        SpcRulesEngineTests.cs      ← W05 八大規則 / Cpk 單元測試
    AOIOpsPlatform.sln

  services/                     ← Python 服務
    ingestion/                  ← AOI 設備模擬 + Kafka producer + DB writer（單一容器）
      app/__main__.py
      requirements.txt
      Dockerfile
    kafka-consumers/
      influx-writer/            ← Kafka aoi.inspection.raw → InfluxDB
        app/                    requirements.txt   Dockerfile
      rabbitmq-publisher/       ← Kafka → RabbitMQ alert / workorder
        app/                    requirements.txt   Dockerfile
    spc-service/                ← 批次 / 歷史 SPC 報表（FastAPI，port 8001）
      app/
        main.py                 ← 路由
        spc_engine.py / rules.py / demo_data.py / models.py
      requirements.txt
      Dockerfile
    rabbitmq-consumers/         ← legacy；W07 起由 .NET 接管
      db-sink/                  ← docker compose profile=legacy 才會啟動

  shared/                       ← W05
    domain-profiles/
      pcb.json                  ← 預設 profile（PCB SMT）
      semiconductor.json        ← 半導體用語
      README.md

  infra/
    docker/
      docker-compose.yml        ← SQL Server / InfluxDB / Kafka / RabbitMQ / backend / frontend / python services
    db/mssql-init/              ← SQL Server init script（建 AOIOpsPlatform_MSSQL）
    rabbitmq/                   ← rabbitmq.conf / definitions.json
    influxdb/                   ← influxdb 初始設定
    kafka/                      ← KRaft mode

  docs/
    architecture.md
    realtime-signalr.md         ← W09 新增：4 hub 訊息格式
    domain-profile.md           ← W09 新增：profile schema + 切換指南
    traceability.md             ← W09 新增：6 站時間軸資料模型 + API
    erd.md / api-spec.md / product-scope.md
    kafka-events.md / rabbitmq-routing.md

  scripts/
    dev/                        ← 一鍵啟動 / 重置 DB
    seed/                       ← 補充 seed

  README.md   project.md   graph.md   ERD.md   schedule-tracker.md
