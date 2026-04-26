using AOIOpsPlatform.Application.Domain;
using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Controllers;

/// <summary>
/// 物料追溯 API：對外用 panel_no 當入口。
/// </summary>
/// <remarks>
/// 為什麼用 panel_no（不是 wafer.id）：
/// - 工程師現場操作習慣是「掃 QR Code 找一張板」，QR Code 寫的是 panel_no；
///   API 介面直接吃 panel_no，比強迫客戶端先把 panel_no → wafer.id 多一層查詢直接得多。
///
/// 為什麼一支 GET 同時回三段（板資訊 + 站別歷程 + 物料 + 同批次）：
/// - 對應 HTML Tab 2 的 4 個區塊，前端打一支 API 就能畫整頁，
///   省去多次 round-trip 與多載入狀態管理。
///
/// 解決什麼問題：
/// - 把「為什麼這張板有問題、哪些同批次板可能也有問題」一次回給前端。
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

        // 為什麼用 join 形式取 panel + lot：
        // - 前端表頭固定要顯示 lot_no，先 join 一次比再多 round-trip 漂亮。
        var panel = await (
            from w in _db.Wafers.AsNoTracking()
            join l in _db.Lots.AsNoTracking() on w.LotId equals l.Id
            where w.PanelNo == panelNo
            select new { Wafer = w, Lot = l })
            .FirstOrDefaultAsync(cancellationToken);

        if (panel is null)
        {
            return NotFound(new { error = $"panel {panelNo} not found" });
        }

        var profile = _profileService.Current;
        // 為什麼 station label 要從 profile 拿：
        // - PCB / 半導體切換時，UI 會自動顯示對應的中文名稱（錫膏印刷 vs 蝕刻），
        //   API 層幫忙 enrich 可以避免前端再 lookup 一次。
        var stationLookup = profile.Stations.ToDictionary(s => s.Code, s => s);

        var stations = await _db.PanelStationLogs.AsNoTracking()
            .Where(x => x.PanelId == panel.Wafer.Id)
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
                s.Note);
        }).ToList();

        var usages = await (
            from u in _db.PanelMaterialUsages.AsNoTracking()
            join m in _db.MaterialLots.AsNoTracking() on u.MaterialLotId equals m.Id
            where u.PanelId == panel.Wafer.Id
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

        // 為什麼「同 lot」直接撈所有 wafer：
        // - 工程師最常想「這 lot 的其他板有沒有也 fail」，10 ~ 25 片可以全列。
        var sameLotPanels = await (
            from w in _db.Wafers.AsNoTracking()
            where w.LotId == panel.Wafer.LotId && w.Id != panel.Wafer.Id && w.PanelNo != null
            orderby w.CreatedAt
            select new RelatedPanelDto(w.PanelNo!, panel.Lot.LotNo, w.Status, w.CreatedAt))
            .Take(50)
            .ToListAsync(cancellationToken);

        // 為什麼「同物料」要 distinct + 限制 50 筆：
        // - 一批錫膏可能用在數百張板，全部撈會把前端撐爆；
        //   先取最近 50 張 demo 用，正式上線可加分頁。
        var materialIds = usages.Select(x => x.Material.Id).ToList();
        var sameMaterialPanels = await (
            from u in _db.PanelMaterialUsages.AsNoTracking()
            join w in _db.Wafers.AsNoTracking() on u.PanelId equals w.Id
            join l in _db.Lots.AsNoTracking() on w.LotId equals l.Id
            where materialIds.Contains(u.MaterialLotId)
                  && w.Id != panel.Wafer.Id
                  && w.PanelNo != null
            orderby u.UsedAt descending
            select new RelatedPanelDto(w.PanelNo!, l.LotNo, w.Status, w.CreatedAt))
            .Distinct()
            .Take(50)
            .ToListAsync(cancellationToken);

        var dto = new PanelTraceDto(
            new PanelInfoDto(panel.Wafer.PanelNo!, panel.Lot.LotNo, panel.Wafer.Status, panel.Wafer.CreatedAt),
            stationDtos,
            materials,
            sameLotPanels,
            sameMaterialPanels);

        return Ok(dto);
    }

    /// <summary>
    /// 列出最近的板（給前端輸入框做 autocomplete / demo 用）。
    /// </summary>
    /// <remarks>
    /// 為什麼提供這支：
    /// - demo / 面試時最尷尬的就是「不知道要輸入哪個 panelNo」；
    ///   在 UI 顯示「最近 20 張板」可以讓觀眾立刻點一張看。
    /// </remarks>
    [HttpGet("panels/recent")]
    public async Task<ActionResult<IReadOnlyList<RelatedPanelDto>>> RecentPanels(
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        var items = await (
            from w in _db.Wafers.AsNoTracking()
            join l in _db.Lots.AsNoTracking() on w.LotId equals l.Id
            where w.PanelNo != null
            orderby w.CreatedAt descending
            select new RelatedPanelDto(w.PanelNo!, l.LotNo, w.Status, w.CreatedAt))
            .Take(take)
            .ToListAsync(cancellationToken);
        return Ok(items);
    }
}
