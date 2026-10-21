using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Portal.Core.Keys;
using Portal.Framework.Web.Context;
using Portal.Framework.Web.Mvc;
using Portal.Security.Authentication;

namespace Portal.Framework.Web.Authorization;

/// <summary>
/// 기능 권한 검사 (설계서 6장 "적용 방법"). Action의 <c>[Program]</c>과 <c>[Func]</c>(없으면 READ)로 현재 사용자 권한을 확인한다.
/// <list type="bullet">
/// <item><c>[AllowAnonymous]</c>: 검사하지 않는다 (로그인 화면).</item>
/// <item>로그인 전: 401 또는 로그인 화면.</item>
/// <item>비밀번호 변경 필요: 비밀번호 변경 화면과 <c>[AuthenticatedOnly]</c> 외에는 변경 화면으로 보낸다.</item>
/// <item><c>[AuthenticatedOnly]</c>: 로그인만 확인한다.</item>
/// <item>프로그램이 없거나 권한이 없으면 403 (기본 거부).</item>
/// </list>
/// </summary>
public sealed class PermissionFilter(CurrentPermissions permissions) : IAsyncAuthorizationFilter
{
    /// <summary>비밀번호 변경 프로그램 (변경 강제 중에도 열린다).</summary>
    public const string PasswordProgram = "MY_PWD_CHG";

    /// <summary>비밀번호 변경 화면 경로 (초기 데이터 <c>TB_PROGRAM.URL</c>).</summary>
    public const string PasswordPath = "/My/Password";

    /// <inheritdoc />
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        var http = context.HttpContext;
        if (http.Features.Get<PortalUserFeature>() is not { } user)
        {
            context.Result = new ChallengeResult();
            return;
        }

        var (programId, funcCd) = Resolve(metadata);
        if (user.PasswordChangeRequired && programId != PasswordProgram && !metadata.OfType<AuthenticatedOnlyAttribute>().Any())
        {
            context.Result = RequestKind.WantsJson(http.Request)
                ? new ObjectResult(ApiResults.Fail(http, AuthMessageKeys.PasswordChangeRequired)) { StatusCode = StatusCodes.Status403Forbidden }
                : new RedirectResult(http.Request.PathBase + PasswordPath);
            return;
        }

        if (metadata.OfType<AuthenticatedOnlyAttribute>().Any())
        {
            return;
        }

        if (programId is null)
        {
            context.Result = new ForbidResult();
            return;
        }

        var granted = await permissions.GetAsync(http.RequestAborted);
        if (!granted.Can(programId, funcCd))
        {
            context.Result = new ForbidResult();
        }
    }

    /// <summary>Action 메타데이터의 프로그램과 기능 (Action 특성이 Controller 특성보다 우선).</summary>
    internal static (string? ProgramId, string FuncCd) Resolve(IList<object> metadata) =>
        (metadata.OfType<ProgramAttribute>().LastOrDefault()?.ProgramId,
         metadata.OfType<FuncAttribute>().LastOrDefault()?.FuncCd ?? FuncCodes.Read);
}
