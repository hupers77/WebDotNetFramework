using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Context;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Localization;
using Portal.Data;
using Portal.Framework.Web.Logging;
using Portal.Framework.Web.Navigation;
using Portal.Infrastructure.Pdf;
using Portal.Web.Areas.Rpt.Models;

namespace Portal.Web.Areas.Rpt.Services;

/// <summary>
/// 화면 출력 (현재 화면 PDF). 화면이 보이는 검색 조건과 표를 글자로 보내면 레포트와 같은 머리글(제목·조건·출력자·일시)과
/// 바닥글·쪽 번호를 붙여 PDF로 만든다. HTML을 받지 않고 서버 템플릿이 그리므로 글자는 모두 인코딩된다.
/// </summary>
/// <remarks>권한(PRINT)은 화면(Controller)이 요청의 프로그램으로 확인한다. 개인정보 화면(<c>PII_YN = 'Y'</c>)은 사유를 받아 다운로드로 기록한다.</remarks>
public interface IScreenPrintService
{
    /// <summary>사용 중인 프로그램. 없거나 사용 안 함이면 null.</summary>
    Task<ProgramInfo?> GetProgramAsync(string programId, CancellationToken cancellationToken = default);

    /// <summary>PDF를 만든다.</summary>
    Task<byte[]> PdfAsync(ActionContext context, ProgramInfo program, ScreenPrintRequest request, CancellationToken cancellationToken = default);

    /// <summary>내려받을 파일 이름 (<c>프로그램ID-yyyyMMddHHmm.pdf</c>).</summary>
    string FileName(ProgramInfo program);
}

/// <inheritdoc />
public sealed class ScreenPrintService(
    ISpExecutor sp,
    ICurrentUser currentUser,
    IPortalLocalizer localizer,
    MenuCatalog menus,
    IPiiAccessLog pii,
    IPdfConverter pdf,
    ReportRenderer renderer,
    IWebHostEnvironment environment,
    TimeProvider time) : IScreenPrintService
{
    internal const string ProgramSp = "FW.USP_PROGRAM_GET";

    /// <summary>표 수 상한.</summary>
    public const int MaxTables = 5;

    /// <summary>표 하나의 열 수 상한.</summary>
    public const int MaxColumns = 60;

    /// <summary>모든 표의 행 수 합 상한. 넘으면 엑셀이나 레포트를 쓰게 한다.</summary>
    public const int MaxRows = 5000;

    /// <summary>조건 수 상한.</summary>
    public const int MaxConditions = 30;

    /// <summary>글자(칸·머리글·조건) 길이 상한.</summary>
    public const int MaxText = 2000;

    /// <summary>열이 이보다 많으면 가로로 출력한다.</summary>
    public const int LandscapeColumns = 7;

    private const string TemplatePath = "~/Areas/Rpt/Views/Templates/Screen.cshtml";

    /// <inheritdoc />
    public Task<ProgramInfo?> GetProgramAsync(string programId, CancellationToken cancellationToken = default) =>
        sp.QuerySingleAsync<ProgramInfo>(ProgramSp, new { P_PROGRAM_ID = programId }, cancellationToken);

    /// <inheritdoc />
    public async Task<byte[]> PdfAsync(ActionContext context, ProgramInfo program, ScreenPrintRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        // 개인정보 화면은 사유가 필수이고, 화면이 사유를 물어 보냈으면(버튼의 data-pii) 그 화면도 다운로드로 기록한다
        var reason = request.Reason?.Trim() is { Length: > 0 } r ? r : null;
        if (program.PiiYn == "Y" && reason is not { Length: >= 2 })
        {
            throw new BizException(MessageKeys.PiiReasonRequired);
        }

        var landscape = request.Tables.Any(t => t.Columns.Count > LandscapeColumns);
        var now = time.GetLocalNow().DateTime;
        var result = new ReportResult
        {
            Definition = new ReportDefinition(program.ProgramId, "", null, null, landscape ? "L" : "P", "N", "N", MaxRows, program.PiiYn),
            Title = menus.ProgramTitle(program.ProgramId, currentUser.LangCd),
            Conditions = [.. request.Conditions.Where(c => !string.IsNullOrWhiteSpace(c.Value)).Select(c => (c.Label.Trim(), c.Value!.Trim()))],
            Columns = [],
            Rows = [],
            PrintedBy = currentUser.UserId ?? "",
            PrintedAt = now,
        };

        var html = await renderer.RenderAsync(context, TemplatePath, new ReportDocumentModel(result, await ReportStyle.InlineAsync(environment, cancellationToken), request.Tables));
        var footer = localizer["RPT.PRINTED", result.PrintedBy, now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)];
        var bytes = await pdf.ConvertAsync(html, new PdfPageOptions(footer, landscape), cancellationToken);
        if (reason is not null)
        {
            var target = JsonSerializer.Serialize(new { cnt = request.Tables.Sum(t => t.Rows.Count), format = "SCREEN_PDF" });
            await pii.WriteAsync(program.ProgramId, PiiActions.Download, target, reason, cancellationToken);
        }

        return bytes;
    }

    /// <inheritdoc />
    public string FileName(ProgramInfo program) =>
        string.Create(CultureInfo.InvariantCulture, $"{program.ProgramId}-{time.GetLocalNow():yyyyMMddHHmm}.pdf");

    /// <summary>
    /// 요청 모양 검사: 표 1~5개, 열 1~60개, 행마다 칸 수 = 열 수, 행 합 5000까지(넘으면 <c>RPT.TOO_MANY</c>), 글자 2000자까지.
    /// </summary>
    internal static void Validate(ScreenPrintRequest request)
    {
        if (request.Tables.Count is 0 or > MaxTables || request.Conditions.Count > MaxConditions
            || request.Conditions.Any(c => Long(c.Label) || Long(c.Value)))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        if (request.Tables.Sum(t => t.Rows.Count) > MaxRows)
        {
            throw new BizException(ReportMessageKeys.TooMany, MaxRows);
        }

        foreach (var table in request.Tables)
        {
            if (table.Columns.Count is 0 or > MaxColumns || Long(table.Caption) || table.Columns.Any(Long)
                || table.Rows.Any(row => row.Count != table.Columns.Count || row.Any(Long)))
            {
                throw new BizException(MessageKeys.InvalidInput);
            }
        }
    }

    private static bool Long(string? text) => text is { Length: > MaxText };
}
