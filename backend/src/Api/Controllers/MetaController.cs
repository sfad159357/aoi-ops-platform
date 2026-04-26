// MetaController：對外曝露當前 domain profile 的端點。
//
// 為什麼開 /api/meta/profile：
// - 前端啟動時要拿一份 profile JSON 才能渲染選單 / 文案 / 規格線；
//   這支端點是「整個前端能跑起來的前提」，獨立成 controller 容易被找到。
// - 為避免前端跨服務拿 profile，集中由後端代發是最簡單的設計。
//
// 解決什麼問題：
// - 讓 React Context 開站第一件事就是 fetch 這支端點，後續所有頁面都讀 context，
//   切換產業 demo 完全不需要改前端 code。

using AOIOpsPlatform.Application.Domain;
using Microsoft.AspNetCore.Mvc;

namespace AOIOpsPlatform.Api.Controllers;

[ApiController]
[Route("api/meta")]
public sealed class MetaController : ControllerBase
{
    private readonly DomainProfileService _profileService;

    public MetaController(DomainProfileService profileService)
    {
        _profileService = profileService;
    }

    /// <summary>
    /// 取得當前生效的 domain profile。
    /// </summary>
    /// <remarks>
    /// 為什麼回傳 DomainProfile 物件而不是檔案內容字串：
    /// - 前端可以直接以強型別解析；未來若要在後端加欄位（例如「線上有效機台清單」）
    ///   也只要改 record，不需要動序列化邏輯。
    /// </remarks>
    [HttpGet("profile")]
    public ActionResult<DomainProfile> GetProfile()
    {
        return Ok(_profileService.Current);
    }
}
