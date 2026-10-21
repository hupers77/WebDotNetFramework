using Microsoft.AspNetCore.Mvc;
using Portal.Core.Results;

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
}
