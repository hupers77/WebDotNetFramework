using System.ComponentModel.DataAnnotations;

namespace Portal.Web.Areas.Board.Models;

/// <summary>게시판 설정 (<c>FW.USP_BOARD_GET</c>). 켜지 않은 기능은 화면에 나타나지 않고 SP도 거절한다.</summary>
public sealed class BoardDto
{
    /// <summary>게시판 코드 (화면 주소의 마지막 마디).</summary>
    public string BoardCd { get; set; } = "";

    /// <summary>권한 기준 프로그램.</summary>
    public string ProgramId { get; set; } = "";

    /// <summary>유형 (공통코드 BOARD_TYPE).</summary>
    public string BoardTypeCd { get; set; } = "NORMAL";

    /// <summary>게시판 이름 (화면 언어).</summary>
    public string BoardNm { get; set; } = "";

    /// <summary>답글.</summary>
    public string ReplyYn { get; set; } = "N";

    /// <summary>댓글.</summary>
    public string CommentYn { get; set; } = "N";

    /// <summary>첨부.</summary>
    public string AttachYn { get; set; } = "N";

    /// <summary>익명 (관리자 외에는 작성자를 숨긴다).</summary>
    public string AnonYn { get; set; } = "N";

    /// <summary>비밀글.</summary>
    public string SecretYn { get; set; } = "N";

    /// <summary>게시기간.</summary>
    public string PeriodYn { get; set; } = "N";

    /// <summary>상단 고정.</summary>
    public string PinYn { get; set; } = "N";

    /// <summary>조회자 목록.</summary>
    public string ReaderYn { get; set; } = "N";
}

