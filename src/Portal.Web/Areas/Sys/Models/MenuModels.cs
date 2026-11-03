using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Localization;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>메뉴 관리 SP가 던지는 메시지 키.</summary>
public static class MenuMessageKeys
{
    /// <summary>이미 있는 메뉴 키.</summary>
    public const string DuplicateKey = "MSG.DUP_MENU_KEY";

    /// <summary>하위 메뉴가 남아 있어 지울 수 없다.</summary>
    public const string HasChild = "MSG.MENU_HAS_CHILD";
}

/// <summary>메뉴 목록 검색 조건.</summary>
public sealed class MenuSearch
{
    /// <summary>메뉴 세트 (PORTAL/ADMIN/MOBILE).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string MenuSetCd { get; set; } = "PORTAL";
}

/// <summary>메뉴 한 건 (<c>FW.USP_MENU_ADMIN_GET_LIST</c>). 트리 순서로 온다.</summary>
public sealed class MenuAdminDto
{
    /// <summary>메뉴 키 (만든 뒤 바꿀 수 없다).</summary>
    public string MenuKey { get; set; } = "";

    /// <summary>부모 메뉴 키 (최상위면 비어 있다).</summary>
    public string? ParentMenuKey { get; set; }

    /// <summary>메뉴 세트.</summary>
    public string MenuSetCd { get; set; } = "";

    /// <summary>유형 (FOLDER/PAGE/LINK/POPUP).</summary>
    public string MenuTypeCd { get; set; } = "";

    /// <summary>연결 프로그램.</summary>
    public string? ProgramId { get; set; }

    /// <summary>링크 주소 (LINK).</summary>
    public string? LinkUrl { get; set; }

    /// <summary>아이콘 코드.</summary>
    public string? IconCd { get; set; }

    /// <summary>정렬 번호.</summary>
    public int SortNo { get; set; }

    /// <summary>새 창으로 열기.</summary>
    public string NewWinYn { get; set; } = "N";

    /// <summary>공개 대상 (ALL/EMP/PARTNER).</summary>
    public string OpenScopeCd { get; set; } = "ALL";

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>트리 깊이 (최상위 1).</summary>
    public int Depth { get; set; }

    /// <summary>언어별 메뉴명 (언어 코드 → 이름).</summary>
    public Dictionary<string, string> Names { get; set; } = new(StringComparer.Ordinal);

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

/// <summary>메뉴명 한 건.</summary>
public sealed class MenuNameRow
{
    /// <summary>메뉴 키.</summary>
    public string MenuKey { get; set; } = "";

    /// <summary>언어.</summary>
    public string LangCd { get; set; } = "";

    /// <summary>메뉴명.</summary>
    public string MenuNm { get; set; } = "";
}

/// <summary>고를 수 있는 프로그램.</summary>
public sealed class MenuProgramOption
{
    /// <summary>프로그램 ID.</summary>
    public string ProgramId { get; set; } = "";

    /// <summary>관리용 이름.</summary>
    public string ProgramNm { get; set; } = "";

    /// <summary>주소.</summary>
    public string Url { get; set; } = "";
}

/// <summary>메뉴 목록 응답.</summary>
public sealed class MenuListResult
{
    /// <summary>메뉴 (트리 순서).</summary>
    public IReadOnlyList<MenuAdminDto> Items { get; set; } = [];

    /// <summary>고를 수 있는 프로그램.</summary>
    public IReadOnlyList<MenuProgramOption> Programs { get; set; } = [];

    /// <summary>전체 건수.</summary>
    public int TotalCount { get; set; }
}

/// <summary>메뉴 일괄 저장 요청.</summary>
public sealed class MenuSaveRequest
{
    /// <summary>메뉴 세트.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string MenuSetCd { get; set; } = "PORTAL";

    /// <summary>바뀐 행.</summary>
    [MaxLength(500, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<MenuSaveRow> Rows { get; set; } = [];
}

/// <summary>바뀐 메뉴 한 건.</summary>
public sealed class MenuSaveRow : IGridRow
{
    /// <summary>행 상태 (I/U/D).</summary>
    public string RowStat { get; set; } = "";

    /// <summary>메뉴 키.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(50, ErrorMessage = "MSG.INVALID_INPUT")]
    public string MenuKey { get; set; } = "";

    /// <summary>부모 메뉴 키.</summary>
    [StringLength(50, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ParentMenuKey { get; set; }

    /// <summary>유형.</summary>
    [StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? MenuTypeCd { get; set; }

    /// <summary>프로그램 ID.</summary>
    [StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ProgramId { get; set; }

    /// <summary>링크 주소.</summary>
    [StringLength(500, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? LinkUrl { get; set; }

    /// <summary>아이콘.</summary>
    [StringLength(50, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? IconCd { get; set; }

    /// <summary>정렬 번호.</summary>
    [Range(0, 100000, ErrorMessage = "MSG.INVALID_INPUT")]
    public int SortNo { get; set; }

    /// <summary>새 창 여부.</summary>
    public string NewWinYn { get; set; } = "N";

    /// <summary>공개 대상.</summary>
    public string OpenScopeCd { get; set; } = "ALL";

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>언어별 메뉴명.</summary>
    public Dictionary<string, string?> Names { get; set; } = new(StringComparer.Ordinal);

    /// <summary>조회 때 받은 값 (수정·삭제 필수).</summary>
    public byte[]? RowVer { get; set; }
}

/// <summary>저장 결과.</summary>
/// <param name="AffectedCnt">처리된 행 수.</param>
public sealed record MenuSaveResult(int AffectedCnt);

/// <summary>메뉴 화면 표시 정보.</summary>
/// <param name="Languages">사용 중인 언어 (기본 언어 포함).</param>
public sealed record MenuIndexModel(IReadOnlyList<LanguageInfo> Languages)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>화면 스크립트에 넘길 언어 목록 (<c>data-langs</c>).</summary>
    public string LanguagesJson => JsonSerializer.Serialize(Languages, Web);
}
