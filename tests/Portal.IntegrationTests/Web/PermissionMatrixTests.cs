using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Keys;
using Portal.Framework.Web.Authorization;
using Portal.IntegrationTests.Auth;
using Portal.IntegrationTests.Infrastructure;
using Portal.Security.Authorization;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>
/// 권한 매트릭스 테스트 (설계서 15장, 1단계 게이트). 기준은 DB의 <c>FW.TB_PROGRAM_API</c>다.
/// <list type="number">
/// <item>코드의 <c>[Program]</c>/<c>[Func]</c>와 <c>TB_PROGRAM_API</c>가 한 건도 빠짐없이 같다 (화면을 추가하고 등록을 빠뜨리면 실패).</item>
/// <item>역할마다 <c>TB_PROGRAM_API</c>의 모든 경로에 대해, DB 권한 평가 결과가 역할 권한 설정과 같고,
/// 그 권한으로 실제 요청하면 허용 경로는 통과하고 나머지는 403이다.</item>
/// </list>
/// </summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class PermissionMatrixTests(DbFixture fixture, PortalWebFactory web) : IClassFixture<PortalWebFactory>
{
    /// <summary>POST에 필요한 CSRF 토큰을 얻는 화면 (모든 역할에 열려 있다).</summary>
    private const string HomeProgram = "DSH_HOME";

    public sealed record Api(string HttpMethod, string RoutePattern, string ProgramId, string FuncCd)
    {
        public override string ToString() => $"{HttpMethod} {RoutePattern} ({ProgramId}:{FuncCd})";
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<List<Api>> RegisteredAsync(TestDb db)
    {
        var rows = await db.ColumnAsync<string>("SELECT CONCAT(HTTP_METHOD, '|', ROUTE_PATTERN, '|', PROGRAM_ID, '|', FUNC_CD) FROM FW.TB_PROGRAM_API");
        return [.. rows.Select(r => r.Split('|')).Select(r => new Api(r[0], r[1], r[2], r[3])).OrderBy(a => a.RoutePattern, StringComparer.Ordinal).ThenBy(a => a.HttpMethod, StringComparer.Ordinal)];
    }

    /// <summary>Portal.Web의 모든 Action (테스트 전용 화면 제외).</summary>
    private List<ControllerActionDescriptor> Actions() =>
        [.. web.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(a => a.ControllerTypeInfo.Assembly == typeof(Program).Assembly)];

    /// <summary>관례 라우팅 경로: <c>/{area}/{controller}/{action}</c>, Index는 생략, 영역 없는 Home/Index는 <c>/</c>.</summary>
    private static string PathOf(ControllerActionDescriptor action)
    {
        if (action.AttributeRouteInfo?.Template is { } template)
        {
            return "/" + template;
        }

        var area = action.RouteValues.TryGetValue("area", out var a) ? a : null;
        var path = (area is null ? "" : "/" + area) + "/" + action.ControllerName + (action.ActionName == "Index" ? "" : "/" + action.ActionName);
        return path == "/Home" ? "/" : path;
    }

    /// <summary>메서드 제한이 없는 Action(관례 Index)은 GET으로 등록한다.</summary>
    private static IEnumerable<string> MethodsOf(ControllerActionDescriptor action) =>
        action.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods) is { } methods && methods.Any() ? methods : ["GET"];

    [Fact]
    public async Task 코드의권한표시와_TB_PROGRAM_API가_같다()
    {
        await using var db = await fixture.BeginAsync();
        var registered = await RegisteredAsync(db);

        var unmarked = new List<string>();
        var inCode = new List<Api>();
        foreach (var action in Actions())
        {
            var metadata = action.EndpointMetadata;
            if (metadata.OfType<IAllowAnonymous>().Any() || metadata.OfType<AuthenticatedOnlyAttribute>().Any()
                || metadata.OfType<ProgramFromDataAttribute>().Any())
            {
                // 프로그램을 데이터에서 찾는 Action(첨부)은 권한을 Action이 확인하고, FileControllerTests가 검증한다
                continue;
            }

            var (programId, funcCd) = PermissionFilter.Resolve(metadata);
            if (programId is null)
            {
                // 기본 거부라 열리지는 않지만, 어느 권한으로도 쓸 수 없는 Action은 표시를 빠뜨린 것이다
                unmarked.Add(action.DisplayName ?? action.ActionName);
                continue;
            }

            inCode.AddRange(MethodsOf(action).Select(m => new Api(m, PathOf(action), programId, funcCd)));
        }

        Assert.Empty(unmarked);
        Assert.Equal(
            registered.Select(a => a.ToString()),
            inCode.OrderBy(a => a.RoutePattern, StringComparer.Ordinal).ThenBy(a => a.HttpMethod, StringComparer.Ordinal).Select(a => a.ToString()));
    }

    [Fact]
    public async Task 역할별_모든경로의_허용차단이_역할권한설정과같다()
    {
        await using var db = await fixture.BeginAsync();
        var apis = await RegisteredAsync(db);
        var roles = await db.ColumnAsync<string>("SELECT ROLE_ID FROM FW.TB_ROLE WHERE USE_YN = 'Y' ORDER BY ROLE_ID");
        await db.SeedCompanyAsync("ITPMX");
        Assert.Contains(PermissionEvaluator.SysAdminRole, roles);

        var mismatches = new List<string>();
        foreach (var role in roles)
        {
            var partner = role.StartsWith("PARTNER", StringComparison.Ordinal);
            var userId = $"it.mx.{role.ToLowerInvariant()}";
            await db.SeedUserAsync(userId, partner ? "ITPMX" : "HQ", partner ? "PARTNER" : "EMP", roles: [role]);
            db.AsUser(userId, partner ? "ITPMX" : "HQ", partner ? "PARTNER" : "EMP");
            var set = await AuthTestContext.NewPermission(db).GetCurrentAsync(Ct);

            // 기대값: 역할 권한 표(사용 중인 프로그램만). 시스템 관리자는 전체.
            var configured = (await db.ColumnAsync<string>(
                """
                SELECT CONCAT(F.PROGRAM_ID, ':', F.FUNC_CD)
                  FROM FW.TB_ROLE_PROGRAM_FUNC F
                  JOIN FW.TB_PROGRAM P ON P.PROGRAM_ID = F.PROGRAM_ID AND P.USE_YN = 'Y'
                 WHERE F.ROLE_ID = @R
                """, ("@R", role))).ToHashSet(StringComparer.Ordinal);
            bool Expected(Api api) => role == PermissionEvaluator.SysAdminRole || configured.Contains($"{api.ProgramId}:{api.FuncCd}");

            var granted = apis.Where(a => set.Can(a.ProgramId, a.FuncCd)).Select(a => $"{a.ProgramId}:{a.FuncCd}").Distinct().ToList();
            var token = web.SignIn(userId, [.. granted], roles: [role], userTypeCd: partner ? "PARTNER" : "EMP");
            foreach (var api in apis)
            {
                if (set.Can(api.ProgramId, api.FuncCd) != Expected(api))
                {
                    mismatches.Add($"{role} {api}: DB 평가 {set.Can(api.ProgramId, api.FuncCd)}, 설정 {Expected(api)}");
                    continue;
                }

                var status = await SendAsync(token, api);
                var allowed = status is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized);
                if (allowed != Expected(api))
                {
                    mismatches.Add($"{role} {api}: 응답 {(int)status}, 설정 {(Expected(api) ? "허용" : "차단")}");
                }
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>
    /// 메뉴 공개 대상(<c>OPEN_SCOPE_CD</c>)은 메뉴 표시만 정하고 보안 검사는 세션과 권한으로만 하므로,
    /// 협력사 역할의 권한 설정 자체가 공개 대상과 맞아야 한다: 본사 전용 화면 권한은 없고, 함께 보는 화면(ALL)은 조회할 수 있다.
    /// </summary>
    [Fact]
    public async Task 협력사역할_본사전용화면권한없음_공용화면은조회가능()
    {
        await using var db = await fixture.BeginAsync();

        var empOnly = await db.ColumnAsync<string>(
            """
            SELECT CONCAT(F.ROLE_ID, ' ', F.PROGRAM_ID, ':', F.FUNC_CD)
              FROM FW.TB_ROLE_PROGRAM_FUNC F
             WHERE F.ROLE_ID LIKE 'PARTNER[_]%'
               AND EXISTS (SELECT 1 FROM FW.TB_MENU M WHERE M.PROGRAM_ID = F.PROGRAM_ID)
               AND NOT EXISTS (SELECT 1 FROM FW.TB_MENU M WHERE M.PROGRAM_ID = F.PROGRAM_ID AND M.OPEN_SCOPE_CD <> 'EMP')
            """);
        var sharedWithoutRead = await db.ColumnAsync<string>(
            """
            SELECT CONCAT(R.ROLE_ID, ' ', M.PROGRAM_ID)
              FROM FW.TB_MENU M
             CROSS JOIN FW.TB_ROLE R
             WHERE M.OPEN_SCOPE_CD = 'ALL' AND M.PROGRAM_ID IS NOT NULL AND M.USE_YN = 'Y'
               AND R.ROLE_ID LIKE 'PARTNER[_]%' AND R.USE_YN = 'Y'
               AND NOT EXISTS (SELECT 1 FROM FW.TB_ROLE_PROGRAM_FUNC F WHERE F.ROLE_ID = R.ROLE_ID AND F.PROGRAM_ID = M.PROGRAM_ID AND F.FUNC_CD = 'READ')
            """);

        Assert.Empty(empOnly);
        Assert.Empty(sharedWithoutRead);
    }

    [Fact]
    public async Task 경로마다_등록된기능이없으면403_같은프로그램의다른기능으로는열리지않는다()
    {
        await using var db = await fixture.BeginAsync();
        var apis = await RegisteredAsync(db);
        var funcs = await db.ColumnAsync<string>("SELECT CONCAT(PROGRAM_ID, ':', FUNC_CD) FROM FW.TB_PROGRAM_FUNC");

        var mismatches = new List<string>();
        foreach (var api in apis)
        {
            var required = $"{api.ProgramId}:{api.FuncCd}";
            var others = funcs.Where(f => f.StartsWith(api.ProgramId + ":", StringComparison.Ordinal) && f != required);
            var userId = $"mx.{api.HttpMethod}.{api.RoutePattern}".ToLowerInvariant();
            // 홈 권한은 POST의 CSRF 토큰을 얻는 데만 쓴다 (홈 자체를 검사할 때는 빼야 한다)
            string[] home = api.ProgramId == HomeProgram ? [] : [$"{HomeProgram}:{FuncCodes.Read}"];
            var denied = await SendAsync(web.SignIn(userId + ".no", [.. others, .. home]), api);
            var allowed = await SendAsync(web.SignIn(userId + ".yes", [required, .. home]), api);

            if (denied != HttpStatusCode.Forbidden)
            {
                mismatches.Add($"{api}: {required} 없이 {(int)denied}");
            }

            if (allowed is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                mismatches.Add($"{api}: {required}만으로 {(int)allowed}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>
    /// 경로를 호출해 상태 코드를 돌려준다. POST는 CSRF 토큰을 붙인다(토큰은 홈 화면에서 얻으므로 홈 권한이 있어야 한다).
    /// 권한 검사가 모델 바인딩보다 먼저이므로 본문은 빈 JSON이면 충분하다.
    /// </summary>
    private async Task<HttpStatusCode> SendAsync(string token, Api api)
    {
        var request = Ajax(new HttpMethod(api.HttpMethod), api.RoutePattern, api.HttpMethod == "GET" ? null : new { });
        string? cookie = null;
        if (api.HttpMethod != "GET")
        {
            (var csrf, cookie) = await GetCsrfAsync(web.NewClient(token), "/");
            request.Headers.Add("X-CSRF-TOKEN", csrf);
        }

        var response = await web.NewClient(token, cookie).SendAsync(request, Ct);
        return response.StatusCode;
    }
}
