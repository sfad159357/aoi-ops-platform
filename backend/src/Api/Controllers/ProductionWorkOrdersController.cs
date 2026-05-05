using AOIOpsPlatform.Domain.Entities;
using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Controllers;

/// <summary>
/// Production Work Order（生產工單/製令）查詢 API。
/// </summary>
/// <remarks>
/// 為什麼提供獨立查詢入口：
/// - 使用者需要「生產工單」作為核心主體來看進度，但現有事件表多以 lot/panel 為粒度；
///   這支 API 用 production_work_order → lots → panels → station_logs 彙整，避免前端自己拼多次請求。
/// </remarks>
[ApiController]
[Route("api/production-work-orders")]
public sealed class ProductionWorkOrdersController : ControllerBase
{
    private readonly AoiOpsDbContext _db;

    public ProductionWorkOrdersController(AoiOpsDbContext db) => _db = db;

    public sealed record ProductionWorkOrderProgressDto(
        int Lots,
        int PanelsTotal,
        int PanelsPass,
        int PanelsFail,
        int PanelsInProgress);

    /// <summary>
    /// 工單列表右欄「目前站點」快照（依產線站序推導，而非時間上全域最新一筆 log）。
    /// </summary>
    /// <param name="Situation">
    /// pending_entry：尚未進入該站；in_station：已進站尚未出站；fail：該站判退；
    /// hold：已出站但結果非 pass/ok（例：warn）；completed：已依序走完且皆 pass/ok。
    /// </param>
    public sealed record PanelCurrentStationDto(
        string StationCode,
        string Situation,
        DateTimeOffset? EnteredAt,
        DateTimeOffset? ExitedAt,
        string? Result,
        string? ToolCode,
        string? Operator,
        string? OperatorName);

    public sealed record PanelDto(
        Guid Id,
        string PanelNo,
        string LotNo,
        string? Status,
        PanelCurrentStationDto? CurrentStation);

    public sealed record LotDto(
        Guid Id,
        string LotNo,
        string? Status,
        IReadOnlyList<PanelDto> Panels);

    public sealed record ProductionWorkOrderListItemDto(
        Guid Id,
        string WorkOrderNo,
        string? LineCode,
        string? Status,
        string? ProductCode,
        int? PlannedQuantity,
        DateTimeOffset CreatedAt,
        ProductionWorkOrderProgressDto Progress,
        IReadOnlyList<LotDto> Lots);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductionWorkOrderListItemDto>>> List(
        [FromQuery] int take = 10,
        [FromQuery] string? q = null,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 50);

