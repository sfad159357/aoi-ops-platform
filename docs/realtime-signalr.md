# 即時推播 — .NET Core SignalR

> 為什麼不是直接從 Kafka 推到瀏覽器：Kafka 是二進位協議 + 需要 consumer group 管理，瀏覽器不適合直連；
> 同時前端要同時聽 SPC / 異常 / 工單 / 物料異動，用 4 個 Hub 較好分流。
>
> 為什麼是 SignalR：與 .NET 棧一致；`IHubContext<T>` 可在任何 BackgroundService / Worker 直接 push；
> 支援 WebSocket，自動降級 SSE / Long Polling；前端 `@microsoft/signalr` 自動 reconnect。

## Hub 一覽

| Hub | Path | 訊息名稱 | Payload | 上游 worker |
|---|---|---|---|---|
| `SpcHub` | `/hubs/spc` | `spcPoint`、`spcViolation` | `SpcPointPayload` | `SpcRealtimeWorker`（Kafka `aoi.inspection.raw`） |
| `AlarmHub` | `/hubs/alarm` | `alarm` | `AlarmEvent` | `AlarmRabbitWorker`（RabbitMQ `alert`） |
| `WorkorderHub` | `/hubs/workorder` | `workorder` | `WorkorderEvent` | `WorkorderRabbitWorker`（RabbitMQ `workorder`） |

## SpcHub group 訂閱

`SpcHub` 提供 `JoinGroup(lineCode, parameterCode)` / `LeaveGroup(lineCode, parameterCode)`，
group key 為 `line:{lineCode}|param:{parameterCode}`。
前端 filter 切換時 add 新 group / remove 舊 group，避免收到不需要的點。

```csharp
// SpcHub.cs
public Task JoinGroup(string lineCode, string parameterCode) =>
    Groups.AddToGroupAsync(Context.ConnectionId, BuildGroupName(lineCode, parameterCode));

public static string BuildGroupName(string lineCode, string parameterCode) =>
    $"line:{lineCode}|param:{parameterCode}";
```

`AlarmHub` / `WorkorderHub` 目前是 broadcast 給所有連線（資料量小，未分群）。

## 訊息格式

### SpcPointPayload（`spcPoint` / `spcViolation`）

```json
{
  "lineCode": "SMT-A",
  "toolCode": "AOI-A",
  "parameterCode": "yield_rate",
  "timestamp": "2026-04-22T10:33:21Z",
  "value": 96.5,
  "mean": 97.0,
  "sigma": 0.85,
  "ucl": 99.55,
  "cl": 97.0,
  "lcl": 94.45,
  "cpk": 1.42,
  "violations": [
    { "ruleId": 1, "ruleName": "Rule1: Beyond 3σ", "severity": "high", "description": "value 跨出 ±3σ" }
  ]
}
```

`spcPoint` 永遠推（即使無違規），讓前端可以畫線；
`spcViolation` 只有違規時推，前端違規表用此事件 append。

### AlarmEvent（`alarm`）

```json
{
  "id": "uuid",
  "alarmCode": "ALM-0042",
  "alarmLevel": "high",
  "message": "Reflow temperature out of UCL",
  "triggeredAt": "2026-04-22T10:33:21Z",
  "status": "active",
  "source": "rabbitmq",
  "toolCode": "AOI-A",
  "lotNo": "LOT-001"
}
```

### WorkorderEvent（`workorder`）

```json
{
  "id": "uuid",
  "workorderNo": "WO-20260422-1234",
  "priority": "urgent",
  "status": "pending",
  "createdAt": "2026-04-22T10:33:21Z",
  "lotNo": "LOT-001",
  "severity": "high"
}
```

## 連線範例（前端）

```ts
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

const conn = new HubConnectionBuilder()
  .withUrl('http://localhost:8080/hubs/spc')
  .withAutomaticReconnect([0, 2000, 5000, 10000])
  .configureLogging(LogLevel.Warning)
  .build();

conn.on('spcPoint', (payload) => { /* append to chart */ });
conn.on('spcViolation', (payload) => { /* prepend to violation table */ });

await conn.start();
await conn.invoke('JoinGroup', 'SMT-A', 'yield_rate');
```

實際封裝在 `frontend/src/realtime/`：`signalr.ts` + `useSpcStream` / `useAlarmStream` / `useWorkorderStream`。

## 連線生命週期注意事項

- **首次連上補歷史點**：SignalR 重連歷史會掉，所以 `AlarmsPage` / `WorkordersPage` 進場時先 `GET /api/alarms?take=100` 預載再開 Hub。
- **Group 切換**：filter 改變時 leave 舊 group → join 新 group，否則會收到混合資料。
- **CORS**：後端 `Program.cs` 必須對 `/hubs/*` 套用同一條 `AllowedOrigins` 規則並開 `AllowCredentials`，否則 WebSocket handshake 會被瀏覽器擋掉。
