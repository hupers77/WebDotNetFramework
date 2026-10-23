namespace Portal.Web.Areas.Dash.Models;

/// <summary>위젯 표시 유형 (<c>TB_WIDGET.TYPE_CD</c>).</summary>
public static class WidgetTypes
{
    /// <summary>숫자 카드.</summary>
    public const string Card = "CARD";

    /// <summary>목록.</summary>
    public const string List = "LIST";
}

/// <summary>위젯 정의 (<c>FW.USP_WIDGET_GET_LIST</c>).</summary>
public sealed class WidgetDto
{
    /// <summary>위젯 코드.</summary>
    public string WidgetCd { get; set; } = "";

    /// <summary>제목 (화면 언어).</summary>
    public string WidgetNm { get; set; } = "";

    /// <summary>표시 유형 (<see cref="WidgetTypes"/>).</summary>
    public string TypeCd { get; set; } = "";

    /// <summary>데이터 SP. 화면에는 내보내지 않는다.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DataSp { get; set; } = "";

    /// <summary>읽기 권한을 확인할 프로그램. 없으면 로그인한 모두.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ProgramId { get; set; }

    /// <summary>제목을 누르면 가는 화면.</summary>
    public string? LinkUrl { get; set; }

    /// <summary>12칸 그리드에서 차지하는 칸 수.</summary>
    public int SpanCols { get; set; } = 6;

    /// <summary>사용자별 데이터 캐시 시간(초).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int CacheSec { get; set; }
}

/// <summary>위젯 데이터 한 행. 위젯 SP는 모두 이 열로 돌려준다 (숫자 카드는 VALUE, 목록은 TEXT·LINK·DT).</summary>
public sealed class WidgetRowDto
{
    /// <summary>이름표.</summary>
    public string? Label { get; set; }

    /// <summary>숫자 값.</summary>
    public decimal? Value { get; set; }

    /// <summary>문구.</summary>
    public string? Text { get; set; }

    /// <summary>바로가기. 사이트 안 경로만 내보낸다.</summary>
    public string? Link { get; set; }

    /// <summary>일시 (UTC).</summary>
    public DateTime? Dt
    {
        get;
        set => field = value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
    }
}
