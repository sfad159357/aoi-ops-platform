// DomainProfileService：載入並提供當前 profile 的 singleton service。
//
// 為什麼用 singleton：
// - profile 是啟動時讀一次的設定，container 重啟才會變；
//   每次注入都讀檔太浪費 IO，singleton 把 JSON 反序列化的成本攤到 0。
// - 為了在 worker / controller / SignalR 任何地方都能拿到，註冊成 singleton 最直接。
//
// 解決什麼問題：
// - 給整個後端唯一一個「現在我跑哪個 profile」的事實來源；
//   切換產業 demo 只要改 docker env 重啟容器，code 不動。

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AOIOpsPlatform.Application.Domain;

/// <summary>
/// 載入並回傳當前生效的 <see cref="DomainProfile"/>。
/// </summary>
/// <remarks>
/// 為什麼提供 ParameterByCode / StationByCode 等查表方法：
/// - 後續 SpcRulesEngine / Controllers 經常要靠 code 取 USL / LSL；
///   集中在這裡用 dict 查 O(1)，比每次 LINQ FirstOrDefault 快也比較易讀。
/// </remarks>
public sealed class DomainProfileService
{
    private readonly DomainProfile _profile;
    private readonly Dictionary<string, DomainParameter> _parameterByCode;
    private readonly Dictionary<string, DomainStation> _stationByCode;
    private readonly Dictionary<string, DomainLine> _lineByCode;

    public DomainProfileService(IConfiguration configuration, ILogger<DomainProfileService> logger)
    {
        var profileId = configuration["Domain:Profile"]
                        ?? Environment.GetEnvironmentVariable("DOMAIN_PROFILE")
                        ?? "pcb";

        var profilesDir = configuration["Domain:ProfilesDirectory"]
                          ?? Environment.GetEnvironmentVariable("DOMAIN_PROFILES_DIR")
                          ?? "/app/shared/domain-profiles";

        var path = ResolveProfilePath(profilesDir, profileId, logger);

        // 為什麼用 try / fallback：
        // - 容器初次部署若 profile 檔還沒同步，落地一個最小空 profile 不會讓服務直接死；
        //   log 會清楚指出檔名，讓維運很快定位。
        try
        {
            using var stream = File.OpenRead(path);
            _profile = JsonSerializer.Deserialize<DomainProfile>(stream, JsonOpts)
                       ?? throw new InvalidOperationException($"Profile {profileId} 解析為 null");
            logger.LogInformation("Domain profile 已載入：{ProfileId} (from {Path})", _profile.ProfileId, path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "載入 domain profile 失敗：{Path}，改用空 profile", path);
            _profile = new DomainProfile { ProfileId = profileId, DisplayName = profileId };
        }

        _parameterByCode = _profile.Parameters.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        _stationByCode = _profile.Stations.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);
        _lineByCode = _profile.Lines.ToDictionary(l => l.Code, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>當前生效的 profile（不可變）。</summary>
    public DomainProfile Current => _profile;

    /// <summary>用 code 取參數定義（含 USL/LSL/target）；找不到回 null。</summary>
    public DomainParameter? ParameterByCode(string code)
        => _parameterByCode.TryGetValue(code, out var p) ? p : null;

    /// <summary>用 code 取站別定義；找不到回 null。</summary>
    public DomainStation? StationByCode(string code)
        => _stationByCode.TryGetValue(code, out var s) ? s : null;

    /// <summary>用 code 取產線定義；找不到回 null。</summary>
    public DomainLine? LineByCode(string code)
        => _lineByCode.TryGetValue(code, out var l) ? l : null;

    /// <summary>
    /// 解析 profile JSON 路徑；找多個常見位置避免容器內外路徑差異。
    /// </summary>
    /// <remarks>
    /// 為什麼要 try 多個位置：
    /// - 容器內：/src/shared/domain-profiles（bind mount）
    /// - 本機開發：相對於 Api 工作目錄
    /// - 容器固定：/app/shared/domain-profiles（未來打 image 時）
    ///   一次處理三種情境，後續不用為了路徑改 code。
    /// </remarks>
    private static string ResolveProfilePath(string profilesDir, string profileId, ILogger logger)
    {
        var candidates = new[]
        {
            Path.Combine(profilesDir, $"{profileId}.json"),
            Path.Combine(AppContext.BaseDirectory, "shared", "domain-profiles", $"{profileId}.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "domain-profiles", $"{profileId}.json"),
            Path.Combine("/src/shared/domain-profiles", $"{profileId}.json"),
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full))
            {
                return full;
            }
        }

        logger.LogWarning("找不到 profile JSON，將以第一個 candidate 路徑嘗試讀取（之後會 fail open）：{Path}",
            candidates[0]);
        return candidates[0];
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
