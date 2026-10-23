using System.ComponentModel.DataAnnotations;
using Portal.Framework.Web.Mvc;

namespace Portal.Web.Areas.Dash.Models;

/// <summary>위젯 설정 한 건 (<c>FW.USP_WIDGET_ADMIN_GET_LIST</c>).</summary>
public sealed class WidgetAdminDto
{
    /// <summary>위젯 코드.</summary>
    public string WidgetCd { get; set; } = "";

    /// <summary>표시 유형.</summary>
    public string TypeCd { get; set; } = "";

    /// <summary>데이터 SP (관리 화면에서만 보인다).</summary>
    public string DataSp { get; set; } = "";

    /// <summary>읽기 권한을 확인할 프로그램.</summary>
    public string? ProgramId { get; set; }

    /// <summary>한국어 제목.</summary>
    public string? NameKo { get; set; }

    /// <summary>영어 제목.</summary>
    public string? NameEn { get; set; }

    /// <summary>중국어 제목.</summary>
    public string? NameZh { get; set; }

    /// <summary>12칸 그리드에서 차지하는 칸 수.</summary>
    public int SpanCols { get; set; }

    /// <summary>캐시 시간(초).</summary>
    public int CacheSec { get; set; }

    /// <summary>홈 화면 정렬 순서.</summary>
    public int SortNo { get; set; }

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

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

/// <summary>위젯 설정 일괄 저장 요청 (수정 행만).</summary>
public sealed class WidgetAdminSaveRequest
{
    /// <summary>바뀐 위젯.</summary>
    [MaxLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<WidgetAdminSaveRow> Rows { get; set; } = [];
}

/// <summary>바뀐 위젯 한 건.</summary>
public sealed class WidgetAdminSaveRow : IGridRow
{
    /// <summary>행 상태 (U만 허용).</summary>
    public string RowStat { get; set; } = "";

    /// <summary>위젯 코드.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string WidgetCd { get; set; } = "";

    /// <summary>한국어 제목.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string NameKo { get; set; } = "";

    /// <summary>영어 제목.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string NameEn { get; set; } = "";

    /// <summary>중국어 제목.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string NameZh { get; set; } = "";

    /// <summary>칸 수 (1~12).</summary>
    [Range(1, 12, ErrorMessage = "MSG.INVALID_INPUT")]
    public int SpanCols { get; set; } = 6;

    /// <summary>캐시 시간(초, 0~86400).</summary>
    [Range(0, 86400, ErrorMessage = "MSG.INVALID_INPUT")]
    public int CacheSec { get; set; }

    /// <summary>홈 화면 정렬 순서.</summary>
    [Range(0, 999999, ErrorMessage = "MSG.INVALID_INPUT")]
    public int SortNo { get; set; }

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>조회 때 받은 값.</summary>
    public byte[]? RowVer { get; set; }
}

/// <summary>저장 결과.</summary>
/// <param name="AffectedCnt">처리된 위젯 수.</param>
public sealed record WidgetAdminSaveResult(int AffectedCnt);
