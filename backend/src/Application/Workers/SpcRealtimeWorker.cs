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
        IConfiguration configuration,
        ILogger<SpcRealtimeWorker> logger)
    {
        _hubBroker = hubBroker;
        _profile = profile;
        _metrics = metrics;
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
        var lineCode = InferLineCodeFromTool(evt.ToolCode);

        foreach (var parameter in _profile.Current.Parameters)
        {
            var v = ExtractParameterValue(evt, parameter.Code);
            if (v is null) continue;

            var key2 = $"{lineCode}|{evt.ToolCode}|{parameter.Code}";
            var state = _windows.GetOrAdd(key2, _ => new SpcWindowState(capacity: 25));
            var window = state.Add(v.Value);

            var payload = SpcRulesEngine.Evaluate(
                window: window,
                lineCode: lineCode,
                toolCode: evt.ToolCode,
                parameterCode: parameter.Code,
                usl: parameter.Usl,
                lsl: parameter.Lsl,
                target: parameter.Target,
                timestamp: evt.Timestamp);

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
            "yield_rate" => evt.YieldRate,
            "temperature" => evt.Temperature,
            "pressure" => evt.Pressure,
            _ => null,
        };

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

        public SpcInspectionEvent? ToEvent()
        {
            if (string.IsNullOrEmpty(ToolCode)) return null;
            return new SpcInspectionEvent(
                EventId ?? Guid.NewGuid().ToString(),
                ToolCode,
                LotNo ?? string.Empty,
                WaferNo,
                Timestamp ?? DateTimeOffset.UtcNow,
                Temperature,
                Pressure,
                YieldRate);
        }
    }
}