        var query = _db.ProductionWorkOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            query = query.Where(pwo =>
                EF.Functions.Like(pwo.WorkOrderNo, like) ||
                pwo.Lots.Any(l => EF.Functions.Like(l.LotNo, like)));
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(pwo => new
            {
                pwo.Id,
                pwo.WorkOrderNo,
                pwo.LineCode,
                pwo.Status,
                pwo.ProductCode,
                pwo.PlannedQuantity,
                pwo.CreatedAt,
                Lots = pwo.Lots
                    .OrderByDescending(l => l.CreatedAt)
                    .Select(l => new
                    {
                        l.Id,
                        l.LotNo,
                        l.Status,
                        Panels = l.Panels
                            .OrderByDescending(p => p.CreatedAt)
                            .Select(p => new
                            {
                                p.Id,
                                p.PanelNo,
                                p.LotNo,
                                p.Status,
                            })
                            .Take(20)
                            .ToList()
                    })
                    .Take(20)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        // 為什麼不能在這裡沿用 panels.status：
        // - 與 Trace API 相同，板級「生產狀態」單一真相應由 panel_station_log 推導（全站最新一筆 + 是否皆已出站 pass）；
        // - panels.status 常為冗餘/未回寫欄位，會出現畫面左欄 in_progress、右欄卻顯示「FQC pass」這種時間軸與摘要互相打架的體驗。
        // 解決什麼問題：工單查詢列表與板號/批次查詢對同一張板描述一致。
        var panelIds = rows
            .SelectMany(x => x.Lots)
            .SelectMany(x => x.Panels)
            .Select(p => p.Id)
            .Distinct()
            .ToList();

        var requiredStations = await _db.Stations
            .AsNoTracking()
            .OrderBy(s => s.Seq)
            .Select(s => s.StationCode)
            .ToListAsync(cancellationToken);

        var logsByPanel = panelIds.Count == 0
            ? new Dictionary<Guid, List<PanelStationLog>>()
            : (await _db.PanelStationLogs
                    .AsNoTracking()
                    .Where(l => panelIds.Contains(l.PanelId))
                    .OrderBy(l => l.EnteredAt)
                    .ToListAsync(cancellationToken))
                .GroupBy(l => l.PanelId)
                .ToDictionary(g => g.Key, g => g.ToList());

        var derivedByPanel = panelIds.ToDictionary(
            id => id,
            id => DerivePanelProductionStatus(logsByPanel.GetValueOrDefault(id) ?? new List<PanelStationLog>(), requiredStations));

        var currentStationByPanel = panelIds.ToDictionary(
            id => id,
            id => BuildCurrentStationSnapshot(logsByPanel.GetValueOrDefault(id) ?? new List<PanelStationLog>(), requiredStations));

        var items = rows.Select(r =>
        {
            var allPanels = r.Lots.SelectMany(x => x.Panels).ToList();
            var panelsPass = allPanels.Count(p =>
                string.Equals(derivedByPanel[p.Id], "pass", StringComparison.OrdinalIgnoreCase));
            var panelsFail = allPanels.Count(p =>
                string.Equals(derivedByPanel[p.Id], "fail", StringComparison.OrdinalIgnoreCase));
            var panelsInProgress = allPanels.Count - panelsPass - panelsFail;

            return new ProductionWorkOrderListItemDto(
                r.Id,
                r.WorkOrderNo,
                r.LineCode,
                r.Status,
                r.ProductCode,
                r.PlannedQuantity,
                r.CreatedAt,
                new ProductionWorkOrderProgressDto(
                    Lots: r.Lots.Count,
                    PanelsTotal: allPanels.Count,
                    PanelsPass: panelsPass,
                    PanelsFail: panelsFail,
                    PanelsInProgress: panelsInProgress),
                r.Lots.Select(l => new LotDto(
                    l.Id,
                    l.LotNo,
                    l.Status,
                    l.Panels.Select(p => new PanelDto(
                        p.Id,
                        p.PanelNo,
                        p.LotNo,
                        derivedByPanel[p.Id],
                        currentStationByPanel[p.Id]))
                        .ToList()))
                    .ToList());
        }).ToList();

        return Ok(items);
    }

    /// <summary>
    /// 依 <paramref name="requiredStations"/> 順序找出板子「現在應關注」的站別。
    /// </summary>
    /// <remarks>
    /// 為什麼不用「EnteredAt 全表最大」的那一筆：
    /// - 時間上最後一筆可能是後段已複判 pass，但前段某站仍缺合格出站紀錄，MES 上仍應以前段卡住為準；
    /// - 與使用者直覺「目前卡在哪一站」一致。
    /// </remarks>
    private static PanelCurrentStationDto? BuildCurrentStationSnapshot(
        IReadOnlyList<PanelStationLog> logs,
        IReadOnlyList<string> requiredStations)
    {
        if (requiredStations.Count == 0)
        {
            return null;
        }

        var latestByStation = logs
            .GroupBy(s => s.StationCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.EnteredAt).First(), StringComparer.OrdinalIgnoreCase);

        foreach (var stationCode in requiredStations)
        {
            if (!latestByStation.TryGetValue(stationCode, out var row))
            {
                return new PanelCurrentStationDto(
                    stationCode,
                    "pending_entry",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            if (!row.ExitedAt.HasValue)
            {
                return new PanelCurrentStationDto(
                    stationCode,
                    "in_station",
                    row.EnteredAt,
                    null,
                    row.Result,
                    row.ToolCode,
                    row.Operator,
                    row.OperatorName);
            }

            if (IsFailLikeResult(row.Result))
            {
                return new PanelCurrentStationDto(
                    stationCode,
                    "fail",
                    row.EnteredAt,
                    row.ExitedAt,
                    row.Result,
                    row.ToolCode,
                    row.Operator,
                    row.OperatorName);
            }

            if (IsPassLikeResult(row.Result))
            {
                continue;
            }

            return new PanelCurrentStationDto(
                stationCode,
                "hold",
                row.EnteredAt,
                row.ExitedAt,
                row.Result,
                row.ToolCode,
                row.Operator,
                row.OperatorName);
        }

        var lastCode = requiredStations[^1];
        var lastRow = latestByStation[lastCode];
        return new PanelCurrentStationDto(
            lastCode,
            "completed",
            lastRow.EnteredAt,
            lastRow.ExitedAt,
            lastRow.Result,
            lastRow.ToolCode,
            lastRow.Operator,
            lastRow.OperatorName);
    }

    private static bool IsFailLikeResult(string? result)
    {
        var r = (result ?? "").Trim();
        return r.Equals("fail", StringComparison.OrdinalIgnoreCase) ||
               r.Equals("scrap", StringComparison.OrdinalIgnoreCase) ||
               r.Equals("ng", StringComparison.OrdinalIgnoreCase) ||
               r.Equals("reject", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPassLikeResult(string? result)
    {
        var r = (result ?? "").Trim();
        return r.Equals("pass", StringComparison.OrdinalIgnoreCase) ||
               r.Equals("ok", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 與 <see cref="TraceController"/> 同步的板級狀態推導，避免多頁面各說各話。
    /// </summary>
    private static string DerivePanelProductionStatus(
        IReadOnlyList<PanelStationLog> stations,
        IReadOnlyList<string> requiredStations)
    {
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
                if (!latestByStation.TryGetValue(stationCode, out var row))
                {
                    return false;
                }

                if (!row.ExitedAt.HasValue)
                {
                    return false;
                }

                return string.Equals(row.Result, "pass", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(row.Result, "ok", StringComparison.OrdinalIgnoreCase);
            });
            if (allPassed)
            {
                derivedStatus = "pass";
            }
        }

        return derivedStatus;
    }
}

