// SpcRealtimeWorker：消費 Kafka aoi.inspection.raw、套八大規則 + Cpk、推 SignalR。
//
// 為什麼放在 Application 層：
// - 串接「規則引擎（純運算）」與「Hub broker（推播抽象）」屬於應用層編排。
// - 透過 ISpcHubBroker 介面，本層不需要直接相依 SignalR。
//
// 解決什麼問題：
// - 把「Kafka payload → 套規則 → 推前端」的 pipeline 集中起來，
//   後續想加 metrics / 多參數，只需改這一個 class。

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AOIOpsPlatform.Application.Domain;
using AOIOpsPlatform.Application.Hubs;
using AOIOpsPlatform.Application.Messaging;
using AOIOpsPlatform.Application.Observability;
using AOIOpsPlatform.Application.Spc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AOIOpsPlatform.Application.Workers;

/// <summary>
/// 處理 <c>aoi.inspection.raw</c> 訊息的 handler。
/// </summary>
public sealed class SpcRealtimeWorker : IKafkaMessageHandler
{
    private readonly ISpcHubBroker _hubBroker;
    private readonly DomainProfileService _profile;
    private readonly IRealtimeMetrics _metrics;
    private readonly ISpcMeasurementSink _measurementSink;
    private readonly ILogger<SpcRealtimeWorker> _logger;

    /// <summary>
    /// 為什麼用 ConcurrentDictionary 收 window：
    /// - 不同 (line, tool, parameter) 組合各自獨立 buffer；
    ///   ConcurrentDictionary 保證新增 key 時的 thread-safety，內部 SpcWindowState 自帶 lock 保證 append 不撞。
    /// </summary>
    private readonly ConcurrentDictionary<string, SpcWindowState> _windows = new();

    public SpcRealtimeWorker(
        ISpcHubBroker hubBroker,
        DomainProfileService profile,
        IRealtimeMetrics metrics,
        ISpcMeasurementSink measurementSink,
        IConfiguration configuration,
        ILogger<SpcRealtimeWorker> logger)
    {
        _hubBroker = hubBroker;
        _profile = profile;
        _metrics = metrics;
        _measurementSink = measurementSink;
        _logger = logger;
        Topic = configuration["Messaging:Kafka:TopicInspectionRaw"] ?? "aoi.inspection.raw";
    }

    public string Topic { get; }

