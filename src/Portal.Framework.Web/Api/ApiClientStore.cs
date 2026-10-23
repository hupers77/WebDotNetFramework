using Microsoft.Extensions.Caching.Memory;
using Portal.Data;
using Portal.Security.Network;

namespace Portal.Framework.Web.Api;

/// <summary>API 클라이언트 설정 (<c>FW.TB_API_CLIENT</c>).</summary>
/// <param name="ClientId">클라이언트 ID.</param>
/// <param name="SecretHash">비밀값 해시 (미발급이면 null).</param>
/// <param name="SecretSalt">salt.</param>
/// <param name="ApiCodes">허용한 API 코드.</param>
/// <param name="AllowIps">허용 IP.</param>
/// <param name="IpListInvalid">저장된 IP 목록이 해석되지 않음 (안전하게 모두 거부한다).</param>
/// <param name="RatePerMin">분당 호출 한도 (null이면 기본값).</param>
/// <param name="Active">사용 중이고 사용 기한 안.</param>
public sealed record ApiClient(
    string ClientId,
    byte[]? SecretHash,
    byte[]? SecretSalt,
    IReadOnlySet<string> ApiCodes,
    IpAllowList AllowIps,
    bool IpListInvalid,
    int? RatePerMin,
    bool Active);

/// <summary>API 클라이언트 조회.</summary>
public interface IApiClientStore
{
    /// <summary>클라이언트를 읽는다. 없으면 null. 호출마다 DB를 읽지 않도록 잠깐(<see cref="ApiClientStore.CacheSeconds"/>초) 기억한다.</summary>
    Task<ApiClient?> GetAsync(string clientId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ApiClientStore(ISpExecutor sp, IMemoryCache cache) : IApiClientStore
{
    internal const string GetSp = "FW.USP_API_CLIENT_GET";

    /// <summary>설정 변경(사용 중지, 허용 API 변경)이 호출에 반영되기까지의 최대 시간.</summary>
    internal const int CacheSeconds = 30;

    /// <inheritdoc />
    public async Task<ApiClient?> GetAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var key = "apiclient:" + clientId;
        if (cache.TryGetValue(key, out ApiClient? hit))
        {
            return hit;
        }

        var row = await sp.QuerySingleAsync<ApiClientRow>(GetSp, new { P_CLIENT_ID = clientId }, cancellationToken);
        if (row is null)
        {
            // 없는 ID는 기억하지 않는다 (임의 ID로 캐시를 채우는 공격 방지)
            return null;
        }

        var ipInvalid = false;
        IpAllowList ips;
        try
        {
            ips = IpAllowList.Parse(row.AllowIpList);
        }
        catch (FormatException)
        {
            ips = IpAllowList.Parse(null);
            ipInvalid = true;
        }

        var codes = row.ApiCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var client = new ApiClient(row.ClientId, row.SecretHash, row.SecretSalt, new HashSet<string>(codes, StringComparer.Ordinal),
            ips, ipInvalid, row.RatePerMin, row.ActiveYn == "Y");
        cache.Set(key, client, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CacheSeconds) });
        return client;
    }

    /// <summary><c>FW.USP_API_CLIENT_GET</c> 결과.</summary>
    internal sealed class ApiClientRow
    {
        public string ClientId { get; set; } = "";

        public string ClientNm { get; set; } = "";

        public byte[]? SecretHash { get; set; }

        public byte[]? SecretSalt { get; set; }

        public string ApiCodes { get; set; } = "";

        public string? AllowIpList { get; set; }

        public int? RatePerMin { get; set; }

        public string ActiveYn { get; set; } = "N";
    }
}
