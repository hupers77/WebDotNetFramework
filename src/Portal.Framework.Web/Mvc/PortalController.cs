using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;

namespace Portal.Framework.Web.Mvc;

/// <summary>
/// 모든 화면 Controller의 기본 클래스 (개발 표준 가이드 3장). 클래스에 <c>[Program]</c>, 변경 Action에 <c>[Func]</c>를 붙인다.
/// 업무 오류(<c>BizException</c>)와 예상 못한 오류는 <see cref="ApiExceptionFilter"/>가 공통 형식으로 바꾸므로 Action에서 잡지 않는다.
/// </summary>
public abstract class PortalController : Controller
{
    /// <summary>데이터 없는 성공 응답.</summary>
    protected static new ApiResult Ok() => ApiResult.Ok();

    /// <summary>데이터를 담은 성공 응답.</summary>
    protected static ApiResult<T> Ok<T>(T data) => ApiResult.Ok(data);

    /// <summary>
    /// 그리드 일괄 저장에서 행 상태별 기능 권한을 확인한다 (추가 행 → CREATE, 수정 → UPDATE, 삭제 → DELETE).
    /// 없으면 <see cref="PermissionDeniedException"/>(403), 모르는 행 상태나 빈 목록이면 업무 오류를 던진다.
    /// </summary>
    protected async Task DemandRowFuncsAsync(IReadOnlyCollection<IGridRow> rows)
    {
        if (rows.Count == 0)
        {
            throw new BizException(MessageKeys.NoChanges);
        }

        var funcs = RowStates.RequiredFuncs(rows) ?? throw new BizException(MessageKeys.InvalidInput);
        var programId = PermissionFilter.Resolve(ControllerContext.ActionDescriptor.EndpointMetadata).ProgramId
            ?? throw new InvalidOperationException("[Program]이 없는 Controller입니다.");
        var granted = await HttpContext.RequestServices.GetRequiredService<CurrentPermissions>().GetAsync(HttpContext.RequestAborted);
        foreach (var func in funcs.Where(f => !granted.Can(programId, f)))
        {
            throw new PermissionDeniedException(programId, func);
        }
    }
}
