using System.ComponentModel.DataAnnotations;
using Portal.Framework.Web.Mvc;

namespace Portal.Web.Areas.Board.Models;

/// <summary>게시판 설정 한 건 (<c>FW.USP_BOARD_ADMIN_GET_LIST</c>).</summary>
public sealed class BoardAdminDto
{
    /// <summary>게시판 코드.</summary>
    public string BoardCd { get; set; } = "";

    /// <summary>권한 기준 프로그램.</summary>
    public string ProgramId { get; set; } = "";

    /// <summary>유형 (공통코드 BOARD_TYPE).</summary>
    public string BoardTypeCd { get; set; } = "";

    /// <summary>한국어 이름.</summary>
    public string? NameKo { get; set; }

    /// <summary>영어 이름.</summary>
    public string? NameEn { get; set; }

    /// <summary>중국어 이름.</summary>
    public string? NameZh { get; set; }

    /// <summary>답글.</summary>
    public string ReplyYn { get; set; } = "N";

    /// <summary>댓글.</summary>
    public string CommentYn { get; set; } = "N";

    /// <summary>첨부.</summary>
    public string AttachYn { get; set; } = "N";

    /// <summary>익명.</summary>
    public string AnonYn { get; set; } = "N";

    /// <summary>비밀글.</summary>
    public string SecretYn { get; set; } = "N";

    /// <summary>게시기간.</summary>
    public string PeriodYn { get; set; } = "N";

    /// <summary>상단 고정.</summary>
    public string PinYn { get; set; } = "N";

    /// <summary>조회자 목록.</summary>
    public string ReaderYn { get; set; } = "N";

    /// <summary>메뉴 정렬 순서.</summary>
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

/// <summary>게시판 설정 일괄 저장 요청 (수정 행만).</summary>
public sealed class BoardAdminSaveRequest
{
    /// <summary>바뀐 게시판.</summary>
    [MaxLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<BoardAdminSaveRow> Rows { get; set; } = [];
}

/// <summary>바뀐 게시판 한 건.</summary>
public sealed class BoardAdminSaveRow : IGridRow
{
    /// <summary>행 상태 (U만 허용).</summary>
    public string RowStat { get; set; } = "";

    /// <summary>게시판 코드.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string BoardCd { get; set; } = "";

    /// <summary>한국어 이름.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string NameKo { get; set; } = "";

    /// <summary>영어 이름.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string NameEn { get; set; } = "";

    /// <summary>중국어 이름.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string NameZh { get; set; } = "";

    /// <summary>답글.</summary>
    public string ReplyYn { get; set; } = "N";

    /// <summary>댓글.</summary>
    public string CommentYn { get; set; } = "N";

    /// <summary>첨부.</summary>
    public string AttachYn { get; set; } = "N";

    /// <summary>익명.</summary>
    public string AnonYn { get; set; } = "N";

    /// <summary>비밀글.</summary>
    public string SecretYn { get; set; } = "N";

    /// <summary>게시기간.</summary>
    public string PeriodYn { get; set; } = "N";

    /// <summary>상단 고정.</summary>
    public string PinYn { get; set; } = "N";

    /// <summary>조회자 목록.</summary>
    public string ReaderYn { get; set; } = "N";

    /// <summary>메뉴 정렬 순서.</summary>
    [Range(0, 999999, ErrorMessage = "MSG.INVALID_INPUT")]
    public int SortNo { get; set; }

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>조회 때 받은 값.</summary>
    public byte[]? RowVer { get; set; }
}

/// <summary>저장 결과.</summary>
/// <param name="AffectedCnt">처리된 게시판 수.</param>
public sealed record BoardAdminSaveResult(int AffectedCnt);
