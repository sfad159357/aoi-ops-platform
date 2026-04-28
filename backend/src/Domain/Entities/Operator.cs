namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Operator（作業員 / 人員）主檔：對應現場 OP / Leader / 工程師。
/// </summary>
/// <remarks>
/// 為什麼要把人員獨立成表：
/// - alarms / workorders / defects / panel_station_log 都會記錄「誰在現場」；
///   把人員字串散在各表會造成同一個人有多個拼法（OP-001 / op-001 / 老王），追溯時對不上。
/// - 人員是穩定主檔，且常被報表「依責任人聚合」，獨立表 + FK 是 MES 標準做法。
///
/// 為什麼仍只用字串 OperatorCode 當業務鍵：
/// - 班別輪替時帳號制度通常以 OP-001 識別；
///   子表冗餘 operator_code 字串就能直接顯示，不必再 JOIN operators。
/// </remarks>
public sealed class Operator
{
    public Guid Id { get; set; }

    /// <summary>作業員代碼，例如 OP-001 / LEADER-A / ENG-005，全廠唯一。</summary>
    public string OperatorCode { get; set; } = null!;

    /// <summary>顯示名稱（中文姓名），例如「王小明」。</summary>
    public string OperatorName { get; set; } = null!;

    /// <summary>角色：operator（作業員）/ leader（線長）/ engineer（工程師）/ qc（品保）。</summary>
    public string Role { get; set; } = null!;

    /// <summary>班別：A / B / C 三班；NULL 表示行政班。</summary>
    public string? Shift { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
