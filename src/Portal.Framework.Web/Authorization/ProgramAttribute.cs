namespace Portal.Framework.Web.Authorization;

/// <summary>
/// Controller(또는 Action)가 속한 프로그램 (<c>TB_PROGRAM.PROGRAM_ID</c>). 권한은 메뉴가 아닌 프로그램에 건다(설계서 6장).
/// <c>[AllowAnonymous]</c>가 아닌데 프로그램이 없는 Action은 권한 필터가 막는다(기본 거부).
/// </summary>
/// <param name="programId">프로그램 ID (예: <c>SYS_CODE_MGT</c>).</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class ProgramAttribute(string programId) : Attribute
{
    /// <summary>프로그램 ID.</summary>
    public string ProgramId { get; } = programId;
}

/// <summary>Action에 필요한 기능 코드 (<see cref="Core.Keys.FuncCodes"/>). 없으면 <c>READ</c>.</summary>
/// <param name="funcCd">기능 코드 (예: <c>UPDATE</c>).</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FuncAttribute(string funcCd) : Attribute
{
    /// <summary>기능 코드.</summary>
    public string FuncCd { get; } = funcCd;
}

/// <summary>로그인만 되어 있으면 쓸 수 있는 Action (로그아웃 등). 비밀번호 변경 강제 중에도 허용된다.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AuthenticatedOnlyAttribute : Attribute;

/// <summary>
/// 프로그램을 요청 데이터(첨부 묶음의 <c>PROGRAM_ID</c> 등)에서 찾아 Action이 직접 권한을 확인한다(<c>PortalController.DemandAnyFuncAsync</c>).
/// 권한 필터는 로그인과 비밀번호 변경 강제만 확인하므로, 이 표시가 붙은 Action은 반드시 권한을 확인해야 한다.
/// 공통 기능(첨부 파일)에만 쓰고, 업무 화면은 <see cref="ProgramAttribute"/>를 쓴다.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ProgramFromDataAttribute : Attribute;
