using Microsoft.Extensions.Caching.Memory;
using Portal.Data;

namespace Portal.Framework.Web.Api.Outbound;

/// <summary>송신 대상 설정 (<c>FW.TB_API_OUTBOUND</c>).</summary>
/// <param name="TargetKey">대상 키.</param>
/// <param name="BaseUrl">기본 주소 (끝 / 없음).</param>
/// <param name="AuthTypeCd">인증 방식: NONE/BASIC/BEARER/API_KEY.</param>
/// <param name="AuthHeader">API_KEY일 때 헤더 이름.</param>
/// <param name="AuthUser">BASIC일 때 사용자.</param>
/// <param name="AuthSecretEnc">암호화된 인증 비밀값.</param>
/// <param name="TimeoutSec">한 번 호출의 제한 시간(초).</param>
/// <param name="RetryCount">재시도 횟수 (멱등 호출에만).</param>
/// <param name="AllowPrivate">사내망(사설 IP) 허용.</param>
/// <param name="Active">사용 중.</param>
public sealed record OutboundTarget(
    string TargetKey,
    string BaseUrl,
    string AuthTypeCd,
    string? AuthHeader,
    string? AuthUser,
    string? AuthSecretEnc,
    int TimeoutSec,
    int RetryCount,
    bool AllowPrivate,
    bool Active);

/// <summary>송신 대상 조회.</summary>
public interface IOutboundTargetStore
{
    /// <summary>대상을 읽는다. 없으면 null. 잠깐(<see cref="OutboundTargetStore.CacheSeconds"/>초) 기억한다.</summary>
    Task<OutboundTarget?> GetAsync(string targetKey, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class OutboundTargetStore(ISpExecutor sp, IMemoryCache cache) : IOutboundTargetStore
{
    internal const string GetSp = "FW.USP_API_OUTBOUND_GET";
    internal const int CacheSeconds = 30;

    /// <inheritdoc />
    public async Task<OutboundTarget?> GetAsync(string targetKey, CancellationToken cancellationToken = default)
    {
        var key = "apiout:" + targetKey;
        if (cache.TryGetValue(key, out OutboundTarget? hit))
        {
            return hit;
        }

        var row = await sp.QuerySingleAsync<Row>(GetSp, new { P_TARGET_KEY = targetKey }, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var target = new OutboundTarget(row.TargetKey, row.BaseUrl.TrimEnd('/'), row.AuthTypeCd, row.AuthHeader, row.AuthUser, row.AuthSecretEnc,
            row.TimeoutSec, row.RetryCount, row.AllowPrivateYn == "Y", row.UseYn == "Y");
        cache.Set(key, target, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CacheSeconds) });
        return target;
    }

    /// <summary><c>FW.USP_API_OUTBOUND_GET</c> 결과.</summary>
    internal sealed class Row
    {
        public string TargetKey { get; set; } = "";

        public string BaseUrl { get; set; } = "";

        public string AuthTypeCd { get; set; } = "NONE";

        public string? AuthHeader { get; set; }

        public string? AuthUser { get; set; }

        public string? AuthSecretEnc { get; set; }

        public int TimeoutSec { get; set; } = 30;

        public int RetryCount { get; set; }

        public string AllowPrivateYn { get; set; } = "N";

        public string UseYn { get; set; } = "Y";
    }
}
