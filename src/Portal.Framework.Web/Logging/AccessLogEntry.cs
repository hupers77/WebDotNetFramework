namespace Portal.Framework.Web.Logging;

/// <summary>접근 기록 한 건. 속성 순서가 <c>LOG.TVP_ACCESS_LOG</c> 컬럼 순서와 같아야 한다.</summary>
public sealed class AccessLogEntry
{
    /// <summary>배치 안 순서 (적재 직전에 채운다).</summary>
    public int SeqNo { get; set; }

    /// <summary>요청 시각 (UTC).</summary>
    public DateTime LogDt { get; set; }

    /// <summary>사용자 ID (로그인 전이면 null).</summary>
    public string? UserId { get; set; }

    /// <summary>프로그램 ID.</summary>
    public string? ProgramId { get; set; }

    /// <summary>기능 코드.</summary>
    public string? FuncCd { get; set; }

    /// <summary>HTTP 메서드.</summary>
    public string HttpMethod { get; set; } = "";

    /// <summary>요청 경로 (쿼리 문자열 제외: 검색어·토큰 같은 값이 남지 않도록).</summary>
    public string RequestPath { get; set; } = "";

    /// <summary>응답 상태 코드.</summary>
    public short StatusCode { get; set; }

    /// <summary>처리 시간(ms).</summary>
    public int ElapsedMs { get; set; }

    /// <summary>클라이언트 IP.</summary>
    public string? ClientIp { get; set; }

    /// <summary>추적 ID.</summary>
    public string? TraceId { get; set; }
}
