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

    /// <summary>화면에서 바로 만들 수 있는 행 수를 넘었음 — 백그라운드 생성을 쓰라는 안내. 인자: 바로 만들 수 있는 최대 행 수.</summary>
    public const string UseBackground = "RPT.USE_BACKGROUND";

    /// <summary>진행 중인 생성 요청이 너무 많음 (<c>USP_REPORT_RUN_SAVE</c>).</summary>
    public const string RunLimit = "RPT.RUN_LIMIT";

    /// <summary>만들던 중 서비스가 멈춰 끝내지 못했음 (<c>USP_REPORT_RUN_CLAIM_EXEC</c>).</summary>
    public const string RunInterrupted = "RPT.RUN_INTERRUPTED";

    /// <summary>만드는 데 너무 오래 걸려 중단했음 (<see cref="Services.ReportRunProcessor"/>).</summary>
    public const string RunTimeout = "RPT.RUN_TIMEOUT";
}

/// <summary>레포트 출력 형식 코드 (<c>TB_REPORT_RUN.FORMAT_CD</c>).</summary>
public static class ReportFormats
{
    /// <summary>PDF (PRINT 권한).</summary>
    public const string Pdf = "PDF";

    /// <summary>엑셀 (EXPORT 권한).</summary>
    public const string Excel = "EXCEL";

    /// <summary>형식에 필요한 기능 코드.</summary>
    public static string? FuncOf(string? formatCd) => formatCd switch
    {
        Pdf => FuncCodes.Print,
        Excel => FuncCodes.Export,
        _ => null,
    };
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
public class ReportRunRequest
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
/// <param name="Tables">화면 출력(<c>Screen</c> 템플릿)의 표. 레포트는 null.</param>
public sealed record ReportDocumentModel(ReportResult Report, IHtmlContent? InlineStyle, IReadOnlyList<ScreenTable>? Tables = null);

/// <summary>화면 출력 요청 (<c>/Rpt/Print/Pdf</c>): 화면에 보이는 검색 조건과 표를 글자 그대로 받는다(HTML은 받지 않는다).</summary>
public sealed class ScreenPrintRequest
{
    /// <summary>출력하는 화면의 프로그램 ID (PRINT 권한을 확인한다).</summary>
    [Required(ErrorMessage = MessageKeys.InvalidInput), StringLength(30, ErrorMessage = MessageKeys.InvalidInput)]
    public string ProgramId { get; set; } = "";

    /// <summary>검색 조건 (이름, 값).</summary>
    public List<ScreenCondition> Conditions { get; set; } = [];

    /// <summary>표 (화면에 보이는 것만).</summary>
    public List<ScreenTable> Tables { get; set; } = [];

    /// <summary>다운로드 사유 (개인정보 화면만).</summary>
    [StringLength(200, ErrorMessage = MessageKeys.PiiReasonRequired)]
    public string? Reason { get; set; }
}

/// <summary>화면의 검색 조건 하나.</summary>
public sealed class ScreenCondition
{
    /// <summary>이름 (입력란 라벨).</summary>
    public string Label { get; set; } = "";

    /// <summary>값 (선택 목록은 보이는 글자).</summary>
    public string? Value { get; set; }
}

/// <summary>화면의 표 하나: 머리글과 행(칸 글자).</summary>
public sealed class ScreenTable
{
    /// <summary>표 제목 (없으면 생략).</summary>
    public string? Caption { get; set; }

    /// <summary>머리글.</summary>
    public List<string> Columns { get; set; } = [];

    /// <summary>행. 칸 수는 머리글 수와 같다.</summary>
    public List<List<string?>> Rows { get; set; } = [];
}

/// <summary>프로그램 (<c>FW.USP_PROGRAM_GET</c>).</summary>
/// <param name="ProgramId">프로그램 ID.</param>
/// <param name="ModuleCd">모듈.</param>
/// <param name="PiiYn">개인정보 처리 화면.</param>
public sealed record ProgramInfo(string ProgramId, string ModuleCd, string PiiYn);

/// <summary>레포트 화면 모델.</summary>
/// <param name="Definition">정의.</param>
/// <param name="Title">제목.</param>
/// <param name="Params">조건.</param>
/// <param name="Defaults">조건별 기본값.</param>
public sealed record ReportViewerModel(ReportDefinition Definition, string Title, IReadOnlyList<ReportParam> Params, IReadOnlyDictionary<string, string?> Defaults);

/// <summary>백그라운드 생성 요청: 형식(<c>PDF</c>/<c>EXCEL</c>)과 지금 입력한 조건.</summary>
public sealed class ReportQueueRequest : ReportRunRequest
{
    /// <summary>PDF 또는 EXCEL.</summary>
    [Required(ErrorMessage = MessageKeys.InvalidInput), StringLength(5, ErrorMessage = MessageKeys.InvalidInput)]
    public string Format { get; set; } = "";
}

/// <summary>내 생성 요청 한 건 (<c>FW.USP_REPORT_RUN_GET_LIST</c>).</summary>
public sealed class ReportRunItem
{
    /// <summary>요청 번호.</summary>
    public long RunId { get; init; }

