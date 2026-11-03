using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Localization;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>다국어 관리 SP가 던지는 메시지 키.</summary>
public static class LangMessageKeys
{
    /// <summary>이미 있는 문구 키.</summary>
    public const string DuplicateKey = "MSG.DUP_LANG_KEY";
}

/// <summary>다국어 문구 검색 조건.</summary>
public sealed class LangSearch
{
    /// <summary>키·문구 일부.</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Keyword { get; set; }

    /// <summary>종류 (LABEL/MSG/BTN/TOOLTIP). 비우면 전체.</summary>
    [StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ResType { get; set; }

    /// <summary>Y면 사용 중인 언어 중 빠진 문구가 있는 키만.</summary>
    [RegularExpression("^[YN]?$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string? MissingYn { get; set; }

    /// <summary>쪽 번호 (1부터).</summary>
    [Range(1, 100000, ErrorMessage = "MSG.INVALID_INPUT")]
    public int PageNo { get; set; } = 1;
}

/// <summary>문구 한 줄 (키 × 언어, <c>FW.USP_LANG_RESOURCE_ADMIN_GET_LIST</c> 결과셋 1).</summary>
public sealed class LangResourceRow
{
    /// <summary>키.</summary>
    public string ResKey { get; set; } = "";

    /// <summary>언어.</summary>
    public string LangCd { get; set; } = "";

    /// <summary>문구.</summary>
    public string ResText { get; set; } = "";

    /// <summary>종류.</summary>
    public string ResType { get; set; } = "LABEL";

    /// <summary>키 단위 동시성 확인값 (그 키 언어 행 중 가장 큰 ROW_VER).</summary>
    public byte[] KeyVer { get; set; } = [];
}

/// <summary>문구 키 한 건 (언어별 문구를 한 행에 모았다).</summary>
public sealed class LangResourceDto
{
    /// <summary>키.</summary>
    public string ResKey { get; set; } = "";

    /// <summary>종류.</summary>
    public string ResType { get; set; } = "LABEL";

    /// <summary>언어별 문구 (언어 코드 → 문구).</summary>
    public Dictionary<string, string> Texts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>동시성 확인용.</summary>
    public byte[] RowVer { get; set; } = [];
}

/// <summary>문구 일괄 저장 요청.</summary>
public sealed class LangSaveRequest
{
    /// <summary>바뀐 행.</summary>
    [MaxLength(500, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<LangSaveRow> Rows { get; set; } = [];
}

/// <summary>바뀐 문구 키 한 건.</summary>
public sealed class LangSaveRow : IGridRow
{
    /// <summary>행 상태 (I/U/D).</summary>
    public string RowStat { get; set; } = "";

    /// <summary>키.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string ResKey { get; set; } = "";

    /// <summary>종류.</summary>
    [StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ResType { get; set; }

    /// <summary>언어별 문구.</summary>
    public Dictionary<string, string?> Texts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>조회 때 받은 값 (수정·삭제 필수).</summary>
    public byte[]? RowVer { get; set; }
}

/// <summary>저장 결과.</summary>
/// <param name="AffectedCnt">처리된 키 수.</param>
public sealed record LangSaveResult(int AffectedCnt);

/// <summary>엑셀 업로드 미리보기 결과.</summary>
public sealed class LangUploadResult
{
    /// <summary>그리드에 반영할 행 (추가 I, 수정 U).</summary>
    public List<LangSaveRow> Rows { get; init; } = [];

    /// <summary>행별 오류 (이 행들은 <see cref="Rows"/>에 없다).</summary>
    public List<LangUploadError> Errors { get; init; } = [];

    /// <summary>현재 값과 같아 건너뛴 행 수.</summary>
    public int UnchangedCnt { get; set; }
}

/// <summary>엑셀 업로드 행 오류.</summary>
/// <param name="RowNo">엑셀 행 번호.</param>
/// <param name="Message">사용자 언어 메시지.</param>
public sealed record LangUploadError(int RowNo, string Message);

/// <summary>다국어 화면 표시 정보.</summary>
/// <param name="Languages">사용 중인 언어 (기본 언어 포함).</param>
public sealed record LangIndexModel(IReadOnlyList<LanguageInfo> Languages)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>화면 스크립트에 넘길 언어 목록 (<c>data-langs</c>).</summary>
    public string LanguagesJson => JsonSerializer.Serialize(Languages, Web);
}
