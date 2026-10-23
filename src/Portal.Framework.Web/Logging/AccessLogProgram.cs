using Microsoft.AspNetCore.Http;

namespace Portal.Framework.Web.Logging;

/// <summary>
/// 접근 기록에 남길 프로그램을 요청 중에 정한다. <c>[Program]</c> 대신 요청 데이터로 프로그램을 찾는 Action(<c>[ProgramFromData]</c>: 첨부 파일, 레포트)은
/// 권한을 확인할 때(<c>PortalController.DemandAnyFuncAsync</c>) 여기에 넣고, <see cref="AccessLogMiddleware"/>가 응답 뒤에 읽는다.
/// </summary>
public static class AccessLogProgram
{
    private static readonly object Key = new();

    /// <summary>이 요청의 프로그램·기능을 정한다 (나중 값이 이긴다).</summary>
    public static void Set(HttpContext context, string programId, string funcCd)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Items[Key] = (programId, funcCd);
    }

    /// <summary>정한 프로그램·기능. 없으면 null.</summary>
    internal static (string ProgramId, string FuncCd)? Get(HttpContext context) =>
        context.Items.TryGetValue(Key, out var value) && value is (string programId, string funcCd) ? (programId, funcCd) : null;
}
