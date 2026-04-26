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
        var lastDash = toolCode.LastIndexOf('-');
        if (lastDash <= 0) return toolCode;
        // SMT-A01 → SMT-A；AOI-001 → AOI；中間可能有兩段
        var firstDash = toolCode.IndexOf('-');
        if (firstDash == lastDash) return toolCode[..firstDash];
        return toolCode[..lastDash];
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
        public string? EventId { get; set; }
        public string? ToolCode { get; set; }
        public string? LotNo { get; set; }
        public int WaferNo { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public double? Temperature { get; set; }
        public double? Pressure { get; set; }
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
