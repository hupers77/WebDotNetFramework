using System.ComponentModel.DataAnnotations;
using Portal.Core.Keys;

namespace Portal.Web.Areas.Log.Models;

/// <summary>로그 조회 SP가 던지는 메시지 키.</summary>
public static class LogMessageKeys
{
    /// <summary>기간이 비었거나 시작이 끝보다 늦거나 31일을 넘는다.</summary>
    public const string Period = "MSG.LOG_PERIOD";

    /// <summary>내려받을 행이 너무 많다.</summary>
    public const string ExportTooMany = "MSG.LOG_EXPORT_TOO_MANY";
}

/// <summary>로그인·접근 기록 종류.</summary>
public static class AccessLogKinds
{
    /// <summary>로그인·로그아웃 (<c>LOG.TB_LOGIN_LOG</c>).</summary>
    public const string Login = "LOGIN";

    /// <summary>화면·API 접근 (<c>LOG.TB_ACCESS_LOG</c>).</summary>
    public const string Access = "ACCESS";
}

/// <summary>로그인·접근 기록 검색 조건. 기간은 브라우저가 사용자 시간대의 날짜 경계를 UTC로 바꿔 보낸다.</summary>
public sealed class AccessLogSearch
{
    /// <summary><see cref="AccessLogKinds"/>.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^(LOGIN|ACCESS)$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string Kind { get; set; } = AccessLogKinds.Login;

    /// <summary>시작 (포함).</summary>
    [Required(ErrorMessage = LogMessageKeys.Period)]
    public DateTimeOffset? FromDt { get; set; }

    /// <summary>끝 (제외). 시작부터 31일까지.</summary>
    [Required(ErrorMessage = LogMessageKeys.Period)]
    public DateTimeOffset? ToDt { get; set; }

    /// <summary>사용자 ID (정확히 일치).</summary>
    [StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? UserId { get; set; }

    /// <summary>로그인 결과 (공통코드 LOGIN_RESULT, 로그인 기록만).</summary>
    [StringLength(20, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ResultCd { get; set; }

    /// <summary>프로그램 (접근 기록만).</summary>
    [StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ProgramId { get; set; }

    /// <summary>Y면 응답 코드 400 이상만 (접근 기록만).</summary>
    [RegularExpression("^[YN]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ErrorYn { get; set; }

    /// <summary>접속 IP (정확히 일치).</summary>
    [StringLength(45, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Ip { get; set; }

    /// <summary>쪽 번호.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "MSG.INVALID_INPUT")]
    public int PageNo { get; set; } = 1;
}

/// <summary>로그인·접근 기록 엑셀 내보내기 요청. 접속 IP가 들어 있어 사유를 묻고 개인정보 접속기록에 남긴다.</summary>
public sealed class AccessLogExportRequest
{
    /// <summary>목록과 같은 검색 조건 (쪽 번호는 쓰지 않는다).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT")]
    public AccessLogSearch Search { get; set; } = new();

    /// <summary>다운로드 사유.</summary>
    [Required(ErrorMessage = MessageKeys.PiiReasonRequired), StringLength(200, MinimumLength = 2, ErrorMessage = MessageKeys.PiiReasonRequired)]
    public string Reason { get; set; } = "";
}

/// <summary>로그인 기록 (<c>LOG.USP_LOGIN_LOG_GET_LIST</c>).</summary>
public sealed class LoginLogDto
{
    /// <summary>로그 ID.</summary>
    public long LogId { get; set; }

    /// <summary>일시 (UTC).</summary>
    public DateTime LogDt { get; set; }

    /// <summary>사용자 ID (없는 ID로 시도했으면 입력값).</summary>
    public string? UserId { get; set; }

    /// <summary>결과 코드.</summary>
    public string LoginResultCd { get; set; } = "";

    /// <summary>결과 이름.</summary>
    public string LoginResultNm { get; set; } = "";

    /// <summary>인증 방식 (FORM/FORM_MFA/AD/AD_LDAP).</summary>
    public string AuthMethodCd { get; set; } = "";

    /// <summary>접속 IP.</summary>
    public string? ClientIp { get; set; }

    /// <summary>브라우저.</summary>
    public string? UserAgent { get; set; }
}

/// <summary>화면·API 접근 기록 (<c>LOG.USP_ACCESS_LOG_GET_LIST</c>).</summary>
public sealed class AccessLogDto
{
    /// <summary>로그 ID.</summary>
    public long LogId { get; set; }

    /// <summary>일시 (UTC).</summary>
    public DateTime LogDt { get; set; }

    /// <summary>사용자 ID (로그인 전 요청이면 없음).</summary>
    public string? UserId { get; set; }

    /// <summary>프로그램 ID.</summary>
    public string? ProgramId { get; set; }

    /// <summary>화면 이름 (메뉴명, 서비스가 채운다).</summary>
    public string? ProgramNm { get; set; }

    /// <summary>기능 코드.</summary>
    public string? FuncCd { get; set; }

    /// <summary>HTTP 메서드.</summary>
    public string HttpMethod { get; set; } = "";

    /// <summary>요청 경로 (쿼리 문자열 제외).</summary>
    public string RequestPath { get; set; } = "";

    /// <summary>응답 코드.</summary>
    public int StatusCode { get; set; }

    /// <summary>처리 시간 (ms).</summary>
    public int ElapsedMs { get; set; }

    /// <summary>접속 IP.</summary>
    public string? ClientIp { get; set; }

    /// <summary>추적 ID (오류 로그와 연결).</summary>
    public string? TraceId { get; set; }
}

/// <summary>화면 모델.</summary>
/// <param name="Results">로그인 결과 (코드, 이름).</param>
/// <param name="Programs">메뉴에 있는 프로그램 (ID, 화면 이름).</param>
public sealed record AccessLogIndexModel(IReadOnlyList<(string Code, string Name)> Results, IReadOnlyList<(string Code, string Name)> Programs);
