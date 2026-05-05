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
/// - 系統聚焦 ABF 高階製程後，wafers 表已重新命名為 panels；
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
        Guid? ProductionWorkOrderId,
        string? WorkOrderNo,
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
        Guid? ProductionWorkOrderId,
        string? WorkOrderNo,
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

        // 為什麼 trace API 也要帶工單號：
        // - 物料追溯常用 panel/lot 當入口，但管理上需要一眼看到「屬於哪張製令/工單」；
        // - 單一真相是 lots.production_work_order_id → production_work_order.work_order_no。
        var lotRow = await _db.Lots
            .AsNoTracking()
            .Where(l => l.Id == panel.LotId)
            .Select(l => new { l.ProductionWorkOrderId })
            .FirstOrDefaultAsync(cancellationToken);
        var pwoId = lotRow?.ProductionWorkOrderId;
        var workOrderNo = pwoId.HasValue
            ? await _db.ProductionWorkOrders.AsNoTracking()
                .Where(p => p.Id == pwoId.Value)
                .Select(p => p.WorkOrderNo)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var profile = _profileService.Current;
        var stationLookup = profile.Stations.ToDictionary(s => s.Code, s => s, StringComparer.OrdinalIgnoreCase);

        var stations = await _db.PanelStationLogs.AsNoTracking()
            .Where(x => x.PanelId == panel.Id)
            .OrderBy(x => x.EnteredAt)
            .ToListAsync(cancellationToken);

        // 為什麼 Panel 狀態要以 panel_station_log 推導（單一狀態真相）：
        // - panels.status 是快取/冗餘欄位，可能因歷史資料或異常流程沒即時回寫而落後；
        // - 使用者希望畫面狀態與站別時間軸同一套事實來源，避免「時間軸顯示 fail 但狀態仍 pass/in_progress」。
        //
        // 取捨：
        // - 這裡只針對單板追溯 API 直接推導狀態；列表頁可仍用 panels.status 做快速查詢（由 ingestion/backfill 保持同步）。
        var requiredStations = await _db.Stations
            .AsNoTracking()
            .OrderBy(s => s.Seq)
            .Select(s => s.StationCode)
            .ToListAsync(cancellationToken);
        var latestByStation = stations
            .OrderByDescending(s => s.EnteredAt)
            .GroupBy(s => s.StationCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var derivedStatus = "in_progress";
        var hasFail = latestByStation.Values.Any(l =>
            string.Equals(l.Result, "fail", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.Result, "scrap", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.Result, "ng", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.Result, "reject", StringComparison.OrdinalIgnoreCase));
        if (hasFail)
        {
            derivedStatus = "fail";
        }
        else if (requiredStations.Count > 0)
        {
            var allPassed = requiredStations.All(stationCode =>
            {
                if (!latestByStation.TryGetValue(stationCode, out var row)) return false;
                if (!row.ExitedAt.HasValue) return false;
                return string.Equals(row.Result, "pass", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(row.Result, "ok", StringComparison.OrdinalIgnoreCase);
            });
            if (allPassed)
            {
                derivedStatus = "pass";
            }
        }

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
            new PanelInfoDto(panel.PanelNo, panel.LotNo, derivedStatus, pwoId, workOrderNo, panel.CreatedAt),
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
    /// 依工單/批次號列出該批次所有板（給前端用批次查板號清單）。
    /// </summary>
    /// <remarks>
    /// 為什麼需要這支：
    /// - 追溯常見入口不是板號，而是「先拿到工單/批次號」；
    /// - 先列出同批次所有板號，才能讓使用者點選查看完整時間軸與用料。
    ///
    /// 解決什麼問題：
    /// - 前端只撈 recent 20 張板時，輸入批次字串不一定在清單內，使用者會誤以為查詢壞掉。
    /// </remarks>
    [HttpGet("panels/by-lot")]
    public async Task<ActionResult<IReadOnlyList<RelatedPanelDto>>> PanelsByLot(
        [FromQuery] string lotNo,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(lotNo))
        {
            return BadRequest(new { error = "lotNo is required" });
        }

        take = Math.Clamp(take, 1, 500);
        var key = lotNo.Trim();
        var items = await _db.Panels
            .AsNoTracking()
            .Where(p => p.LotNo == key)
            .OrderBy(p => p.CreatedAt)
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
        [FromQuery] string? date = null,
        [FromQuery] int take = 500,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 2000);
        // 為什麼不直接用 DateOnly? model binding：
        // - 某些部署/版本下 DateOnly 的 query binding 可能會失敗（最後變成 null），導致前端怎麼換日期都查到「今天(UTC)」；
        // - 使用者要求畫面需能與 SQL 查詢對齊，因此這裡改成明確用 yyyy-MM-dd 解析，避免隱性 fallback。
        var target = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!string.IsNullOrWhiteSpace(date))
        {
            var s = date.Trim();
            if (!DateOnly.TryParseExact(s, "yyyy-MM-dd", out target))
            {
                return BadRequest(new { error = $"invalid date format: {s}, expected yyyy-MM-dd" });
            }
        }
        // 為什麼用 DateTimeOffset 邊界：
        // - panel_material_usage.used_at 欄位型別是 DateTimeOffset；
        //   直接用 offset-aware 區間比較，避免 provider 對 DateTime kind 轉換歧義。
        var start = new DateTimeOffset(target.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(target.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var items = await (
            from u in _db.PanelMaterialUsages.AsNoTracking()
            join m in _db.MaterialLots.AsNoTracking() on u.MaterialLotId equals m.Id
            join p in _db.Panels.AsNoTracking() on u.PanelId equals p.Id
            join l in _db.Lots.AsNoTracking() on p.LotId equals l.Id
            join wo in _db.ProductionWorkOrders.AsNoTracking() on l.ProductionWorkOrderId equals wo.Id into pj
            from pwo in pj.DefaultIfEmpty()
            where u.UsedAt >= start && u.UsedAt < end
            orderby u.UsedAt descending
            select new MaterialTrackingItemDto(
                u.PanelNo,
                p.LotNo,
                l.ProductionWorkOrderId,
                pwo != null ? pwo.WorkOrderNo : null,
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
