using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Html;
using Portal.Core.Keys;
using Portal.Data;

namespace Portal.Web.Areas.Rpt.Models;

/// <summary>레포트 메시지 키.</summary>
public static class ReportMessageKeys
{
    /// <summary>결과가 최대 행 수를 넘음. 인자: 최대 행 수.</summary>
    public const string TooMany = "RPT.TOO_MANY";

    /// <summary>조건 값 형식이 틀림. 인자: 조건 이름.</summary>
    public const string ParamInvalid = "RPT.PARAM_INVALID";

    /// <summary>필수 조건이 비었음. 인자: 조건 이름.</summary>
    public const string ParamRequired = "VAL.REQUIRED";
}

/// <summary>레포트 정의 (<c>FW.USP_REPORT_GET</c>, 표 <c>FW.TB_REPORT</c>).</summary>
/// <param name="ReportId">레포트 ID (= 프로그램 ID, 권한·메뉴·접근 기록의 기준).</param>
/// <param name="DataSp">데이터 SP (<c>스키마.USP_RPT_대상_GET_LIST</c>).</param>
/// <param name="TemplateView">템플릿 뷰 이름 (<c>Areas/Rpt/Views/Templates</c>). 없으면 기본 표.</param>
/// <param name="ParamsJson">조건 정의 JSON.</param>
/// <param name="OrientCd">용지 방향 P 세로 / L 가로.</param>
/// <param name="ApprovalYn">결재란 표시.</param>
/// <param name="TotalYn">숫자 컬럼 합계 행 표시.</param>
/// <param name="MaxRows">최대 행 수.</param>
/// <param name="PiiYn">개인정보 포함 (프로그램의 <c>PII_YN</c>).</param>
public sealed record ReportDefinition(
    string ReportId, string DataSp, string? TemplateView, string? ParamsJson, string OrientCd, string ApprovalYn, string TotalYn, int MaxRows, string PiiYn)
{
    /// <summary>가로 방향.</summary>
    public bool Landscape => OrientCd == "L";

    /// <summary>개인정보 포함.</summary>
    public bool HasPii => PiiYn == "Y";
}

/// <summary>조건 하나 (<c>PARAMS_JSON</c>의 항목).</summary>
/// <param name="Name">데이터 SP 파라미터 이름 (<c>P_MONTH</c>, @ 제외).</param>
/// <param name="Type">DATE / MONTH / TEXT.</param>
/// <param name="Required">필수.</param>
/// <param name="LabelKey">화면 문구 키.</param>
/// <param name="Default">기본값 TODAY / THIS_MONTH / PREV_MONTH, 없으면 빈 값.</param>
/// <param name="MaxLength">TEXT 최대 길이.</param>
public sealed record ReportParam(string Name, string Type, bool Required, string LabelKey, string? Default, int MaxLength);

/// <summary>PDF·엑셀 요청. 조건 값은 조건 이름(<c>P_MONTH</c>)별 문자열이다.</summary>
public sealed class ReportRunRequest
{
    /// <summary>조건 값.</summary>
    public Dictionary<string, string?> Params { get; set; } = [];

    /// <summary>다운로드 사유 (개인정보가 든 레포트만).</summary>
    [StringLength(200, ErrorMessage = MessageKeys.PiiReasonRequired)]
    public string? Reason { get; set; }
}

/// <summary>미리보기 응답: 레포트 HTML 문서 전체 (화면이 스크립트를 막은 iframe에 넣는다).</summary>
/// <param name="Html">HTML 문서.</param>
/// <param name="RowCount">행 수.</param>
public sealed record ReportPreview(string Html, int RowCount);

/// <summary>표의 컬럼.</summary>
/// <param name="Name">SP 결과 컬럼 이름.</param>
/// <param name="Label">머리글 (문구 <c>RPT.{레포트 ID}.{컬럼}</c>, 없으면 컬럼 이름).</param>
/// <param name="Numeric">숫자 컬럼 (오른쪽 정렬, 합계 대상).</param>
public sealed record ReportColumn(string Name, string Label, bool Numeric);

/// <summary>레포트 실행 결과 (템플릿 모델, 엑셀 원본).</summary>
public sealed class ReportResult
{
    /// <summary>정의.</summary>
    public required ReportDefinition Definition { get; init; }

    /// <summary>제목 (메뉴명).</summary>
    public required string Title { get; init; }

    /// <summary>조건 (이름, 값) — 머리글에 찍는다.</summary>
    public required IReadOnlyList<(string Label, string Value)> Conditions { get; init; }

    /// <summary>컬럼 (SP 결과 순서).</summary>
    public required IReadOnlyList<ReportColumn> Columns { get; init; }

    /// <summary>행.</summary>
    public required IReadOnlyList<SpRow> Rows { get; init; }

    /// <summary>합계 행 (<c>TOTAL_YN</c>이 아니거나 행이 없으면 null). 첫 컬럼은 "합계", 숫자가 아닌 컬럼은 null.</summary>
    public SpRow? Totals { get; init; }

    /// <summary>출력자 ID.</summary>
    public required string PrintedBy { get; init; }

    /// <summary>출력 일시 (서버 현지 시간).</summary>
    public required DateTime PrintedAt { get; init; }
}

/// <summary>템플릿에 넘기는 모델.</summary>
/// <param name="Report">실행 결과.</param>
/// <param name="InlineStyle">PDF용: 문서에 넣을 <c>&lt;style&gt;</c> (wwwroot의 <c>css/report.css</c> 내용, 사용자 입력 아님).
/// null이면 <c>/css/report.css</c>를 연결한다(미리보기).</param>
public sealed record ReportDocumentModel(ReportResult Report, IHtmlContent? InlineStyle);

/// <summary>레포트 화면 모델.</summary>
/// <param name="Definition">정의.</param>
/// <param name="Title">제목.</param>
/// <param name="Params">조건.</param>
/// <param name="Defaults">조건별 기본값.</param>
public sealed record ReportViewerModel(ReportDefinition Definition, string Title, IReadOnlyList<ReportParam> Params, IReadOnlyDictionary<string, string?> Defaults);