/// <summary>게시글 목록 조건.</summary>
public sealed class PostSearch
{
    /// <summary>검색 대상 (TITLE/CONTENT/TITLE_CONTENT/WRITER).</summary>
    [RegularExpression("^(TITLE|CONTENT|TITLE_CONTENT|WRITER)$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string SearchType { get; set; } = "TITLE_CONTENT";

    /// <summary>검색어.</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Keyword { get; set; }

    /// <summary>쪽 번호 (1부터).</summary>
    [Range(1, 100000, ErrorMessage = "MSG.INVALID_INPUT")]
    public int PageNo { get; set; } = 1;

    /// <summary>쪽 크기.</summary>
    [Range(1, 100, ErrorMessage = "MSG.INVALID_INPUT")]
    public int PageSize { get; set; } = 20;
}

/// <summary>게시글 목록 한 줄 (<c>FW.USP_POST_GET_LIST</c>). 볼 수 없는 비밀글은 <see cref="Title"/>이 null이다.</summary>
public sealed class PostRowDto
{
    /// <summary>글 ID.</summary>
    public long PostId { get; set; }

    /// <summary>원글 ID (원글은 자기 자신).</summary>
    public long RootPostId { get; set; }

    /// <summary>답글이 달린 글.</summary>
    public long? ParentPostId { get; set; }

    /// <summary>답글 단계 (원글 0).</summary>
    public int Depth { get; set; }

    /// <summary>제목. 볼 수 없는 비밀글이면 null.</summary>
    public string? Title { get; set; }

    /// <summary>비밀글 여부.</summary>
    public string SecretYn { get; set; } = "N";

    /// <summary>상단 고정 여부.</summary>
    public string PinYn { get; set; } = "N";

    /// <summary>작성자 회사.</summary>
    public string CompanyCd { get; set; } = "";

    /// <summary>조회 수.</summary>
    public int ViewCnt { get; set; }

    /// <summary>작성자 이름. 익명 게시판이면 null.</summary>
    public string? WriterNm { get; set; }

    /// <summary>내가 쓴 글이면 Y.</summary>
    public string MineYn { get; set; } = "N";

    /// <summary>첨부가 있으면 Y.</summary>
    public string HasFileYn { get; set; } = "N";

    /// <summary>댓글 수.</summary>
    public int CommentCnt { get; set; }

    /// <summary>게시 시작일.</summary>
    public DateOnly? StartDt { get; set; }

    /// <summary>게시 종료일.</summary>
    public DateOnly? EndDt { get; set; }

    /// <summary>등록 시각 (UTC).</summary>
    public DateTime RegDt { get; set; }
}

/// <summary>게시글 한 건 (<c>FW.USP_POST_GET</c>).</summary>
public sealed class PostDto
{
    /// <summary>글 ID.</summary>
    public long PostId { get; set; }

    /// <summary>게시판 코드.</summary>
    public string BoardCd { get; set; } = "";

    /// <summary>원글 ID.</summary>
    public long RootPostId { get; set; }

    /// <summary>답글이 달린 글.</summary>
    public long? ParentPostId { get; set; }

    /// <summary>답글 단계.</summary>
    public int Depth { get; set; }

    /// <summary>제목.</summary>
    public string Title { get; set; } = "";

    /// <summary>내용 (저장 시 거른 HTML).</summary>
    public string Content { get; set; } = "";

    /// <summary>작성자 회사.</summary>
    public string CompanyCd { get; set; } = "";

    /// <summary>작성자 이름. 익명 게시판이면 null.</summary>
    public string? WriterNm { get; set; }

    /// <summary>내가 쓴 글이면 Y.</summary>
    public string MineYn { get; set; } = "N";

    /// <summary>비밀글.</summary>
    public string SecretYn { get; set; } = "N";

    /// <summary>상단 고정.</summary>
    public string PinYn { get; set; } = "N";

    /// <summary>게시 시작일.</summary>
    public DateOnly? StartDt { get; set; }

    /// <summary>게시 종료일.</summary>
    public DateOnly? EndDt { get; set; }

    /// <summary>첨부 묶음 ID.</summary>
    public long? FileGroupId { get; set; }

    /// <summary>조회 수.</summary>
    public int ViewCnt { get; set; }

    /// <summary>등록 시각 (UTC).</summary>
    public DateTime RegDt { get; set; }

    /// <summary>수정 시각 (UTC).</summary>
    public DateTime UpdDt { get; set; }

    /// <summary>동시 수정 확인 값.</summary>
    public byte[] RowVer { get; set; } = [];
}

/// <summary>글과 공개 대상 회사.</summary>
/// <param name="Post">글.</param>
/// <param name="Targets">공개 대상 회사 코드. 비어 있으면 게시판을 볼 수 있는 모든 회사.</param>
public sealed record PostDetail(PostDto Post, IReadOnlyList<string> Targets);

/// <summary>공개 대상 한 행 (<c>FW.USP_POST_GET</c> 결과셋 2).</summary>
public sealed class PostTargetRow
{
    /// <summary>회사 코드.</summary>
    public string CompanyCd { get; set; } = "";
}

/// <summary>게시글 등록·수정 요청. <see cref="PostId"/>가 없으면 등록(<see cref="ParentPostId"/>가 있으면 답글).</summary>
public sealed class PostSaveRequest
{
    /// <summary>글 ID. 수정할 때만.</summary>
    public long? PostId { get; set; }

    /// <summary>수정할 때 조회에서 받은 값.</summary>
    [Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[]? RowVer { get; set; }

    /// <summary>답글을 달 글.</summary>
    public long? ParentPostId { get; set; }

    /// <summary>제목.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(200, ErrorMessage = "MSG.INVALID_INPUT")]
    public string Title { get; set; } = "";

    /// <summary>내용 (HTML, 서버가 거른다).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(200_000, ErrorMessage = "MSG.INVALID_INPUT")]
    public string Content { get; set; } = "";

    /// <summary>비밀글 (Y/N).</summary>
    [RegularExpression("^[YN]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string SecretYn { get; set; } = "N";

    /// <summary>상단 고정 (Y/N, 관리자만 반영).</summary>
    [RegularExpression("^[YN]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string PinYn { get; set; } = "N";

    /// <summary>게시 시작일.</summary>
    public DateOnly? StartDt { get; set; }

    /// <summary>게시 종료일.</summary>
    public DateOnly? EndDt { get; set; }

    /// <summary>첨부 묶음 ID.</summary>
    public long? FileGroupId { get; set; }

    /// <summary>공개 대상 회사 코드 (원글, 관리자만 반영). 비우면 모든 회사.</summary>
    public List<string>? Targets { get; set; }
}

/// <summary>게시글 삭제 요청.</summary>
public sealed class PostDeleteRequest
{
    /// <summary>글 ID.</summary>
    [Range(1, long.MaxValue, ErrorMessage = "MSG.INVALID_INPUT")]
    public long PostId { get; set; }

    /// <summary>조회에서 받은 값.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[] RowVer { get; set; } = [];
}

/// <summary>조회자 한 줄 (<c>FW.USP_POST_READER_GET_LIST</c>).</summary>
public sealed class PostReaderDto
{
    /// <summary>사용자 ID.</summary>
    public string UserId { get; set; } = "";

    /// <summary>이름.</summary>
    public string UserNm { get; set; } = "";

    /// <summary>회사.</summary>
    public string CompanyCd { get; set; } = "";

    /// <summary>처음 본 시각 (UTC).</summary>
    public DateTime FirstDt { get; set; }

    /// <summary>마지막으로 본 시각 (UTC).</summary>
    public DateTime LastDt { get; set; }

    /// <summary>본 횟수.</summary>
    public int ReadCnt { get; set; }
}

/// <summary>댓글 (<c>FW.USP_COMMENT_GET_LIST</c>).</summary>
public sealed class CommentDto
{
    /// <summary>댓글 ID.</summary>
    public long CommentId { get; set; }

    /// <summary>내용 (글자만).</summary>
    public string Content { get; set; } = "";

    /// <summary>작성자 회사.</summary>
    public string CompanyCd { get; set; } = "";

    /// <summary>작성자 이름. 익명 게시판이면 null.</summary>
    public string? WriterNm { get; set; }

    /// <summary>내가 쓴 댓글이면 Y.</summary>
    public string MineYn { get; set; } = "N";

    /// <summary>등록 시각 (UTC).</summary>
    public DateTime RegDt { get; set; }

    /// <summary>수정 시각 (UTC).</summary>
    public DateTime UpdDt { get; set; }

    /// <summary>동시 수정 확인 값.</summary>
    public byte[] RowVer { get; set; } = [];
}

/// <summary>댓글 등록·수정 요청.</summary>
public sealed class CommentSaveRequest
{
    /// <summary>댓글 ID. 수정할 때만.</summary>
    public long? CommentId { get; set; }

    /// <summary>수정할 때 조회에서 받은 값.</summary>
    [Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[]? RowVer { get; set; }

    /// <summary>글 ID.</summary>
    [Range(1, long.MaxValue, ErrorMessage = "MSG.INVALID_INPUT")]
    public long PostId { get; set; }

    /// <summary>내용.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(1000, ErrorMessage = "MSG.INVALID_INPUT")]
    public string Content { get; set; } = "";
}

/// <summary>댓글 삭제 요청.</summary>
public sealed class CommentDeleteRequest
{
    /// <summary>댓글 ID.</summary>
    [Range(1, long.MaxValue, ErrorMessage = "MSG.INVALID_INPUT")]
    public long CommentId { get; set; }

    /// <summary>글 ID.</summary>
    [Range(1, long.MaxValue, ErrorMessage = "MSG.INVALID_INPUT")]
    public long PostId { get; set; }

    /// <summary>조회에서 받은 값.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[] RowVer { get; set; } = [];
}
