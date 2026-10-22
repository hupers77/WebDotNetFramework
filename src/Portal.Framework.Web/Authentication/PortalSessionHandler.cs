using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portal.Core.Keys;
using Portal.Framework.Web.Context;
using Portal.Framework.Web.Mvc;
using Portal.Security.Authentication;

namespace Portal.Framework.Web.Authentication;

/// <summary>
/// 세션 쿠키 인증 (설계서 4장 요청 처리 3단계). 요청마다 <c>TB_USER_SESSION</c>을 확인하므로
/// 강제 종료·잠금·중복 로그인이 다음 요청부터 바로 반영되고, 앱 풀이 재시작돼도 로그인이 유지된다.
/// 화면 요청은 로그인 화면으로 보내고, AJAX·API 요청은 401/403 JSON으로 답한다.
/// </summary>
public sealed class PortalSessionHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AuthService auth)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>인증 스킴 이름.</summary>
    public const string SchemeName = "PortalSession";

    /// <summary>로그인 화면 경로.</summary>
    public const string LoginPath = "/Account/Login";

    private const string EndReasonItem = "portal.session.end";

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = SessionCookie.Read(Request);
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }

        var check = await auth.CheckSessionAsync(token, extend: !RequestKind.IsBackground(Request), Context.RequestAborted);
        if (check.User is not { } user)
        {
            SessionCookie.Delete(Response);
            Context.Items[EndReasonItem] = check.EndMessageKey;
            return AuthenticateResult.NoResult();
        }

        Context.Features.Set(new PortalUserFeature(user, auth.IsPasswordChangeRequired(user.PwdChgReqYn, user.PwdChgDt)));
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.UserId),
                new Claim(ClaimTypes.Name, user.UserNm),
            ],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    /// <inheritdoc />
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var reason = Context.Items[EndReasonItem] as string;
        if (RequestKind.WantsJson(Request))
        {
            await WriteJsonAsync(StatusCodes.Status401Unauthorized, reason ?? MessageKeys.LoginRequired);
            return;
        }

        var returnUrl = Request.PathBase + Request.Path + Request.QueryString;
        var query = QueryString.Create([
            new("returnUrl", returnUrl),
            .. reason is null ? [] : new KeyValuePair<string, string?>[] { new("reason", reason) },
        ]);
        Response.Redirect(Request.PathBase + LoginPath + query);
    }

    /// <inheritdoc />
    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        if (RequestKind.WantsJson(Request))
        {
            await WriteJsonAsync(StatusCodes.Status403Forbidden, MessageKeys.Forbidden);
            return;
        }

        Response.StatusCode = StatusCodes.Status403Forbidden;
    }

    private Task WriteJsonAsync(int status, string messageKey)
    {
        Response.StatusCode = status;
        return Response.WriteAsJsonAsync(ApiResults.Fail(Context, messageKey));
    }
}
