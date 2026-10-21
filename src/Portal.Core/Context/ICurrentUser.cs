namespace Portal.Core.Context;

/// <summary>
/// 현재 요청의 사용자 정보. SpExecutor가 SP 공통 파라미터(<c>@P_USER_ID</c>, <c>@P_LANG_CD</c>, <c>@P_CLIENT_IP</c>)를
/// 여기서 채우므로, 화면이 보낸 값은 사용되지 않는다.
/// </summary>
public interface ICurrentUser
{
    /// <summary>로그인 여부.</summary>
    bool IsAuthenticated { get; }

    /// <summary>사용자 ID. 비로그인이면 null.</summary>
    string? UserId { get; }

    /// <summary>소속 회사 코드. 비로그인이면 null.</summary>
    string? CompanyCd { get; }

    /// <summary>사용자 유형(<c>EMP</c>/<c>PARTNER</c>/<c>SYSTEM</c>). 비로그인이면 null.</summary>
    string? UserTypeCd { get; }

    /// <summary>현재 언어 코드 (예: <c>ko</c>, <c>en</c>, <c>zh-CN</c>).</summary>
    string LangCd { get; }

    /// <summary>클라이언트 IP. 알 수 없으면 null.</summary>
    string? ClientIp { get; }
}
