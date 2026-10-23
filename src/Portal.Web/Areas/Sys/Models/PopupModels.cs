using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portal.Infrastructure.Localization;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>공지 팝업 목록 검색 조건.</summary>
public sealed class PopupSearch
{
    /// <summary>제목의 일부 (어느 언어든).</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Keyword { get; set; }

    /// <summary>사용 여부 (Y/N). 비우면 전체.</summary>
    [RegularExpression("^(Y|N)?$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string? UseYn { get; set; }

    /// <summary>쪽 번호 (1부터).</summary>
    [Range(1, 100000, ErrorMessage = "MSG.INVALID_INPUT")]
    public int PageNo { get; set; } = 1;
}

/// <summary>언어별 제목·내용.</summary>
public sealed class PopupLangDto
{
    /// <summary>언어 코드.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(5, ErrorMessage = "MSG.INVALID_INPUT")]
    public string LangCd { get; set; } = "";

    /// <summary>제목.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string Title { get; set; } = "";

    /// <summary>내용 (글자만, HTML 아님).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(2000, ErrorMessage = "MSG.INVALID_INPUT")]
    public string Content { get; set; } = "";
}

/// <summary>공지 팝업 한 건 (<c>FW.USP_POPUP_GET_LIST</c>).</summary>
public sealed class PopupDto
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>팝업 ID.</summary>
    public long PopupId { get; set; }

    /// <summary>공개 대상 (ALL/EMP/PARTNER).</summary>
    public string OpenScopeCd { get; set; } = "ALL";

    /// <summary>시작일 (yyyy-MM-dd, 포함).</summary>
    public string StartDt { get; set; } = "";

    /// <summary>종료일 (yyyy-MM-dd, 포함).</summary>
    public string EndDt { get; set; } = "";

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>제목 (화면 언어).</summary>
    public string Title { get; set; } = "";

    /// <summary>SP가 돌려주는 언어별 문구 JSON. 화면에는 <see cref="Langs"/>로 내보낸다.</summary>
    [JsonIgnore]
    public string? LangsJson { get; set; }

    /// <summary>언어별 제목·내용.</summary>
    public IReadOnlyList<PopupLangDto> Langs =>
        string.IsNullOrEmpty(LangsJson) ? [] : JsonSerializer.Deserialize<List<PopupLangDto>>(LangsJson, Web) ?? [];

    /// <summary>마지막 수정자.</summary>
    public string UpdId { get; set; } = "";

    /// <summary>마지막 수정 일시 (UTC).</summary>
    public DateTime UpdDt
    {
        get;
        set => field = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    /// <summary>동시성 확인용.</summary>
    public byte[] RowVer { get; set; } = [];
}

/// <summary>공지 팝업 등록·수정 요청. <see cref="RowVer"/>가 없으면 등록.</summary>
public sealed class PopupSaveRequest : IValidatableObject
{
    /// <summary>수정할 팝업 ID. 등록이면 비운다.</summary>
    public long? PopupId { get; set; }

    /// <summary>수정할 때 조회에서 받은 값.</summary>
    [Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[]? RowVer { get; set; }

    /// <summary>공개 대상 (ALL/EMP/PARTNER).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^(ALL|EMP|PARTNER)$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string OpenScopeCd { get; set; } = "ALL";

    /// <summary>시작일.</summary>
    public DateOnly StartDt { get; set; }

    /// <summary>종료일.</summary>
    public DateOnly EndDt { get; set; }

    /// <summary>사용 여부.</summary>
    [RegularExpression("^(Y|N)$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string UseYn { get; set; } = "Y";

    /// <summary>언어별 제목·내용. 기본 언어는 반드시 있어야 한다.</summary>
    [MinLength(1, ErrorMessage = "MSG.INVALID_INPUT"), MaxLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<PopupLangDto> Langs { get; set; } = [];

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDt > EndDt || StartDt.Year < 2000 || EndDt.Year > 2100 || (PopupId is null) != (RowVer is null))
        {
            yield return new ValidationResult("MSG.INVALID_INPUT");
        }
    }
}

/// <summary>공지 팝업 삭제 요청.</summary>
public sealed class PopupDeleteRequest
{
    /// <summary>팝업 ID.</summary>
    [Range(1, long.MaxValue, ErrorMessage = "MSG.INVALID_INPUT")]
    public long PopupId { get; set; }

    /// <summary>조회에서 받은 값.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[]? RowVer { get; set; }
}

/// <summary>저장 결과.</summary>
/// <param name="PopupId">저장된 팝업 ID.</param>
public sealed record PopupSaveResult(long PopupId);

/// <summary>공지 팝업 화면 모델.</summary>
/// <param name="Languages">사용 중인 언어 (기본 언어 포함).</param>
public sealed record PopupIndexModel(IReadOnlyList<LanguageInfo> Languages)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>화면 스크립트에 넘길 언어 목록 (<c>data-langs</c>).</summary>
    public string LanguagesJson => JsonSerializer.Serialize(Languages, Web);
}
