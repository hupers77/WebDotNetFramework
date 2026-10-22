using System.ComponentModel.DataAnnotations;
using Portal.Framework.Web.Mvc;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>설정 형식 (<c>VAL_TYPE</c> 공통코드).</summary>
public static class ConfigValueTypes
{
    /// <summary>문자열.</summary>
    public const string Text = "STRING";

    /// <summary>0 이상의 정수.</summary>
    public const string Number = "INT";

    /// <summary>Y/N.</summary>
    public const string Bool = "BOOL";

    /// <summary>JSON 문서.</summary>
    public const string Json = "JSON";

    /// <summary>비밀값 (암호화 저장, 화면에 다시 보이지 않음).</summary>
    public const string Secret = "SECRET";
}

/// <summary>설정 목록 검색 조건.</summary>
public sealed class ConfigSearch
{
    /// <summary>설정 분류 (<c>CONFIG_CATEGORY</c>). 비우면 전체.</summary>
    [StringLength(20, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? CategoryCd { get; set; }

    /// <summary>설정 키 또는 설명의 일부.</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Keyword { get; set; }
}

/// <summary>설정 한 건 (<c>FW.USP_CONFIG_ADMIN_GET_LIST</c>).</summary>
public sealed class ConfigDto
{
    /// <summary>설정 키 (예: <c>SESSION.IDLE_MINUTES</c>).</summary>
    public string ConfigKey { get; set; } = "";

    /// <summary>값. SECRET은 항상 null(화면에 내보내지 않는다).</summary>
    public string? ConfigVal { get; set; }

    /// <summary>SECRET 값이 입력되어 있는지 (Y/N).</summary>
    public string SecretSetYn { get; set; } = "N";

    /// <summary>값 형식 (<see cref="ConfigValueTypes"/>).</summary>
    public string ValTypeCd { get; set; } = "";

    /// <summary>설정 분류.</summary>
    public string CategoryCd { get; set; } = "";

    /// <summary>설명 (관리용 한국어).</summary>
    public string? Description { get; set; }

    /// <summary>Y면 앱 풀을 재시작해야 반영된다.</summary>
    public string RestartYn { get; set; } = "N";

    /// <summary>마지막 수정자.</summary>
    public string UpdId { get; set; } = "";

    /// <summary>마지막 수정 일시 (UTC, 화면이 사용자 시간대로 바꿔 보여 준다).</summary>
    public DateTime UpdDt
    {
        get;
        set => field = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    /// <summary>동시성 확인용. 저장할 때 그대로 돌려보낸다.</summary>
    public byte[] RowVer { get; set; } = [];
}

/// <summary>설정 변경 이력 한 건 (<c>FW.USP_CONFIG_HIST_GET_LIST</c>). SECRET은 값 없이 변경 사실만 있다.</summary>
public sealed class ConfigHistDto
{
    /// <summary>이력 ID.</summary>
    public long HistId { get; set; }

    /// <summary>이전 값.</summary>
    public string? OldVal { get; set; }

    /// <summary>새 값.</summary>
    public string? NewVal { get; set; }

    /// <summary>변경자.</summary>
    public string ChgId { get; set; } = "";

    /// <summary>변경 일시 (UTC).</summary>
    public DateTime ChgDt
    {
        get;
        set => field = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    /// <summary>변경한 곳의 IP.</summary>
    public string? ClientIp { get; set; }
}

/// <summary>설정 일괄 저장 요청. 키 추가·삭제는 없고 수정 행(U)만 받는다.</summary>
public sealed class ConfigSaveRequest
{
    /// <summary>바뀐 설정.</summary>
    [MaxLength(Services.ConfigAdminService.MaxRows, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<ConfigSaveRow> Rows { get; set; } = [];
}

/// <summary>바뀐 설정 한 건.</summary>
public sealed class ConfigSaveRow : IGridRow
{
    /// <summary>행 상태 (U만 허용).</summary>
    public string RowStat { get; set; } = "";

    /// <summary>설정 키.</summary>
    public string ConfigKey { get; set; } = "";

    /// <summary>새 값. SECRET은 평문으로 받아 서버가 암호화한다.</summary>
    public string? ConfigVal { get; set; }

    /// <summary>조회 때 받은 값.</summary>
    public byte[]? RowVer { get; set; }
}

/// <summary>저장 결과.</summary>
/// <param name="AffectedCnt">실제로 바뀐 설정 수.</param>
/// <param name="RestartRequired">재시작해야 반영되는 설정이 포함되었는지.</param>
public sealed record ConfigSaveResult(int AffectedCnt, bool RestartRequired);

/// <summary>설정 관리 화면 모델.</summary>
/// <param name="Categories">설정 분류 (코드, 사용자 언어 명칭).</param>
public sealed record ConfigIndexModel(IReadOnlyList<(string Code, string Name)> Categories);
