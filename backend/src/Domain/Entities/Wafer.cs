namespace AOIOpsPlatform.Domain.Entities;

/// <summary>
/// Wafer（晶圓）資料。
/// 目的：對應 ERD 的 lots -> wafers 關聯，支援 wafer_no 細查與後續缺陷定位。
/// </summary>
public sealed class Wafer
{
    public Guid Id { get; set; }

    public Guid LotId { get; set; }

    public string WaferNo { get; set; } = null!;

    public string? Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

