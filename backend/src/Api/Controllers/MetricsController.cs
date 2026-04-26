// MetricsController：W11 暴露即時觀測快照（GET /api/metrics）。
//
// 為什麼放 controller 而不是 Minimal API：
// - 全專案其他 endpoint 都用 controller，集中一致、Swagger 描述也比較好整理。
//
// 解決什麼問題：
// - 讓使用者 / smoke 腳本一行 curl 就能看到「目前推播狀態 + 違規累計 + 延遲」，
//   不需另外接 prometheus / grafana。

using AOIOpsPlatform.Api.Observability;
using Microsoft.AspNetCore.Mvc;

namespace AOIOpsPlatform.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class MetricsController : ControllerBase
{
    private readonly RealtimeMetricsService _metrics;

    public MetricsController(RealtimeMetricsService metrics)
    {
        _metrics = metrics;
    }

    /// <summary>
    /// 取目前 in-memory metrics 快照。
    /// </summary>
    [HttpGet]
    public ActionResult<RealtimeMetricsSnapshot> Get() => Ok(_metrics.Snapshot());
}
