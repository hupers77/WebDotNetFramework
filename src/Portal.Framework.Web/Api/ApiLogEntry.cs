namespace Portal.Framework.Web.Api;

/// <summary>API 기록 한 건 (<c>LOG.TB_API_LOG</c>). 본문은 이미 민감 값을 가린 것이어야 한다.</summary>
public sealed class ApiLogEntry
{
    /// <summary>시각 (UTC).</summary>
    public DateTime LogDt { get; set; }

    /// <summary>방향: <c>IN</c> 수신, <c>OUT</c> 송신.</summary>
    public string DirectionCd { get; set; } = "IN";

    /// <summary>클라이언트 ID (수신) 또는 송신 대상 키 (송신).</summary>
    public string? ClientId { get; set; }

    /// <summary>HTTP 메서드.</summary>
    public string HttpMethod { get; set; } = "";

    /// <summary>주소 (쿼리 값은 가린 것).</summary>
    public string Url { get; set; } = "";

    /// <summary>응답 상태 코드.</summary>
    public short? StatusCode { get; set; }

    /// <summary>처리 시간(ms).</summary>
    public int? ElapsedMs { get; set; }

    /// <summary>요청 본문 (가린 것).</summary>
    public string? RequestBody { get; set; }

    /// <summary>응답 본문 (가린 것).</summary>
    public string? ResponseBody { get; set; }

    /// <summary>추적 ID.</summary>
    public string? TraceId { get; set; }
}
