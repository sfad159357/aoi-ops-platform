namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Workorder（舊名，已淘汰）。
/// </summary>
/// <remarks>
/// 為什麼保留這個 class：
/// - 早期 migrations / snapshot 仍引用 `AOIOpsPlatform.Domain.Entities.Workorder`；
///   直接刪除會導致 migrations 專案無法編譯與新增新 migration。
/// - 實際業務語意已改為 `Ncr`（不良單/異常處置單），而「生產工單/製令」由 `ProductionWorkOrder` 承接。
///
/// 重要：
/// - DbContext 已不再映射此 entity（不會生成/使用 workorders 表）；
///   這個 class 僅為了 migrations 的歷史編譯相容性。
/// </remarks>
public sealed class Workorder
{
    public Guid Id { get; set; }
}

