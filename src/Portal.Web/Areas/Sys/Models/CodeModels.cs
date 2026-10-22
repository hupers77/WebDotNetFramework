using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Portal.Core.Results;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Localization;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>공통코드 그룹 (<c>FW.USP_CODE_GROUP_GET_LIST</c>).</summary>
public sealed class CodeGroupDto
{
    /// <summary>코드 그룹 ID.</summary>
    public string GroupCd { get; set; } = "";

    /// <summary>그룹명 (사용자 언어).</summary>
    public string GroupNm { get; set; } = "";

    /// <summary>Y면 코드값을 추가·삭제할 수 없다 (<see cref="AllowAddYn"/>가 Y면 추가는 된다).</summary>
    public string SystemYn { get; set; } = "N";

    /// <summary>시스템 그룹이어도 값 추가를 허용하는지.</summary>
    public string AllowAddYn { get; set; } = "Y";

    /// <summary>추가 속성의 의미. 비어 있으면 그 속성은 화면에 보이지 않는다.</summary>
    public string? Attr1Nm { get; set; }

    /// <summary>속성2의 의미.</summary>
    public string? Attr2Nm { get; set; }

    /// <summary>속성3의 의미.</summary>
    public string? Attr3Nm { get; set; }

    /// <summary>속성4의 의미.</summary>
    public string? Attr4Nm { get; set; }

    /// <summary>속성5의 의미.</summary>
    public string? Attr5Nm { get; set; }

    /// <summary>사용 여부 (Y/N).</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>코드 값 개수 (사용 안 함 포함).</summary>
    public int CodeCnt { get; set; }
}

/// <summary>공통코드 값 (<c>FW.USP_CODE_GET_LIST</c> + 언어별 명칭).</summary>
public sealed class CodeDto
{
    /// <summary>코드값.</summary>
    public string Code { get; set; } = "";

    /// <summary>사용자 언어의 명칭 (없으면 기본 언어).</summary>
    public string CodeNm { get; set; } = "";

    /// <summary>상위 코드 (같은 그룹 안의 계층).</summary>
    public string? ParentCode { get; set; }

    /// <summary>정렬 순서.</summary>
    public int SortNo { get; set; }

    /// <summary>추가 속성1 (의미는 그룹의 속성명).</summary>
    public string? Attr1 { get; set; }

    /// <summary>추가 속성2.</summary>
    public string? Attr2 { get; set; }

    /// <summary>추가 속성3.</summary>
    public string? Attr3 { get; set; }

    /// <summary>추가 속성4.</summary>
    public string? Attr4 { get; set; }

    /// <summary>추가 속성5.</summary>
    public string? Attr5 { get; set; }

    /// <summary>유효 시작일.</summary>
    public DateOnly StartDt { get; set; }

    /// <summary>유효 종료일.</summary>
    public DateOnly EndDt { get; set; }

    /// <summary>사용 여부 (Y/N).</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>동시성 확인용. 저장할 때 그대로 돌려보낸다.</summary>
    public byte[] RowVer { get; set; } = [];

    /// <summary>언어 코드 → 명칭 (사용 중인 언어만).</summary>
    public Dictionary<string, string> Names { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>코드 그룹 하나의 코드 목록. 그리드 일괄 편집이라 한 번에 최대 <see cref="Services.CodeAdminService.MaxRows"/>개까지 읽는다.</summary>
public sealed class CodeListDto
{
    /// <summary>코드 목록 (전체 건수가 읽은 개수보다 많으면 화면이 안내한다).</summary>
    public required PageResult<CodeDto> Codes { get; init; }
}

/// <summary>그리드 일괄 저장 요청.</summary>
public sealed class CodeSaveRequest
{
    /// <summary>코드 그룹 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT")]
    [StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string GroupCd { get; set; } = "";

    /// <summary>변경된 행 (추가 I, 수정 U, 삭제 D).</summary>
    [MaxLength(Services.CodeAdminService.MaxRows, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<CodeSaveRow> Rows { get; set; } = [];
}

/// <summary>그리드의 변경 행 (<c>fw.grid</c>의 <c>getChanges()</c>).</summary>
public sealed class CodeSaveRow : IGridRow
{
    /// <summary>행 상태 (I/U/D).</summary>
    public string RowStat { get; set; } = "";

    /// <summary>코드값.</summary>
    public string Code { get; set; } = "";

    /// <summary>언어 코드 → 명칭. 추가 행은 기본 언어 명칭이 필수다. 비운 언어는 명칭을 지운다.</summary>
    public Dictionary<string, string?> Names { get; set; } = new(StringComparer.Ordinal);

    /// <summary>상위 코드 (같은 그룹 안의 계층).</summary>
    public string? ParentCode { get; set; }

    /// <summary>정렬 순서.</summary>
    public int? SortNo { get; set; }

    /// <summary>추가 속성1 (의미는 그룹의 속성명).</summary>
    public string? Attr1 { get; set; }

    /// <summary>추가 속성2.</summary>
    public string? Attr2 { get; set; }

    /// <summary>추가 속성3.</summary>
    public string? Attr3 { get; set; }

    /// <summary>추가 속성4.</summary>
    public string? Attr4 { get; set; }

    /// <summary>추가 속성5.</summary>
    public string? Attr5 { get; set; }

    /// <summary>유효 시작일.</summary>
    public DateOnly? StartDt { get; set; }

    /// <summary>유효 종료일.</summary>
    public DateOnly? EndDt { get; set; }

    /// <summary>사용 여부 (Y/N).</summary>
    public string? UseYn { get; set; }

    /// <summary>조회 때 받은 값 (수정·삭제 행 필수).</summary>
    public byte[]? RowVer { get; set; }
}

/// <summary>공통코드 화면 모델: 명칭 컬럼을 사용 중인 언어 수만큼 만든다.</summary>
/// <param name="Languages">사용 중인 언어 (기본 언어 포함).</param>
public sealed record CodeIndexModel(IReadOnlyList<LanguageInfo> Languages)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>화면 스크립트에 넘길 언어 목록 (<c>data-langs</c>).</summary>
    public string LanguagesJson => JsonSerializer.Serialize(Languages, Web);
}
