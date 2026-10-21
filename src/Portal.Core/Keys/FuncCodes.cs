namespace Portal.Core.Keys;

/// <summary>기능(버튼) 코드. 공통코드 <c>FUNC_CD</c> 그룹의 시스템 값이다. 업무별 기능은 코드만 추가하면 된다.</summary>
public static class FuncCodes
{
    /// <summary>조회(화면 접근). 특성이 없는 Action의 기본값.</summary>
    public const string Read = "READ";

    /// <summary>등록.</summary>
    public const string Create = "CREATE";

    /// <summary>수정.</summary>
    public const string Update = "UPDATE";

    /// <summary>삭제.</summary>
    public const string Delete = "DELETE";

    /// <summary>내보내기(엑셀, PDF).</summary>
    public const string Export = "EXPORT";

    /// <summary>출력.</summary>
    public const string Print = "PRINT";

    /// <summary>일괄 업로드.</summary>
    public const string Upload = "UPLOAD";

    /// <summary>승인/확정.</summary>
    public const string Approve = "APPROVE";

    /// <summary>개인정보 원문 조회.</summary>
    public const string Pii = "PII";

    /// <summary>작업 수동 실행.</summary>
    public const string Exec = "EXEC";
}