    /// <summary>레포트 ID.</summary>
    public string ReportId { get; init; } = "";

    /// <summary>레포트 이름 (메뉴명, 서비스가 채운다).</summary>
    public string ReportNm { get; set; } = "";

    /// <summary>PDF / EXCEL.</summary>
    public string FormatCd { get; init; } = "";

    /// <summary>QUEUED / RUNNING / DONE / FAILED.</summary>
    public string StatusCd { get; init; } = "";

    /// <summary>행 수 (끝난 뒤).</summary>
    public int? RowCnt { get; init; }

    /// <summary>파일 이름 (끝난 뒤).</summary>
    public string? FileNm { get; init; }

    /// <summary>파일 크기 (바이트).</summary>
    public long? FileSize { get; init; }

    /// <summary>실패 사유 문구 키 (서비스가 <see cref="ErrMsg"/>로 바꾼다).</summary>
    public string? ErrKey { get; init; }

    /// <summary>실패 사유 문구의 {0}.</summary>
    public string? ErrArg { get; init; }

    /// <summary>실패 사유 (사용자 언어).</summary>
    public string? ErrMsg { get; set; }

    /// <summary>요청 시각 (UTC).</summary>
    public DateTime RegDt { get; init; }

    /// <summary>끝난 시각 (UTC).</summary>
    public DateTime? EndDt { get; init; }

    /// <summary>지워지는 시각 (UTC).</summary>
    public DateTime? ExpireDt { get; init; }
}

/// <summary>내려받을 결과 파일 (<c>FW.USP_REPORT_RUN_GET</c>).</summary>
public sealed class ReportRunFile
{
    /// <summary>요청 번호.</summary>
    public long RunId { get; init; }

    /// <summary>레포트 ID (내려받기 권한 확인용).</summary>
    public string ReportId { get; init; } = "";

    /// <summary>PDF / EXCEL.</summary>
    public string FormatCd { get; init; } = "";

    /// <summary>QUEUED / RUNNING / DONE / FAILED.</summary>
    public string StatusCd { get; init; } = "";

    /// <summary>파일 이름.</summary>
    public string? FileNm { get; init; }

    /// <summary>파일 크기.</summary>
    public long? FileSize { get; init; }

    /// <summary>저장소 안 경로.</summary>
    public string? StoredPath { get; init; }
}

/// <summary>백그라운드 서비스가 꺼낸 요청 (<c>FW.USP_REPORT_RUN_CLAIM_EXEC</c>).</summary>
public sealed class ClaimedReportRun
{
    /// <summary>요청 번호.</summary>
    public long RunId { get; init; }

    /// <summary>레포트 ID.</summary>
    public string ReportId { get; init; } = "";

    /// <summary>PDF / EXCEL.</summary>
    public string FormatCd { get; init; } = "";

    /// <summary>조건 (JSON, 이름 → 값).</summary>
    public string ParamsJson { get; init; } = "{}";

    /// <summary>다운로드 사유 (개인정보 레포트).</summary>
    public string? Reason { get; init; }

    /// <summary>요청자 ID.</summary>
    public string ReqUserId { get; init; } = "";

    /// <summary>요청자 회사.</summary>
    public string ReqCompanyCd { get; init; } = "";

    /// <summary>요청자 유형.</summary>
    public string ReqUserTypeCd { get; init; } = "";

    /// <summary>요청자 언어.</summary>
    public string ReqLangCd { get; init; } = "ko";
}

/// <summary>만든 결과: 내용, 행 수, 파일 이름.</summary>
/// <param name="Content">PDF·엑셀 내용.</param>
/// <param name="RowCount">행 수.</param>
/// <param name="FileName">내려받을 파일 이름.</param>
public sealed record ReportOutput(byte[] Content, int RowCount, string FileName);

/// <summary>백그라운드 생성 요청 응답.</summary>
/// <param name="RunId">요청 번호.</param>
public sealed record ReportQueued(long RunId);

/// <summary>내 레포트 화면 모델.</summary>
/// <param name="KeepDays">완성된 파일 보관 일수.</param>
public sealed record ReportRunsViewModel(int KeepDays);

/// <summary>내 레포트 화면의 요청 번호 본문 (내려받기·삭제).</summary>
public sealed class ReportRunIdRequest
{
    /// <summary>요청 번호.</summary>
    [System.ComponentModel.DataAnnotations.Range(1, long.MaxValue, ErrorMessage = MessageKeys.InvalidInput)]
    public long RunId { get; set; }
}
