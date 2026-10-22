using System.ComponentModel.DataAnnotations;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>프로그램 관리 SP가 던지는 메시지 키.</summary>
public static class ProgramMessageKeys
{
    /// <summary>조회(READ)는 꼭 있어야 한다.</summary>
    public const string ReadRequired = "MSG.PROGRAM_READ_REQUIRED";

    /// <summary>서버 경로가 요구하는 기능은 뺄 수 없다.</summary>
    public const string FuncInUse = "MSG.PROGRAM_FUNC_IN_USE";

    /// <summary>홈과 프로그램 관리 화면은 사용 안 함으로 바꿀 수 없다.</summary>
    public const string KeepUse = "MSG.PROGRAM_KEEP_USE";

    /// <summary>서버 경로·메뉴·첨부 파일·메시지 템플릿이 연결된 프로그램은 지울 수 없다.</summary>
    public const string InUse = "MSG.PROGRAM_IN_USE";
}

/// <summary>프로그램 (<c>FW.USP_PROGRAM_GET_LIST</c>).</summary>
public sealed class ProgramDto
{
    /// <summary>프로그램 ID.</summary>
    public string ProgramId { get; set; } = "";

    /// <summary>모듈.</summary>
    public string ModuleCd { get; set; } = "";

    /// <summary>관리용 이름 (화면 제목은 메뉴명).</summary>
    public string ProgramNm { get; set; } = "";

    /// <summary>화면 주소.</summary>
    public string Url { get; set; } = "";

    /// <summary>개인정보 처리 화면.</summary>
    public string PiiYn { get; set; } = "N";

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>사용 기능 (쉼표 구분, 공통코드 순서).</summary>
    public string Funcs { get; set; } = "";

    /// <summary>연결 메뉴 수.</summary>
    public int MenuCnt { get; set; }

    /// <summary>등록된 서버 경로 수.</summary>
    public int ApiCnt { get; set; }

    /// <summary>권한을 받은 역할 수.</summary>
    public int RoleCnt { get; set; }

    /// <summary>마지막 수정자.</summary>
    public string UpdId { get; set; } = "";

    /// <summary>마지막 수정 (UTC).</summary>
    public DateTime UpdDt { get; set; }

    /// <summary>동시성 확인용.</summary>
    public byte[] RowVer { get; set; } = [];
}

/// <summary>프로그램 한 개의 기능 (<c>FW.USP_PROGRAM_FUNC_GET_LIST</c>).</summary>
public sealed class ProgramFuncDto
{
    /// <summary>기능 코드.</summary>
    public string FuncCd { get; set; } = "";

    /// <summary>이 프로그램이 쓰는 기능.</summary>
    public string UsedYn { get; set; } = "N";

    /// <summary>이 기능을 요구하는 서버 경로 수 (있으면 뺄 수 없다).</summary>
    public int ApiCnt { get; set; }

    /// <summary>이 기능을 받은 역할 수.</summary>
    public int RoleCnt { get; set; }
}

/// <summary>프로그램 등록·수정 요청. <see cref="RowVer"/>가 없으면 등록.</summary>
public sealed class ProgramSaveRequest
{
    /// <summary>프로그램 ID (<c>모듈_대상_유형</c>, 영문 대문자로 시작하는 대문자·숫자·밑줄 2~30자). 등록한 뒤에는 바꿀 수 없다.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^[A-Z][A-Z0-9_]{1,29}$", ErrorMessage = "VAL.PROGRAM_ID_FORMAT")]
    public string ProgramId { get; set; } = "";

    /// <summary>수정할 때 조회에서 받은 값. 등록이면 비운다.</summary>
    [Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[]? RowVer { get; set; }

    /// <summary>모듈 (등록할 때만, 영문 대문자로 시작하는 대문자·숫자 2~20자).</summary>
    [RegularExpression("^[A-Z][A-Z0-9]{1,19}$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ModuleCd { get; set; }

    /// <summary>관리용 이름.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string ProgramNm { get; set; } = "";

    /// <summary>화면 주소 (등록할 때만, <c>/</c>로 시작하는 사이트 안 경로).</summary>
    [StringLength(300, ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression(@"^/(?![/\\])[A-Za-z0-9/_\-.?=&]*$", ErrorMessage = "VAL.PROGRAM_URL_FORMAT")]
    public string? Url { get; set; }

    /// <summary>개인정보 처리 화면 (Y/N).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^[YN]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string PiiYn { get; set; } = "N";

    /// <summary>사용 여부 (Y/N).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^[YN]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string UseYn { get; set; } = "Y";

    /// <summary>이 프로그램이 쓰는 기능 전부 (공통코드 <c>FUNC_CD</c>, 조회 필수).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), MaxLength(50, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<string> Funcs { get; set; } = [];
}

/// <summary>프로그램 삭제 요청.</summary>
public sealed class ProgramDeleteRequest
{
    /// <summary>프로그램 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string ProgramId { get; set; } = "";

    /// <summary>조회 때 받은 값.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[] RowVer { get; set; } = [];
}

/// <summary>화면 모델: 기능 이름 (공통코드 FUNC_CD).</summary>
/// <param name="Funcs">(코드, 이름).</param>
public sealed record ProgramIndexModel(IReadOnlyList<(string Code, string Name)> Funcs);
