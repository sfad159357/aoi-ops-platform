using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Controllers;

/// <summary>
/// Alarms API。
/// </summary>
/// <remarks>
/// 為什麼即時推播之外還要保留 REST：
/// - 前端剛打開頁面要先撈最近 N 筆「歷史告警」打底，
///   之後 SignalR 推進來的新事件才會無縫接續，使用者不會看到一張空白表。
/// - REST 也保留給後台 / 報表 / e2e 測試使用。
///
/// 解決什麼問題：
/// - 解決「重新整理頁面就一切歸零」的問題：
///   有 REST 載入歷史 + SignalR 推新事件，UX 就能像 SaaS 系統一樣完整。
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public sealed class AlarmsController : ControllerBase
{
    private readonly AoiOpsDbContext _db;

    public AlarmsController(AoiOpsDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// 列表回傳給前端用的 DTO。
    /// </summary>
    /// <remarks>
    /// 為什麼在這層另外定義 DTO，而不直接回 Alarm entity：
    /// - Entity 有 navigation property 與 DB column，直接序列化會洩漏內部欄位、且容易循環序列化崩潰。
    /// - DTO 把欄位收斂到「前端需要顯示什麼」，未來改 schema 也只動這層。
    /// </remarks>
    public sealed record AlarmListItemDto(
        Guid Id,
        string AlarmCode,
        string? AlarmLevel,
        string? Message,
        DateTimeOffset TriggeredAt,
        DateTimeOffset? ClearedAt,
        string? Status,
        string? Source,
        string? ToolCode);

    /// <summary>
    /// 取得近期告警清單，預設回最近 100 筆。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AlarmListItemDto>>> List(
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        // 為什麼 take 上限固定為 500：
        // - 防止 client 傳入超大數字打爆 DB / 前端；
        // - 真要分頁可在 W11 補 cursor 或 offset。
        take = Math.Clamp(take, 1, 500);

        var items = await _db.Alarms
            .AsNoTracking()
            .OrderByDescending(x => x.TriggeredAt)
            .Take(take)
            .Join(_db.Tools.AsNoTracking(), a => a.ToolId, t => t.Id, (a, t) => new AlarmListItemDto(
                a.Id,
                a.AlarmCode,
                a.AlarmLevel,
                a.Message,
                a.TriggeredAt,
                a.ClearedAt,
                a.Status,
                a.Source,
                t.ToolCode))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}
