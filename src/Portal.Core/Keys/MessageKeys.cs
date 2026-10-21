namespace Portal.Core.Keys;

/// <summary>공통 다국어 메시지 키. 문구는 <c>FW.TB_LANG_RESOURCE</c>에 언어별로 등록한다.</summary>
public static class MessageKeys
{
    /// <summary>저장되었습니다.</summary>
    public const string Saved = "MSG.SAVED";

    /// <summary>삭제되었습니다.</summary>
    public const string Deleted = "MSG.DELETED";

    /// <summary>이미 사용 중인 코드입니다.</summary>
    public const string DupCode = "MSG.DUP_CODE";

    /// <summary>다른 사용자가 먼저 수정했습니다. 다시 조회 후 저장하세요.</summary>
    public const string Concurrency = "MSG.CONCURRENCY";

    /// <summary>대상을 찾을 수 없습니다.</summary>
    public const string NotFound = "MSG.NOT_FOUND";

    /// <summary>권한이 없습니다.</summary>
    public const string Forbidden = "MSG.FORBIDDEN";

    /// <summary>로그인이 필요합니다.</summary>
    public const string LoginRequired = "MSG.LOGIN_REQUIRED";

    /// <summary>요청이 너무 많습니다. 잠시 후 다시 시도하세요.</summary>
    public const string TooManyRequests = "MSG.TOO_MANY_REQUESTS";

    /// <summary>일시적인 오류가 발생했습니다. (추적 ID 함께 표시)</summary>
    public const string SystemError = "MSG.SYSTEM_ERROR";

    /// <summary>ID 또는 비밀번호가 올바르지 않습니다.</summary>
    public const string LoginFailed = "MSG.LOGIN_FAILED";

    /// <summary>입력값이 올바르지 않습니다.</summary>
    public const string InvalidInput = "MSG.INVALID_INPUT";

    /// <summary>시스템 코드는 추가하거나 삭제할 수 없습니다.</summary>
    public const string SystemCode = "MSG.SYSTEM_CODE";

    /// <summary>변경된 내용이 없습니다.</summary>
    public const string NoChanges = "MSG.NO_CHANGES";
}
