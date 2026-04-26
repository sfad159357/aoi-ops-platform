# Clean Architecture 分層指南（Domain / Application / Infrastructure）

> 這份文件用本 repo 的實作語境，解釋 Clean Architecture 三層各自的職責、依賴方向，以及常見「不 Clean」的反例。
> 目的：讓你在做功能時知道「程式應該放哪裡」，也能在面試時用一致的語言描述架構決策。

---

## 1) 三層在解決什麼問題？

Clean Architecture 想解決的是：

- **可維護**：需求變動時，不會牽一髮動全身
- **可測試**：核心規則不用真的連 DB / Kafka / RabbitMQ 也能測
- **可替換**：把 PostgreSQL 換成別的 DB、把 Kafka client 換版本，核心邏輯不用重寫

核心原則只有一句話：

> **依賴方向永遠指向「內圈」（Domain 最內層）**  
> 外圈可以依賴內圈，內圈不可以反過來依賴外圈。

---

## 2) 各層的上下關係（依賴方向）

在這個 repo（含 Api）可以用下圖理解：

```
        外圈（Framework / IO）
 ┌──────────────────────────┐
 │ Api (ASP.NET Core)        │  Controllers / SignalR Hubs / DI 組裝
 └───────────▲──────────────┘
             │ depends on
 ┌───────────┴──────────────┐
 │ Infrastructure            │  EF Core / Kafka / RabbitMQ / File IO / 外部系統
 └───────────▲──────────────┘
             │ depends on
 ┌───────────┴──────────────┐
 │ Application               │  Use cases / Workers / Ports(interfaces)
 └───────────▲──────────────┘
             │ depends on
 ┌───────────┴──────────────┐
 │ Domain                    │  Entities / Value Objects / 規則（最穩定）
 └──────────────────────────┘
```

> 為什麼 `Infrastructure → Application` 是允許的：  
> Application 會定義「抽象介面」（ports），Infrastructure 只是在實作它（adapters）。  
> 這符合「依賴指向內圈」：外圈的實作依賴內圈的抽象。

---

## 3) Domain（最內圈）

### 3.1 放什麼？

- **最穩定、最不該被 IO 影響的東西**
- 常見包含：
  - `Entities`（例如 `Alarm`, `Workorder`, `ProcessRun`…）
  - `Value Objects`（例如規格上下限、狀態列舉）
  - **製程/品質規則的語意模型**

### 3.2 不能放什麼？

- EF Core `DbContext` / `DbSet`
- Kafka/RabbitMQ client
- HTTP、SignalR、Controller
- 任何「需要連線」或「跟框架綁死」的東西

> 判斷口訣：  
> Domain 應該可以被搬到另一個專案/語言，仍然保有意義。

---

## 4) Application（用例層 / 規則編排層）

### 4.1 放什麼？

Application 解決的是「**我要完成一個用例（Use case）**，需要哪些步驟、哪些規則」。

在本 repo 常見放：

- **Use cases / Services**：例如 Domain Profile 的讀取/提供、SPC 規則引擎的 API
- **Ports（介面/抽象）**：讓外圈實作
  - 例如 `IKafkaMessageHandler`, `IRabbitMessageHandler`
  - 例如 `IRealtimeMetrics`（收集推播延遲/計數的抽象）
- **Workers（用例導向的處理器）**
  - 例如 `SpcRealtimeWorker`（它描述「收到 Kafka 點 → 跑規則 → 推播」這個用例）

### 4.2 不能放什麼？

- 具體的資料庫實作（SQL、EF Core 的查詢細節）
- 具體的 Kafka/RabbitMQ 連線細節
- 具體的 HTTP/SignalR endpoint 定義

> Application 可以「說它需要 DB」，但要用 **介面** 表達，不能直接依賴 DB 的具體技術。

---

## 5) Infrastructure（外圈實作 / IO 層）

### 5.1 放什麼？

Infrastructure 解決的是「**把外部世界接進來**」：

- EF Core：`DbContext`、Repository（若有）、Migration/EnsureCreated 等
- Kafka/RabbitMQ：consumer/publisher、連線設定、retry、ack/nack
- 檔案/環境：讀 JSON profile、讀設定、呼叫外部 API

