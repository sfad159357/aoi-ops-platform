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
}
