using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Portal.Infrastructure.Localization;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>메시지 템플릿 검색 조건.</summary>
public sealed class MsgTemplateSearch
{
    /// <summary>템플릿 ID·이름 일부.</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Keyword { get; set; }
}

/// <summary>메시지 템플릿 한 건 (<c>FW.USP_MSG_TEMPLATE_ADMIN_GET_LIST</c>).</summary>
public sealed class MsgTemplateDto
{
    /// <summary>템플릿 ID.</summary>
    public string TemplateId { get; set; } = "";

    /// <summary>관리용 이름.</summary>
    public string TemplateNm { get; set; } = "";

    /// <summary>메일 발송 여부.</summary>
    public string MailYn { get; set; } = "Y";

    /// <summary>포털 알림 여부.</summary>
    public string PortalYn { get; set; } = "N";

    /// <summary>사용 여부 (N이면 보낼 메시지를 쌓지 않는다).</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>포털 알림을 누르면 여는 화면.</summary>
    public string? LinkProgramId { get; set; }

    /// <summary>보내기 기다리는 메일 수.</summary>
    public int PendingCnt { get; set; }

    /// <summary>언어별 제목·본문.</summary>
    public List<MsgTemplateLangDto> Langs { get; set; } = [];

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

/// <summary>템플릿의 언어별 제목·본문.</summary>
public sealed class MsgTemplateLangDto
{
    /// <summary>템플릿 ID.</summary>
    public string TemplateId { get; set; } = "";

    /// <summary>언어.</summary>
    public string LangCd { get; set; } = "";

    /// <summary>제목.</summary>
    public string Subject { get; set; } = "";

    /// <summary>본문 (HTML).</summary>
    public string Body { get; set; } = "";
}

/// <summary>메시지 템플릿 수정 요청.</summary>
public sealed class MsgTemplateSaveRequest
{
    /// <summary>템플릿 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string TemplateId { get; set; } = "";

    /// <summary>조회 때 받은 값.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[]? RowVer { get; set; }

    /// <summary>메일 발송 여부.</summary>
    public string MailYn { get; set; } = "Y";

    /// <summary>포털 알림 여부.</summary>
    public string PortalYn { get; set; } = "N";

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>언어별 제목·본문.</summary>
    [MaxLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<MsgTemplateLangInput> Langs { get; set; } = [];
}

/// <summary>입력한 언어별 제목·본문.</summary>
public sealed class MsgTemplateLangInput
{
    /// <summary>언어.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(5, ErrorMessage = "MSG.INVALID_INPUT")]
    public string LangCd { get; set; } = "";

    /// <summary>제목 (줄바꿈 불가).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(200, ErrorMessage = "MSG.INVALID_INPUT")]
    public string Subject { get; set; } = "";

    /// <summary>본문 (HTML, 스크립트·이벤트 속성 불가).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(4000, ErrorMessage = "MSG.INVALID_INPUT")]
    public string Body { get; set; } = "";
}

/// <summary>저장 결과.</summary>
/// <param name="AffectedCnt">처리된 건수.</param>
public sealed record MsgTemplateSaveResult(int AffectedCnt);

/// <summary>메시지 템플릿 화면 표시 정보.</summary>
/// <param name="Languages">사용 중인 언어 (기본 언어 포함).</param>
public sealed record MsgTemplateIndexModel(IReadOnlyList<LanguageInfo> Languages)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>화면 스크립트에 넘길 언어 목록 (<c>data-langs</c>).</summary>
    public string LanguagesJson => JsonSerializer.Serialize(Languages, Web);
}
