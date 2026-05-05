using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Controllers;

/// <summary>
/// Factory（產線/機台）查詢 API：提供「產線查詢」頁的平面圖與機台明細。
/// </summary>
/// <remarks>
/// 為什麼需要這組 API：
/// - 使用者要求一切從 SQL Server 真實表撈取，禁止任何 hard coding；
/// - MES 常見操作是「先看產線平面圖 → 點機台 → 看誰在操作、目前跑到哪張板」。
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public sealed class FactoryController : ControllerBase
{
    private readonly AoiOpsDbContext _db;

    public FactoryController(AoiOpsDbContext db)
    {
        _db = db;
    }

    public sealed record FactoryFloorDto(
        IReadOnlyList<StationDto> Stations,
        IReadOnlyList<LineTileDto> Lines);

    public sealed record StationDto(string StationCode, string StationName, int Seq);

    public sealed record LineTileDto(
        string LineCode,
        string LineName,
        IReadOnlyList<ToolTileDto> Tools);

    public sealed record ToolTileDto(
        string ToolCode,
        string ToolName,
        string? ToolType,
        string? Status,
        string MesState,
        string? StationCode,
        DateTimeOffset? LastSeenAt,
        int RecentTotalCount,
        int RecentWarnCount,
        int RecentFailCount,
        string? CurrentOperatorCode,
        string? CurrentOperatorName,
        string? CurrentPanelNo,
        string? CurrentLotNo,
        string? CurrentResult);

    public sealed record ToolDetailDto(
        string ToolCode,
        string ToolName,
        string? ToolType,
        string? Status,
        string? LineCode,
        string MesState,
        string? StationCode,
        DateTimeOffset? LastSeenAt,
        int RecentTotalCount,
        int RecentWarnCount,
        int RecentFailCount,
        string? CurrentOperatorCode,
        string? CurrentOperatorName,
        string? CurrentPanelNo,
        string? CurrentLotNo,
        string? CurrentResult,
        IReadOnlyList<ToolRecentOperatorDto> RecentOperators);

    public sealed record ToolRecentOperatorDto(
        string OperatorCode,
        string? OperatorName,
        DateTimeOffset LastAt,
        string? StationCode,
        string? PanelNo,
        string? Result);

    public sealed record BackfillToolLineRequest(
        string ToolCodeLike,
        string TargetLineCode,
        bool DryRun = true);

    private sealed record LineRow(Guid Id, string LineCode, string LineName);

    private sealed record ToolRow(
        string ToolCode,
        string ToolName,
        string? ToolType,
        string? Status,
        Guid? LineId,
        string? LineCode);

    /// <summary>
    /// 產線平面圖（2D view）所需資料：lines + tools + 每台機的「目前狀態」快照。
    /// </summary>
    [HttpGet("floor")]
    public async Task<ActionResult<FactoryFloorDto>> Floor(CancellationToken cancellationToken)
    {
        // 為什麼機台狀態只看「最新一筆 panel_station_log」：
        // - 使用者要求「最新一筆 panel_no 狀態是什麼，機台狀態就是什麼」；
        // - 因此不再用 tools.status 或「短視窗 fail 統計」推導，避免多訊號造成判斷不一致。
        // 為什麼用 1h（而不是 2h）：
        // - 使用者希望「現在這台機最近的品質/壓力」能更即時反映，因此統計視窗縮短；
        // - 取捨：視窗縮短會讓計數更敏感、波動更大，但更符合現場看板的「即時感」。
        var recentCountWindowStart = DateTimeOffset.UtcNow.AddHours(-1);

        var lines = await _db.Lines
            .AsNoTracking()
            .OrderBy(l => l.LineCode)
            .Select(l => new LineRow(l.Id, l.LineCode, l.LineName))
            .ToListAsync(cancellationToken);

        // 為什麼站別順序要從 DB stations 主檔取：
        // - 使用者希望機台依製程順序排列（從頭到尾）；
        // - 站別順序單一真相在 stations.seq，不應由前端硬寫。
        var stations = await _db.Stations
            .AsNoTracking()
            .OrderBy(s => s.Seq)
            .Select(s => new StationDto(s.StationCode, s.StationName, s.Seq))
            .ToListAsync(cancellationToken);

        var tools = await _db.Tools
            .AsNoTracking()
            .OrderBy(t => t.LineCode)
            .ThenBy(t => t.ToolCode)
            .Select(t => new ToolRow(t.ToolCode, t.ToolName, t.ToolType, t.Status, t.LineId, t.LineCode))
            .ToListAsync(cancellationToken);

        var toolCodes = tools.Select(t => t.ToolCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // 取每台機的「最新一筆」station log（entered_at 最大者）。
        // 取捨：為了避免 N+1 query，這裡以 toolCodes + 最近 24 小時資料一次撈回再 in-memory 取 first。
        var latestWindowStart = DateTimeOffset.UtcNow.AddHours(-24);
        var latestLogs = await _db.PanelStationLogs
            .AsNoTracking()
            .Where(l => l.ToolCode != null && toolCodes.Contains(l.ToolCode) && l.EnteredAt >= latestWindowStart)
            .OrderByDescending(l => l.EnteredAt)
            .Select(l => new
            {
                l.ToolCode,
                l.StationCode,
                l.EnteredAt,
                l.ExitedAt,
                l.Operator,
                l.OperatorName,
                l.PanelId,
                l.PanelNo,
                l.Result,
            })
            .ToListAsync(cancellationToken);

        var latestByTool = latestLogs
            .GroupBy(x => x.ToolCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // 為什麼 floor 的「當下狀態」必須只用 latestByTool（entered_at 最大）：
        // - 使用者對「最新」的直覺是時間軸：最新一筆 log 是 fail，就應該顯示異常；
        // - 若同時存在「較舊的在製 in_process」與「較新的 fail/pass」，優先採在製會讓卡片顯示稼動，
        //   但 modal 的近期列表第一列卻是 fail，造成強烈不一致（你看到的 REFLOW-A01 就是這種案例）。
        //
        // 取捨：
        // - 這會讓「在製中」狀態必須體現在「最新 log 的 result=in_process 或 exited_at=NULL」，
        //   而不是靠「挑一筆舊的在製 log」硬蓋掉時間上更新的結案事件。

        // 保留 1h 統計（用於 UI 顯示品質壓力），但不再用來決定 mesState。
        var recentCountsByTool = latestLogs
            .Where(x => x.EnteredAt >= recentCountWindowStart)
            .GroupBy(x => x.ToolCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g =>
            {
                var total = g.Count();
                var warn = g.Count(x => string.Equals(x.Result, "warn", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Result, "warning", StringComparison.OrdinalIgnoreCase));
                var fail = g.Count(x =>
                    string.Equals(x.Result, "fail", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.Result, "ng", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.Result, "reject", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.Result, "scrap", StringComparison.OrdinalIgnoreCase));
                return (total, warn, fail);
            }, StringComparer.OrdinalIgnoreCase);

        var activePanelIds = latestByTool.Values
            .Where(x => !x.ExitedAt.HasValue || string.Equals(x.Result, "in_process", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.PanelId)
            .Distinct()
            .ToList();
        var lotNoByPanelId = await _db.Panels
            .AsNoTracking()
            .Where(p => activePanelIds.Contains(p.Id))
            .Select(p => new { p.Id, p.LotNo })
            .ToDictionaryAsync(x => x.Id, x => x.LotNo, cancellationToken);

        // 為什麼優先用 line_id 分群：
        // - line_code 是冗餘字串，容易因歷史資料或寫入順序造成 null/錯誤；
        // - line_id FK 才是 DB 層可驗證的單一真相。
        var toolsByLineId = tools
            .Where(t => t.LineId.HasValue)
            .GroupBy(t => t.LineId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var lineDtos = new List<LineTileDto>();
        foreach (var line in lines)
        {
            toolsByLineId.TryGetValue(line.Id, out var lineTools);
            lineTools ??= new List<ToolRow>();

            var toolDtos = new List<ToolTileDto>();
            foreach (var t in lineTools)
            {
                latestByTool.TryGetValue(t.ToolCode, out var latest);
                var lotNo = latest != null && lotNoByPanelId.TryGetValue(latest.PanelId, out var ln) ? ln : null;
                recentCountsByTool.TryGetValue(t.ToolCode, out var counts);

                // 為什麼要帶 lastSeenAt：
                // - 若該機台在 24h 內沒有任何 panel_station_log，latest 會是 null；
                //   這時候不能把 exited_at=null 誤判成 running，否則整條線會看起來「全在跑但又全異常」。
                // - 透過 overload 讓「完全沒有 log」的機台回到 offline，符合現場直覺（沒資料 = 沒上線/沒接到事件）。
                var mesState = ComputeMesStateFromLatestLog(latest?.EnteredAt, latest?.ExitedAt, latest?.Result);

                toolDtos.Add(new ToolTileDto(
                    t.ToolCode,
                    t.ToolName,
                    t.ToolType,
                    t.Status,
                    mesState,
                    latest?.StationCode,
                    latest?.EnteredAt,
                    counts.total,
                    counts.warn,
                    counts.fail,
                    latest?.Operator,
                    latest?.OperatorName,
                    latest?.PanelNo,
                    lotNo,
                    latest?.Result
                ));
            }

            lineDtos.Add(new LineTileDto(line.LineCode, line.LineName, toolDtos));
        }

        return Ok(new FactoryFloorDto(stations, lineDtos));
    }

    /// <summary>
    /// 機台明細：點機台 modal 需要的「當下」與「近期操作人員」資訊。
    /// </summary>
    [HttpGet("tools/{toolCode}")]
    public async Task<ActionResult<ToolDetailDto>> ToolDetail(string toolCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toolCode))
        {
            return BadRequest(new { error = "toolCode is required" });
        }

        var code = toolCode.Trim();
        var tool = await _db.Tools
            .AsNoTracking()
            .Where(t => t.ToolCode == code)
            .Select(t => new { t.ToolCode, t.ToolName, t.ToolType, t.Status, t.LineCode })
            .FirstOrDefaultAsync(cancellationToken);

        if (tool is null)
        {
            return NotFound(new { error = $"tool {code} not found" });
        }

        // 為什麼 ToolDetail 也要以「entered_at 最新」為單一真相：
        // - 使用者會用「近期操作人員」第一列當作最新事件；若上方快照採另一套挑選規則（例如在製優先），
        //   會出現「列表最新是 fail，但 MES 狀態卻是稼動」的矛盾。
        // - 因此這裡與 /factory/floor 一致：永遠用最新一筆 log 推導 mesState/在製資訊。
        var latest = await _db.PanelStationLogs
            .AsNoTracking()
            .Where(l => l.ToolCode == code)
            .OrderByDescending(l => l.EnteredAt)
            .Select(l => new
            {
                l.StationCode,
                l.EnteredAt,
                l.ExitedAt,
                l.Operator,
                l.OperatorName,
                l.PanelId,
                l.PanelNo,
                l.Result,
            })
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        // 為什麼工具明細同樣用 1h：
        // - 避免 floor 與 detail 兩邊顯示的統計視窗不一致，造成使用者誤判。
        var recentCountWindowStart = now.AddHours(-1);
        var recentCounts = await _db.PanelStationLogs
            .AsNoTracking()
            .Where(l => l.ToolCode == code && l.EnteredAt >= recentCountWindowStart)
            .Select(l => l.Result)
            .ToListAsync(cancellationToken);

        var recentTotal = recentCounts.Count;
        var recentWarn = recentCounts.Count(r => string.Equals(r, "warn", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "warning", StringComparison.OrdinalIgnoreCase));
        var recentFail = recentCounts.Count(r =>
            string.Equals(r, "fail", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, "ng", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, "reject", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, "scrap", StringComparison.OrdinalIgnoreCase));

        var mesState = ComputeMesStateFromLatestLog(latest?.EnteredAt, latest?.ExitedAt, latest?.Result);

        string? lotNo = null;
        if (latest != null && (!latest.ExitedAt.HasValue || string.Equals(latest.Result, "in_process", StringComparison.OrdinalIgnoreCase)))
        {
            lotNo = await _db.Panels
                .AsNoTracking()
                .Where(p => p.Id == latest.PanelId)
                .Select(p => p.LotNo)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // 為什麼 recent operators 取自 panel_station_log：
        // - 站別履歷天生帶 operator/tool/panel/result，最能模擬「這台機最近誰在跑」的 MES 追溯。
        var recent = await _db.PanelStationLogs
            .AsNoTracking()
            .Where(l => l.ToolCode == code && l.Operator != null)
            .OrderByDescending(l => l.EnteredAt)
            .Select(l => new ToolRecentOperatorDto(
                l.Operator!,
                l.OperatorName,
                l.EnteredAt,
                l.StationCode,
                l.PanelNo,
                l.Result))
            .Take(12)
            .ToListAsync(cancellationToken);

        return Ok(new ToolDetailDto(
            tool.ToolCode,
            tool.ToolName,
            tool.ToolType,
            tool.Status,
            tool.LineCode,
            mesState,
            latest?.StationCode,
            latest?.EnteredAt,
            recentTotal,
            recentWarn,
            recentFail,
            latest?.Operator,
            latest?.OperatorName,
            latest?.PanelNo,
            lotNo,
            latest?.Result,
            recent
        ));
    }

    /// <summary>
    /// 回填 tools 的產線關聯（line_id/line_code）。
    /// </summary>
    /// <remarks>
    /// 為什麼提供這支 API：
    /// - 歷史資料可能因 ingestion/seed 版本不同而讓 tools 沒掛到線，造成平面圖某條線「看起來沒機台」；
    /// - 使用者希望以 SQL Server 真實資料回填，不靠前端猜測或硬寫對應表。
    ///
    /// 取捨：
    /// - API 以「tool_code LIKE」+「目標 line_code」讓你明確指定回填範圍，避免內建規則造成誤更新；
    /// - 預設 dryRun=true，先回傳將更新筆數與樣本，再由你決定是否真的更新。
    /// </remarks>
    [HttpPost("tools/backfill-line")]
    public async Task<ActionResult<object>> BackfillToolLine(
        [FromBody] BackfillToolLineRequest req,
        CancellationToken cancellationToken)
    {
        if (req is null)
        {
            return BadRequest(new { error = "request body is required" });
        }
        if (string.IsNullOrWhiteSpace(req.ToolCodeLike))
        {
            return BadRequest(new { error = "toolCodeLike is required" });
        }
        if (string.IsNullOrWhiteSpace(req.TargetLineCode))
        {
            return BadRequest(new { error = "targetLineCode is required" });
        }

        var like = req.ToolCodeLike.Trim();
        var targetLineCode = req.TargetLineCode.Trim();
        var targetLine = await _db.Lines
            .AsNoTracking()
            .Where(l => l.LineCode == targetLineCode)
            .Select(l => new { l.Id, l.LineCode })
            .FirstOrDefaultAsync(cancellationToken);

        if (targetLine is null)
        {
            return NotFound(new { error = $"target line_code not found: {targetLineCode}" });
        }

        var matches = await _db.Tools
            .AsNoTracking()
            .Where(t => EF.Functions.Like(t.ToolCode, like))
            .OrderBy(t => t.ToolCode)
            .Select(t => new { t.ToolCode, t.LineCode, t.LineId })
            .Take(30)
            .ToListAsync(cancellationToken);

        var total = await _db.Tools
            .AsNoTracking()
            .Where(t => EF.Functions.Like(t.ToolCode, like))
            .CountAsync(cancellationToken);

        if (req.DryRun)
        {
            return Ok(new
            {
                dryRun = true,
                toolCodeLike = like,
                targetLineCode = targetLine.LineCode,
                matchedTools = total,
                sample = matches,
            });
        }

        // 為什麼用 ExecuteSql：
        // - 這是一次性的 bulk update，EF change tracking 沒必要介入；
        // - 直接用單條 UPDATE 最能保證效能與原子性。
        var updated = await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"
            UPDATE tools
            SET line_id = {targetLine.Id},
                line_code = {targetLine.LineCode}
            WHERE tool_code LIKE {like}
            ",
            cancellationToken);

        return Ok(new
        {
            dryRun = false,
            toolCodeLike = like,
            targetLineCode = targetLine.LineCode,
            matchedTools = total,
            updatedTools = updated,
            sampleBefore = matches,
        });
    }

    private static string ComputeMesStateFromLatestLog(DateTimeOffset? exitedAt, string? result)
    {
        // 為什麼只看最新一筆：
        // - 使用者要求機台狀態 = 最新 panel_station_log 的狀態；
        // - 這裡把 exited_at=NULL 視為 in_process，其餘以 result 直接映射。
        //
        // 解決什麼問題：
        // - 某些資料來源會把「in_process」用 result 標記，但 exited_at 仍被填值（或被後續流程補上），
        //   這會讓機台看起來永遠是 idle；因此只要 result 明確是 in_process，就一律視為 running。
        var r = (result ?? "").Trim().ToLowerInvariant();
        if (r is "in_process" or "processing" or "running")
        {
            return "running";
        }
        if (!exitedAt.HasValue)
        {
            return "running";
        }

        if (r is "fail" or "ng" or "reject" or "scrap")
        {
            return "abnormal";
        }
        if (r is "warn" or "warning")
        {
            return "idle";
        }
        if (r is "pass" or "ok")
        {
            return "idle";
        }
        if (r is "skip" or "bypass")
        {
            return "idle";
        }
        return "idle";
    }

    private static string ComputeMesStateFromLatestLog(DateTimeOffset? _lastSeenAt, DateTimeOffset? exitedAt, string? result)
        => exitedAt.HasValue || _lastSeenAt.HasValue ? ComputeMesStateFromLatestLog(exitedAt, result) : "offline";
}

