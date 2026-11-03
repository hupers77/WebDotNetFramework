using System.ComponentModel.DataAnnotations;
using Portal.Framework.Web.Mvc;

namespace Portal.Web.Areas.Rpt.Models;

/// <summary>레포트 관리 SP가 던지는 메시지 키.</summary>
public static class ReportAdminMessageKeys
{
    /// <summary>이미 레포트로 등록된 프로그램.</summary>
    public const string Duplicate = "MSG.DUP_REPORT";

    /// <summary>만든 요청이 있어 지울 수 없다.</summary>
    public const string InUse = "MSG.REPORT_IN_USE";

    /// <summary>데이터 SP가 DB에 없다.</summary>
    public const string SpNotFound = "MSG.REPORT_SP_NOT_FOUND";
}

/// <summary>레포트 검색 조건.</summary>
public sealed class ReportAdminSearch
{
    /// <summary>레포트 ID·이름·데이터 SP 일부.</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Keyword { get; set; }
}

/// <summary>레포트 한 건 (<c>FW.USP_REPORT_ADMIN_GET_LIST</c>).</summary>
public sealed class ReportAdminDto
{
    /// <summary>레포트 ID (= 프로그램 ID).</summary>
    public string ReportId { get; set; } = "";

    /// <summary>프로그램 관리용 이름.</summary>
    public string ProgramNm { get; set; } = "";

    /// <summary>프로그램 주소.</summary>
    public string Url { get; set; } = "";

    /// <summary>개인정보 레포트 (프로그램 설정).</summary>
    public string PiiYn { get; set; } = "N";

    /// <summary>프로그램 사용 여부.</summary>
    public string ProgramUseYn { get; set; } = "Y";

    /// <summary>데이터 SP.</summary>
    public string DataSp { get; set; } = "";

    /// <summary>양식 뷰 이름 (비우면 기본 표).</summary>
    public string? TemplateView { get; set; }

    /// <summary>조건 정의 (JSON).</summary>
    public string? ParamsJson { get; set; }

    /// <summary>용지 방향 (P 세로, L 가로).</summary>
    public string OrientCd { get; set; } = "P";

    /// <summary>결재란 표시.</summary>
    public string ApprovalYn { get; set; } = "N";

    /// <summary>합계 행 표시.</summary>
    public string TotalYn { get; set; } = "N";

    /// <summary>최대 행 수.</summary>
    public int MaxRows { get; set; } = 5000;

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>지금까지 만든 요청 수.</summary>
    public int RunCnt { get; set; }

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

/// <summary>레포트로 등록할 수 있는 프로그램.</summary>
public sealed class ReportProgramOption
{
    /// <summary>프로그램 ID.</summary>
    public string ProgramId { get; set; } = "";

    /// <summary>관리용 이름.</summary>
    public string ProgramNm { get; set; } = "";
}

/// <summary>레포트 목록 응답.</summary>
public sealed class ReportAdminListResult
{
    /// <summary>레포트.</summary>
    public IReadOnlyList<ReportAdminDto> Items { get; set; } = [];

    /// <summary>등록할 수 있는 프로그램.</summary>
    public IReadOnlyList<ReportProgramOption> Programs { get; set; } = [];

    /// <summary>전체 건수.</summary>
    public int TotalCount { get; set; }
}

/// <summary>레포트 일괄 저장 요청.</summary>
public sealed class ReportSaveRequest
{
    /// <summary>바뀐 행.</summary>
    [MaxLength(200, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<ReportSaveRow> Rows { get; set; } = [];
}

/// <summary>바뀐 레포트 한 건.</summary>
public sealed class ReportSaveRow : IGridRow
{
    /// <summary>행 상태 (I/U/D).</summary>
    public string RowStat { get; set; } = "";

    /// <summary>레포트 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string ReportId { get; set; } = "";

    /// <summary>데이터 SP.</summary>
    [StringLength(128, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? DataSp { get; set; }

    /// <summary>양식 뷰 이름.</summary>
    [StringLength(50, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? TemplateView { get; set; }

    /// <summary>조건 정의 (JSON).</summary>
    [StringLength(2000, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ParamsJson { get; set; }

    /// <summary>용지 방향.</summary>
    public string OrientCd { get; set; } = "P";

    /// <summary>결재란.</summary>
    public string ApprovalYn { get; set; } = "N";

    /// <summary>합계 행.</summary>
    public string TotalYn { get; set; } = "N";

    /// <summary>최대 행 수.</summary>
    [Range(1, 100000, ErrorMessage = "MSG.INVALID_INPUT")]
    public int MaxRows { get; set; } = 5000;

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>조회 때 받은 값 (수정·삭제 필수).</summary>
    public byte[]? RowVer { get; set; }
}

/// <summary>저장 결과.</summary>
/// <param name="AffectedCnt">처리된 행 수.</param>
public sealed record ReportSaveResult(int AffectedCnt);
