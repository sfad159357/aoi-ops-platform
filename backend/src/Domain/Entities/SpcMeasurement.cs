namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// SPC 量測值：每一筆 (panel, tool, parameter) 對應一個觀測點。
/// </summary>
/// <remarks>
/// 為什麼新增這張表：
/// - 原本 SPC 只有 Kafka stream → SignalR 推前端，DB 完全沒落地；
///   重新整理頁面就重置、跑批與稽核都做不了。
/// - 落地後可在前端做「歷史曲線」「跨日報表」「離線重算 Cpk」。
///
/// 為什麼大量冗餘可讀欄位（panel_no / tool_code / line_code / station_code / parameter_code）：
/// - SPC 報表最常依產線 / 機台 / 站別 / 參數聚合，
///   join 太多張表會拖慢 dashboard；冗餘字串欄位 + 對應 index 可以讓查詢直接走 covering index。
/// - 這些業務鍵幾乎不會被改寫（一旦量測完就是事實），冗餘的維護成本極低。
///
/// 為什麼 is_violation / violation_codes 預先算好：
/// - 違規規則由 Worker 計算，順手寫 DB 就不用之後重跑；
/// - 前端「歷史違規」查詢可直接 WHERE is_violation = 1，不用反序列化規則。
/// </remarks>
public sealed class SpcMeasurement
{
    public Guid Id { get; set; }

    /// <summary>所屬板（FK→panels.id），可選：純機台層級量測也可能沒對應板。</summary>
    public Guid? PanelId { get; set; }

    /// <summary>所屬機台（FK→tools.id）。</summary>
    public Guid ToolId { get; set; }

    /// <summary>所屬參數（FK→parameters.id）。</summary>
    public Guid ParameterId { get; set; }

    public string? PanelNo { get; set; }
    public string? LotNo { get; set; }
    public string ToolCode { get; set; } = null!;
    public string LineCode { get; set; } = null!;
    public string? StationCode { get; set; }
    public string ParameterCode { get; set; } = null!;

    /// <summary>冗餘：當下作業員 OperatorCode，便於 SPC 異常追責。</summary>
    public string? OperatorCode { get; set; }

    /// <summary>冗餘：當下作業員顯示名稱。</summary>
    public string? OperatorName { get; set; }

    /// <summary>量測值（與 parameter.unit 對應）。</summary>
    public decimal Value { get; set; }

    /// <summary>事件時間。</summary>
    public DateTimeOffset MeasuredAt { get; set; }

    /// <summary>是否違規（任一規則觸發）。</summary>
    public bool IsViolation { get; set; }

    /// <summary>違規規則 id 清單，逗號分隔字串，例如 "SPC-1,SPC-3"。</summary>
    public string? ViolationCodes { get; set; }

    /// <summary>對應 Kafka event id，方便追溯這筆量測值是哪則訊息產生的。</summary>
    public string? KafkaEventId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Panel? Panel { get; set; }
    public Tool? Tool { get; set; }
    public Parameter? Parameter { get; set; }
}