    public async Task HandleAsync(string? key, string value, CancellationToken cancellationToken)
    {
        SpcInspectionEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<SpcInspectionEventRaw>(value, JsonOpts)?.ToEvent();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "SPC raw payload JSON 解析失敗：{Value}", Truncate(value));
            return;
        }

        if (evt is null) return;

        // 為什麼跑「所有 profile parameters」一輪：
        // - 同一筆量測訊息可能同時帶 yield_rate / temperature / pressure；
        //   依 profile 設定逐個對應、有值才送，省掉硬編 if 分支。
        // 為什麼優先用 evt.LineCode：
        // - ingestion 端已經把產線與站別寫進 payload（與 panels.tools 主檔一致），
        //   Worker 直接吃比 InferLineCodeFromTool 推斷穩定；推斷只當 fallback。
        var lineCode = !string.IsNullOrEmpty(evt.LineCode) ? evt.LineCode : InferLineCodeFromTool(evt.ToolCode);
        // 為什麼 panelNo 優先用 evt.PanelNo：
        // - ingestion 已經組好（lot_no-wafer_no），直接吃避免兩端格式不一致。
        var panelNo = !string.IsNullOrEmpty(evt.PanelNo)
            ? evt.PanelNo
            : (string.IsNullOrEmpty(evt.LotNo) ? null : $"{evt.LotNo}-{evt.WaferNo}");

        foreach (var parameter in _profile.Current.Parameters)
        {
            var v = ExtractParameterValue(evt, parameter.Code);
            if (v is null) continue;

            var key2 = $"{lineCode}|{evt.ToolCode}|{parameter.Code}";
            var state = _windows.GetOrAdd(key2, _ => new SpcWindowState(capacity: 25));
            var snap = state.Add(v.Value);
            var window = snap.Window;

            var rawPayload = SpcRulesEngine.Evaluate(
                window: window,
                lineCode: lineCode,
                toolCode: evt.ToolCode,
                parameterCode: parameter.Code,
                usl: parameter.Usl,
                lsl: parameter.Lsl,
                target: parameter.Target,
                timestamp: evt.Timestamp,
                inspectedQty: evt.InspectedQty,
                fixedCl: snap.Baseline?.Cl,
                fixedSigma: snap.Baseline?.Sigma);
            // 為什麼用 with 補 lot/wafer/station/operator：
            // - 規則引擎只做數值與違規；追溯欄位（lot/panel/station/operator）由原始事件帶入，避免引擎簽名爆炸。
            // - 前端 SPC 頁面要在點上同時顯示「機台、站別、操作員、批次、板號」，這些欄位必須一路 push 到 SignalR。
            var payload = rawPayload with
            {
                LotNo = string.IsNullOrEmpty(evt.LotNo) ? null : evt.LotNo,
                WaferNo = evt.WaferNo,
                StationCode = evt.StationCode,
                PanelNo = panelNo,
                OperatorCode = evt.OperatorCode,
                OperatorName = evt.OperatorName,
            };

            // 為什麼這裡 enqueue 一筆 spc_measurement：
            // - 即時 stream 是「易失」資料（重新整理就沒），落地後可做歷史曲線與報表；
            // - sink 內部用 Channel + batch flush，不會卡住 Kafka 處理迴圈。
            var violationCodes = payload.Violations.Count == 0
                ? null
                : string.Join(",", payload.Violations.Select(v => $"SPC-{v.RuleId}"));
            _measurementSink.Enqueue(new SpcMeasurementWriteRequest(
                LineCode: lineCode,
                ToolCode: evt.ToolCode,
                ParameterCode: parameter.Code,
                PanelNo: panelNo,
                StationCode: evt.StationCode,
                Value: (decimal)payload.Value,
                MeasuredAt: payload.Timestamp,
                IsViolation: payload.Violations.Count > 0,
                ViolationCodes: violationCodes,
                KafkaEventId: evt.EventId,
                LotNo: string.IsNullOrEmpty(evt.LotNo) ? null : evt.LotNo,
                OperatorCode: evt.OperatorCode,
                OperatorName: evt.OperatorName));

            // 為什麼從 push 前 Stopwatch.Start：
            // - W11 想量測「我們把訊息送進 SignalR 的成本」，不是 Kafka 反序列化或規則計算的成本，
            //   後者已經在前面做完；這裡只計 push 段落，可清楚對齊 hub broker 的延遲。
            var sw = Stopwatch.StartNew();
            await _hubBroker.PushSpcPointAsync(payload, cancellationToken);
            if (payload.Violations.Count > 0)
            {
                await _hubBroker.PushSpcViolationAsync(payload, cancellationToken);
            }
            sw.Stop();
            _metrics.RecordSpcPoint(payload.Violations.Count > 0, sw.Elapsed.TotalMilliseconds);
        }

        _ = key;
    }

    /// <summary>
    /// 依 parameter code 從 raw payload 抓對應欄位。
    /// </summary>
    /// <remarks>
    /// 為什麼用 switch：
    /// - Python publisher 的 payload 欄位名固定，幾個 case 即可；
    ///   未來新增 parameter 只需加 case + profile JSON 新增條目。
    /// </remarks>
    private static double? ExtractParameterValue(SpcInspectionEvent evt, string parameterCode)
        => parameterCode.ToLowerInvariant() switch
        {
            // 為什麼良率要 NormalizeYieldToRatio 而不是直接用 Raw：
            // - ingestion 模擬器送 0~100（例 97.2 = 97.2%），domain profile / SpcRulesEngine 與 JSON usl=1.0
            //   皆假設 0~1 比例；不轉會讓 UCL/CL/LCL、Cpk 與前端 (×100) 全錯、良率可顯示成 9000%+
            "yield_rate" => NormalizeYieldToRatio(evt.YieldRate),
            "temperature" => evt.Temperature,
            "pressure" => evt.Pressure,
            _ => null,
        };

    /// <summary>
    /// 將 Kafka/設備可能送出的良率（0~1 或 0~100）統一為 0~1，與 profile 及 DB 寫入慣例一致。
    /// </summary>
    /// <remarks>
    /// 解決什麼問題：避免同一欄位混用「比例」與「百分比」兩種刻度，造成 SPC 規則與儀表板同時失效。
    /// 對齊 services/ingestion process_runs：值 &gt; 1 時視為百分度並除以 100。
    /// </remarks>
    private static double? NormalizeYieldToRatio(double? v)
    {
        if (v is null) return null;
        if (v.Value > 1.0) return v.Value / 100.0;
        return v.Value;
    }

    /// <summary>
    /// 用 toolCode prefix 推斷 line code（例：SMT-A01 → SMT-A）。
    /// </summary>
    private static string InferLineCodeFromTool(string toolCode)
    {
        // 為什麼不能只用「找最後一個 '-'」：
        // - 本專案 ingestion 的 toolCode 可能是 AOI-A / AOI-B 這種「已經是產線代碼」，
        //   若硬切成 AOI 會導致 SignalR group key 不一致（前端訂閱 AOI-A、後端推 AOI），
        //   結果就是「SPC 頁面完全收不到點」。
        //
        // 這裡的策略：
        // 1) 若已符合常見 line code（例如 AOI-A / SMT-B）就原樣回傳
        // 2) 若是帶數字尾碼（例如 SMT-A01 / AOI-A12）就去掉尾碼 → SMT-A / AOI-A
        // 3) 若是 AOI-001 這種用 dash 分隔序號，就回 AOI
        if (string.IsNullOrWhiteSpace(toolCode)) return string.Empty;

        var s = toolCode.Trim();

        // case1: AOI-A / SMT-B（已經是 line code）
        if (s.Length == 5 && s[3] == '-' && char.IsLetter(s[4])) return s;

        // case2: SMT-A01 / AOI-A12 → SMT-A / AOI-A
        // 為什麼不用 Regex：避免引入額外成本；字串掃描即可。
        if (s.Length >= 6 && s[3] == '-' && char.IsLetter(s[4]))
        {
            var i = 5;
            var hasDigit = false;
            while (i < s.Length && char.IsDigit(s[i]))
            {
                hasDigit = true;
                i++;
            }
            if (hasDigit && i == s.Length)
            {
                return s[..5];
            }
        }

        // case3: AOI-001 → AOI（dash 後全是數字）
        var dash = s.IndexOf('-');
        if (dash > 0 && dash < s.Length - 1)
        {
            var ok = true;
            for (var i = dash + 1; i < s.Length; i++)
            {
                if (!char.IsDigit(s[i]))
                {
                    ok = false;
                    break;
                }
            }
            if (ok) return s[..dash];
        }

        return s;
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "…";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 對應 Python publisher payload 的內部 DTO。
    /// </summary>
    private sealed class SpcInspectionEventRaw
    {
        // 為什麼每個欄位都加 JsonPropertyName：
        // - ingestion (Python) 出來的 payload 是 snake_case（tool_code / yield_rate），
        //   System.Text.Json 的 PropertyNameCaseInsensitive 只處理大小寫，不會把 '_' 視為等價；
        // - 沒 mapping 的結果是全部欄位都維持 null → SPC 永遠不推點（前端就會看起來「沒數據進來」）。
        // - 明確標註後，能穩定把 Kafka payload 對齊到 C# DTO。
        [JsonPropertyName("event_id")]
        public string? EventId { get; set; }
        [JsonPropertyName("tool_code")]
        public string? ToolCode { get; set; }
        [JsonPropertyName("lot_no")]
        public string? LotNo { get; set; }
        [JsonPropertyName("wafer_no")]
        public int WaferNo { get; set; }
        [JsonPropertyName("timestamp")]
        public DateTimeOffset? Timestamp { get; set; }
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }
        [JsonPropertyName("pressure")]
        public double? Pressure { get; set; }
        [JsonPropertyName("yield_rate")]
        public double? YieldRate { get; set; }
        /// <summary>本則事件檢驗件數；缺省 1。與 KPI 累積產出、件/小時對帳。</summary>
        [JsonPropertyName("inspected_qty")]
        public int InspectedQty { get; set; } = 1;
        // 為什麼把以下五個欄位也吃進 DTO：
        // - 來源 ingestion 端已經寫齊，下游 SPC measurement / SignalR payload 直接用，避免兩端字串格式不一致。
        [JsonPropertyName("line_code")]
        public string? LineCode { get; set; }
        [JsonPropertyName("station_code")]
        public string? StationCode { get; set; }
        [JsonPropertyName("panel_no")]
        public string? PanelNo { get; set; }
        [JsonPropertyName("operator_code")]
        public string? OperatorCode { get; set; }
        [JsonPropertyName("operator_name")]
        public string? OperatorName { get; set; }

        public SpcInspectionEvent? ToEvent()
        {
            if (string.IsNullOrEmpty(ToolCode)) return null;
            var q = InspectedQty < 1 ? 1 : InspectedQty;
            return new SpcInspectionEvent(
                EventId ?? Guid.NewGuid().ToString(),
                ToolCode,
                LotNo ?? string.Empty,
                WaferNo,
                Timestamp ?? DateTimeOffset.UtcNow,
                Temperature,
                Pressure,
                YieldRate,
                q,
                LineCode,
                StationCode,
                PanelNo,
                OperatorCode,
                OperatorName);
        }
    }
}
