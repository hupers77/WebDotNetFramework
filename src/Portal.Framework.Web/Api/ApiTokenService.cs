using System.Text.RegularExpressions;
using Portal.Core.Config;
using Portal.Core.Keys;
using Portal.Security.Passwords;

namespace Portal.Framework.Web.Api;

/// <summary>발급한 토큰.</summary>
/// <param name="AccessToken">서명 토큰.</param>
/// <param name="ExpiresInSeconds">유효 시간(초).</param>
internal sealed record ApiTokenResult(string AccessToken, int ExpiresInSeconds);

/// <summary>토큰 요청 실패 (OAuth2 오류 코드로 응답한다).</summary>
internal sealed class ApiAuthException(int statusCode, string error, string description) : Exception(description)
{
    public int StatusCode { get; } = statusCode;

    public string Error { get; } = error;
}

/// <summary>
/// 토큰 발급 (OAuth2 Client Credentials). 비밀값은 해시로만 비교하고, 없는 ID·미발급·사용 중지는 모두 같은 오류로 답해 존재 여부를 알려 주지 않는다.
/// 클라이언트의 허용 API 코드를 토큰에 담는다.
/// </summary>
internal sealed partial class ApiTokenService(IApiClientStore clients, IPasswordHasher hasher, IPortalConfig config, ApiJwt jwt)
{
    private static readonly byte[] DummyHash = new byte[64];
    private static readonly byte[] DummySalt = new byte[16];

    /// <summary>클라이언트 ID와 비밀값을 확인하고 토큰을 발급한다.</summary>
    /// <exception cref="ApiAuthException">확인에 실패했을 때.</exception>
    public async Task<ApiTokenResult> IssueAsync(string clientId, string clientSecret, System.Net.IPAddress? clientIp, CancellationToken cancellationToken = default)
    {
        var client = ClientIdPattern().IsMatch(clientId) ? await clients.GetAsync(clientId, cancellationToken) : null;
        var hasSecret = client is { SecretHash: not null, SecretSalt: not null };
        // 없는 클라이언트도 같은 시간만큼 해시를 계산해 응답 시간으로 존재 여부가 드러나지 않게 한다
        var verified = hasher.Verify(clientSecret, hasSecret ? client!.SecretHash : DummyHash, hasSecret ? client!.SecretSalt : DummySalt);
        if (client is null || !hasSecret || !client.Active || !verified)
        {
            throw new ApiAuthException(401, "invalid_client", "Client authentication failed.");
        }

        if (client.IpListInvalid || !client.AllowIps.Allows(clientIp))
        {
            throw new ApiAuthException(403, "access_denied", "The client is not allowed from this address.");
        }

        var lifetime = TimeSpan.FromMinutes(config.GetInt(ConfigKeys.ApiTokenMinutes));
        var token = jwt.Create(client.ClientId, client.ApiCodes.Order(StringComparer.Ordinal), lifetime, out _);
        return new ApiTokenResult(token, (int)lifetime.TotalSeconds);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,50}$")]
    private static partial Regex ClientIdPattern();
}