在本 repo 的例子：

- `AoiOpsDbContext`、`AoiOpsDbInitializer`
- `KafkaConsumerHostedService`、`RabbitMqConsumerHostedService`
- `AlarmRabbitWorker` / `WorkorderRabbitWorker`（它們依賴 `DbContext`，因此放 Infrastructure 合理）

### 5.2 可以依賴什麼？

- 可以依賴 `Application`（因為要實作 Application 定義的 ports / 介面）
- 可以依賴 `Domain`
- **不應被 Domain/Application 反向依賴**

---

## 6) Api（Framework 層 / 組裝層，通常算最外圈）

Api 通常不是 Clean Architecture 的三層之一，但實務上一定會存在：

- ASP.NET Core pipeline
- Controllers / SignalR Hubs
- DI 註冊（把 Application ports 對到 Infrastructure adapters）

> Api 的核心任務：**把 request 轉成 use case input**，把 use case output 轉成 response。  
> Api 不應該承擔重的商業邏輯（會讓測試/維護變差）。

---

## 7) 什麼叫「不 Clean」？（常見反例）

以下狀況通常代表分層破壞、後續維護成本會上升：

### 7.1 Domain 依賴外圈（最糟）

- Domain 直接 `using Microsoft.EntityFrameworkCore;`
- Entity 裡面出現 `DbContext.SaveChanges()`

**為什麼不 Clean**：  
你把「業務語意」跟「特定框架」綁死，未來換 DB/框架時，核心規則也得跟著改。

### 7.2 Application 直接 new 外部 client / 直接寫 SQL

- Application 直接 `new NpgsqlConnection(...)`
- Application 直接 `new ConsumerBuilder<...>()`（Kafka client 具體實作）

**為什麼不 Clean**：  
Use case 變成「被技術細節綁死」，測試也會被迫要起 DB/Kafka 才能跑。

### 7.3 Api Controller 做太多事

- Controller 裡面直接寫 200 行「規則判斷 + DB 存取 + 推播」

**為什麼不 Clean**：  
Controller 會變成上帝類別，重構困難；也很難讓相同用例被 background worker 重用。

### 7.4 Infrastructure 回傳外圈型別到內圈

- Infrastructure 回傳 `IActionResult`、`HttpResponseMessage` 給 Application

**為什麼不 Clean**：  
這代表外圈型別滲透到內圈，導致內圈也必須依賴外圈套件。

---

## 8) 快速判斷：我這段 code 應該放哪一層？

用 4 個問題快速分類：

1. **它是否描述「業務語意/規則」且不需要 IO？**  
   → 放 **Domain**
2. **它是否描述「用例流程/編排」並依賴抽象介面？**  
   → 放 **Application**
3. **它是否是「跟外部世界接起來」的具體技術細節？**  
   → 放 **Infrastructure**
4. **它是否是「HTTP/SignalR 端點」或 DI 組裝？**  
   → 放 **Api**

---

## 9) 本 repo 的具體對照（讓你能對號入座）

| 需求/元件 | 放哪層 | 為什麼 |
|---|---|---|
| SPC 規則引擎（8 rules / Cpk） | Application（規則計算） + Domain（語意模型/資料結構） | 規則是核心；不該被 Kafka/DB 綁住 |
| Kafka consume 迴圈（連線、subscribe、error handler） | Infrastructure | 這是 IO/SDK 細節 |
| `IKafkaMessageHandler` / `IRabbitMessageHandler` | Application | ports（抽象）應在內圈定義 |
| EF Core `DbContext` | Infrastructure | 具體資料庫技術 |
| Controllers / SignalR Hubs | Api | Web 端點層 |

---

## 10) 補充：這次「API 全掛但沒有 500」跟分層有關嗎？

本質上不是分層破壞，而是 **Hosted Service 的啟動行為踩到了 ASP.NET Core host 生命周期**：

- `BackgroundService.ExecuteAsync` 若沒有先 `await`，可能在啟動階段同步卡住，導致 **Kestrel 沒有 listen**

對應除錯紀錄請看：

- [Troubleshooting：Backend API 全掛（沒有 500）](troubleshooting-backend-api-down.md)

