using System.ComponentModel.DataAnnotations;
using Portal.Framework.Web.Mvc;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>휴일 목록 검색 조건.</summary>
public sealed class HolidaySearch
{
    /// <summary>조회 연도.</summary>
    [Range(1990, 2100, ErrorMessage = "MSG.INVALID_INPUT")]
    public int Year { get; set; }
}

/// <summary>휴일·달력 한 건 (<c>FW.USP_HOLIDAY_GET_LIST</c>).</summary>
public sealed class HolidayDto
{
    /// <summary>날짜 (yyyy-MM-dd).</summary>
    public string HolidayDt { get; set; } = "";

    /// <summary>이름.</summary>
    public string HolidayNm { get; set; } = "";

    /// <summary>Y면 주말에 일하는 날(대체 근무일), N이면 쉬는 날.</summary>
    public string WorkYn { get; set; } = "N";

    /// <summary>요일 번호 (0=일 … 6=토).</summary>
    public int WeekdayNo { get; set; }

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

/// <summary>휴일 일괄 저장 요청.</summary>
public sealed class HolidaySaveRequest
{
    /// <summary>바뀐 행.</summary>
    [MaxLength(500, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<HolidaySaveRow> Rows { get; set; } = [];
}

/// <summary>바뀐 휴일 한 건.</summary>
public sealed class HolidaySaveRow : IGridRow
{
    /// <summary>행 상태 (I/U/D).</summary>
    public string RowStat { get; set; } = "";

    /// <summary>날짜 (yyyy-MM-dd).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string HolidayDt { get; set; } = "";

    /// <summary>이름 (삭제 행은 비어도 된다).</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? HolidayNm { get; set; }

    /// <summary>Y면 주말 근무일.</summary>
    public string WorkYn { get; set; } = "N";

    /// <summary>조회 때 받은 값 (수정·삭제 필수).</summary>
    public byte[]? RowVer { get; set; }
}

/// <summary>저장 결과.</summary>
/// <param name="AffectedCnt">처리된 행 수.</param>
public sealed record HolidaySaveResult(int AffectedCnt);
