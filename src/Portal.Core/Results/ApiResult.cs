using System.Text.Json.Serialization;

namespace Portal.Core.Results;

/// <summary>
/// 화면 AJAX와 외부 API가 공통으로 쓰는 응답 형식(개발 표준 가이드 8장).
/// 성공: <c>{ success: true, data, traceId }</c>, 실패: <c>{ success: false, error: { code, message }, traceId }</c>.
/// </summary>
public class ApiResult
{
    /// <summary>처리 성공 여부.</summary>
    public bool Success { get; init; }

    /// <summary>실패 시 오류 정보. 성공이면 null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiError? Error { get; init; }

    /// <summary>요청 추적 ID. 프레임워크 필터가 응답 직전에 채운다.</summary>
    public string? TraceId { get; set; }

    /// <summary>데이터 없는 성공 응답.</summary>
    public static ApiResult Ok() => new() { Success = true };

    /// <summary>데이터를 담은 성공 응답.</summary>
    public static ApiResult<T> Ok<T>(T data) => new() { Success = true, Data = data };

    /// <summary>실패 응답. <paramref name="code"/>는 다국어 메시지 키, <paramref name="message"/>는 번역된 문구.</summary>
    public static ApiResult Fail(string code, string message) =>
        new() { Success = false, Error = new ApiError(code, message) };
}

/// <summary>데이터를 담는 응답.</summary>
/// <typeparam name="T">데이터 형식.</typeparam>
public sealed class ApiResult<T> : ApiResult
{
    /// <summary>응답 데이터. 값 형식(처리 건수 등)도 담을 수 있도록 null이어도 항상 내보낸다.</summary>
    public T? Data { get; init; }
}

/// <summary>오류 정보.</summary>
/// <param name="Code">다국어 메시지 키 (예: <c>MSG.DUP_CODE</c>).</param>
/// <param name="Message">사용자 언어로 번역된 메시지.</param>
public sealed record ApiError(string Code, string Message);
