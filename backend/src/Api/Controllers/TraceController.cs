using AOIOpsPlatform.Application.Domain;
using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Controllers;

/// <summary>
/// 物料追溯 API：對外用 panel_no 當入口。
/// </summary>
/// <remarks>
/// 為什麼把所有 wafer 用語改成 panel：
/// - 系統聚焦 PCB 高階製程後，wafers 表已重新命名為 panels；
///   controller 直接吃 panel.PanelNo 不再需要心算 wafer ↔ panel。
///
/// 為什麼仍保留 profile.Stations 的 enrich 動作：
/// - 站別中文 label / 序號是「設定」而非 transaction 資料；
///   它的單一真相在 profile JSON，DB stations 表只是為了 FK 一致；
///   控制器在這裡 enrich 顯示用欄位，避免前端再 lookup 一次。
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public sealed class TraceController : ControllerBase
{
    private readonly AoiOpsDbContext _db;
    private readonly DomainProfileService _profileService;

    public TraceController(AoiOpsDbContext db, DomainProfileService profileService)
    {
        _db = db;
        _profileService = profileService;
    }

    public sealed record PanelInfoDto(
        string PanelNo,
        string LotNo,
        string? Status,
        DateTimeOffset CreatedAt);

    public sealed record StationLogDto(
        string StationCode,
        string StationLabel,
        int Seq,
        DateTimeOffset EnteredAt,
        DateTimeOffset? ExitedAt,
        string? Result,
        string? Operator,
        // 為什麼補 OperatorName / ToolCode：
        // - 前端時間軸要顯示「OP-001 王小明 / SMT-A01」這種人機並列文字，
        //   而 panel_station_log 已冗餘儲存兩者，DTO 拋出零成本。
        string? OperatorName,
        string? ToolCode,
        string? Note);

    public sealed record MaterialLotDto(
        string MaterialLotNo,
        string MaterialType,
        string? MaterialName,
        string? Supplier,
        DateTimeOffset? ReceivedAt,
        decimal? Quantity);

    public sealed record RelatedPanelDto(
        string PanelNo,
        string LotNo,
        string? Status,
        DateTimeOffset CreatedAt);

    public sealed record PanelTraceDto(
        PanelInfoDto Panel,
        IReadOnlyList<StationLogDto> Stations,
        IReadOnlyList<MaterialLotDto> Materials,
        IReadOnlyList<RelatedPanelDto> SameLotPanels,
        IReadOnlyList<RelatedPanelDto> SameMaterialPanels);

    public sealed record MaterialTrackingItemDto(
        string PanelNo,
        string LotNo,
        string MaterialLotNo,
        string MaterialType,
        string? MaterialName,
        string? Supplier,
        decimal? Quantity,
        DateTimeOffset UsedAt);

    /// <summary>
    /// 取得指定板的完整追溯資訊。
    /// </summary>
    [HttpGet("panel/{panelNo}")]
    public async Task<ActionResult<PanelTraceDto>> GetPanel(string panelNo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(panelNo))
        {
            return BadRequest(new { error = "panelNo is required" });
        }

        // 為什麼直接 .FirstOrDefaultAsync(panels)：
        // - panels 表已冗餘 lot_no，連 lots 都不必再 JOIN；
        //   PanelInfoDto 直接用 entity 屬性即可。
        var panel = await _db.Panels
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PanelNo == panelNo, cancellationToken);

        if (panel is null)
        {
            return NotFound(new { error = $"panel {panelNo} not found" });
        }

        var profile = _profileService.Current;
        var stationLookup = profile.Stations.ToDictionary(s => s.Code, s => s, StringComparer.OrdinalIgnoreCase);

        var stations = await _db.PanelStationLogs.AsNoTracking()
            .Where(x => x.PanelId == panel.Id)
            .OrderBy(x => x.EnteredAt)
            .ToListAsync(cancellationToken);

        var stationDtos = stations.Select(s =>
        {
            stationLookup.TryGetValue(s.StationCode, out var sta);
            return new StationLogDto(
                s.StationCode,
                sta?.LabelZh ?? s.StationCode,
                sta?.Seq ?? 0,
                s.EnteredAt,
                s.ExitedAt,
                s.Result,
                s.Operator,
                s.OperatorName,
                s.ToolCode,
                s.Note);
        }).ToList();

        // 為什麼仍 JOIN material_lots：
        // - 物料的 supplier / received_at 沒有冗餘到 panel_material_usage；
        //   單筆查詢 JOIN 一次成本可接受，也比把整個 MaterialLot 反正規化進中介表乾淨。
        var usages = await (
            from u in _db.PanelMaterialUsages.AsNoTracking()
            join m in _db.MaterialLots.AsNoTracking() on u.MaterialLotId equals m.Id
            where u.PanelId == panel.Id
            orderby u.UsedAt
            select new { Usage = u, Material = m })
            .ToListAsync(cancellationToken);

        var materials = usages.Select(x => new MaterialLotDto(
            x.Material.MaterialLotNo,
            x.Material.MaterialType,
            x.Material.MaterialName,
            x.Material.Supplier,
            x.Material.ReceivedAt,
            x.Usage.Quantity)).ToList();

        // 同 lot 板：直接 .Select Panel entity 即可。
        var sameLotPanels = await _db.Panels
            .AsNoTracking()
            .Where(p => p.LotId == panel.LotId && p.Id != panel.Id)
            .OrderBy(p => p.CreatedAt)
            .Take(50)
            .Select(p => new RelatedPanelDto(p.PanelNo, p.LotNo, p.Status, p.CreatedAt))
            .ToListAsync(cancellationToken);

        // 同物料板：透過 panel_material_usage 找對應 panel，因為 usage 已冗餘 panel_no/lot_no(間接) 我們仍要關 panels 表拿 status 與 createdAt。
        var materialIds = usages.Select(x => x.Material.Id).ToList();
        var sameMaterialPanels = await (
            from u in _db.PanelMaterialUsages.AsNoTracking()
            join p in _db.Panels.AsNoTracking() on u.PanelId equals p.Id
            where materialIds.Contains(u.MaterialLotId) && p.Id != panel.Id
            orderby u.UsedAt descending
            select new RelatedPanelDto(p.PanelNo, p.LotNo, p.Status, p.CreatedAt))
            .Distinct()
            .Take(50)
            .ToListAsync(cancellationToken);

        var dto = new PanelTraceDto(
            new PanelInfoDto(panel.PanelNo, panel.LotNo, panel.Status, panel.CreatedAt),
            stationDtos,
            materials,
            sameLotPanels,
            sameMaterialPanels);

        return Ok(dto);
    }

    /// <summary>
    /// 列出最近的板（給前端輸入框做 autocomplete / demo 用）。
    /// </summary>
    [HttpGet("panels/recent")]
    public async Task<ActionResult<IReadOnlyList<RelatedPanelDto>>> RecentPanels(
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        var items = await _db.Panels
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Take(take)
            .Select(p => new RelatedPanelDto(p.PanelNo, p.LotNo, p.Status, p.CreatedAt))
            .ToListAsync(cancellationToken);
        return Ok(items);
    }

    /// <summary>
    /// 依日期查詢「物料追蹤」真實資料（panel_material_usage + material_lots + panels）。
    /// </summary>
    /// <remarks>
    /// 為什麼要做這支查詢：
    /// - 前端「物料追溯查詢」需要的是某天所有用料明細，不是 recent panel 的前端二次過濾；
    /// - 直接在 DB 依 used_at 篩選才能保證畫面顯示的是當天真實資料。
    ///
    /// 解決什麼問題：
    /// - 避免使用者誤以為是 mock/即時生成資料；
    /// - DBeaver SQL 與前端畫面都對同一張交易表（panel_material_usage）校對。
    /// </remarks>
    [HttpGet("material-tracking")]
    public async Task<ActionResult<IReadOnlyList<MaterialTrackingItemDto>>> MaterialTracking(
        [FromQuery] DateOnly? date = null,
        [FromQuery] int take = 500,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 2000);
        var target = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        // 為什麼用 DateTimeOffset 邊界：
        // - panel_material_usage.used_at 欄位型別是 DateTimeOffset；
        //   直接用 offset-aware 區間比較，避免 provider 對 DateTime kind 轉換歧義。
        var start = new DateTimeOffset(target.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(target.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var items = await (
            from u in _db.PanelMaterialUsages.AsNoTracking()
            join m in _db.MaterialLots.AsNoTracking() on u.MaterialLotId equals m.Id
            join p in _db.Panels.AsNoTracking() on u.PanelId equals p.Id
            where u.UsedAt >= start && u.UsedAt < end
            orderby u.UsedAt descending
            select new MaterialTrackingItemDto(
                u.PanelNo,
                p.LotNo,
                u.MaterialLotNo,
                m.MaterialType,
                m.MaterialName,
                m.Supplier,
                u.Quantity,
                u.UsedAt))
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    /// <summary>
    /// 一次性回填：依 panel_station_log 最新站別結果重算所有 panel 狀態。
    /// </summary>
    /// <remarks>
    /// 為什麼要提供這支：
    /// - 狀態動態規則上線前，歷史資料可能停在 in_progress；
    /// - 需要一次批次回填，讓舊資料與新規則對齊，避免前端看到「全站 pass 但狀態仍 in_progress」。
    /// </remarks>
    [HttpPost("panels/backfill-status")]
    public async Task<ActionResult<object>> BackfillPanelStatus(CancellationToken cancellationToken)
    {
        // 為什麼以 stations 主檔當「完整過站」標準：
        // - 站別定義在主檔，避免前端或 worker 各自硬編碼站數；
        // - 當站別配置調整時，回填規則自動跟著主檔變化。
        var requiredStations = await _db.Stations
            .AsNoTracking()
            .OrderBy(s => s.Seq)
            .Select(s => s.StationCode)
            .ToListAsync(cancellationToken);

        var panels = await _db.Panels.ToListAsync(cancellationToken);
        if (panels.Count == 0)
        {
            return Ok(new
            {
                totalPanels = 0,
                updatedPanels = 0,
                requiredStationCount = requiredStations.Count,
            });
        }

        var panelIds = panels.Select(p => p.Id).ToList();
        var logs = await _db.PanelStationLogs
            .AsNoTracking()
            .Where(l => panelIds.Contains(l.PanelId))
            .OrderByDescending(l => l.EnteredAt)
            .ToListAsync(cancellationToken);

        var logsByPanel = logs.GroupBy(l => l.PanelId).ToDictionary(g => g.Key, g => g.ToList());
        var updated = 0;

        foreach (var panel in panels)
        {
            logsByPanel.TryGetValue(panel.Id, out var panelLogs);
            panelLogs ??= new List<Domain.Entities.PanelStationLog>();

            var latestByStation = new Dictionary<string, Domain.Entities.PanelStationLog>(StringComparer.OrdinalIgnoreCase);
            foreach (var log in panelLogs)
            {
                if (!latestByStation.ContainsKey(log.StationCode))
                {
                    latestByStation[log.StationCode] = log;
                }
            }

            var nextStatus = "in_progress";
            var hasFail = latestByStation.Values.Any(l =>
                string.Equals(l.Result, "fail", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(l.Result, "scrap", StringComparison.OrdinalIgnoreCase));
            if (hasFail)
            {
                nextStatus = "fail";
            }
            else if (requiredStations.Count > 0)
            {
                var allPassed = requiredStations.All(stationCode =>
                {
                    if (!latestByStation.TryGetValue(stationCode, out var row)) return false;
                    return row.ExitedAt.HasValue && string.Equals(row.Result, "pass", StringComparison.OrdinalIgnoreCase);
                });
                if (allPassed)
                {
                    nextStatus = "pass";
                }
            }

            if (!string.Equals(panel.Status, nextStatus, StringComparison.OrdinalIgnoreCase))
            {
                panel.Status = nextStatus;
                updated++;
            }
        }

        if (updated > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
        {
            totalPanels = panels.Count,
            updatedPanels = updated,
            requiredStationCount = requiredStations.Count,
        });
    }
}
