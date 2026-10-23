using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Context;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Localization;
using Portal.Data;
using Portal.Framework.Web.Logging;
using Portal.Framework.Web.Navigation;
using Portal.Infrastructure.Excel;
using Portal.Infrastructure.Pdf;
using Portal.Web.Areas.Rpt.Models;

namespace Portal.Web.Areas.Rpt.Services;

/// <summary>
/// 레포트 모듈 (설계서 11장 '레포트'). <c>TB_REPORT</c>에 등록한 레포트를 조건대로 실행해 미리보기 HTML·PDF·엑셀로 만든다.
/// 데이터는 데이터 SP의 결과셋 하나를 컬럼 그대로 쓰고, 표는 템플릿(Razor 뷰)이 머리글·조건·결재란과 함께 그린다.
/// </summary>
/// <remarks>
/// 권한은 화면(Controller)이 레포트 ID(= 프로그램 ID)로 확인한다. 개인정보가 든 레포트(프로그램 <c>PII_YN = 'Y'</c>)는
/// 미리보기를 조회로, PDF·엑셀을 사유와 함께 다운로드로 개인정보 접속기록에 남긴다.
/// </remarks>
public interface IReportService
{
    /// <summary>사용 중인 레포트 정의. 없거나 사용 안 함이면 null.</summary>
    Task<ReportDefinition?> GetAsync(string reportId, CancellationToken cancellationToken = default);

    /// <summary>조건 입력 화면 모델 (기본값은 오늘 날짜 기준).</summary>
    ReportViewerModel Viewer(ReportDefinition definition);

    /// <summary>미리보기 HTML (CSS는 <c>/css/report.css</c> 연결).</summary>
    Task<ReportPreview> PreviewAsync(ActionContext context, ReportDefinition definition, IReadOnlyDictionary<string, string?> input, CancellationToken cancellationToken = default);

    /// <summary>PDF (모든 쪽에 출력자·일시 바닥글과 쪽 번호).</summary>
    Task<byte[]> PdfAsync(ActionContext context, ReportDefinition definition, ReportRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>엑셀 (머리글 = 컬럼 문구, 합계 행 포함).</summary>
    Task<Stream> ExcelAsync(ReportDefinition definition, ReportRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 백그라운드 서비스용: 요청자의 권한으로 PDF 또는 엑셀을 만든다. 화면에서 바로 만드는 행 수 한도(<see cref="ReportService.SyncMaxRows"/>)가 아니라
    /// 레포트의 <c>MAX_ROWS</c>까지 허용한다.
    /// </summary>
    Task<ReportOutput> GenerateAsync(ActionContext context, ReportDefinition definition, ReportRunRequest request, string formatCd, CancellationToken cancellationToken = default);

    /// <summary>조건 값이 정의에 맞는지 확인한다(요청을 받을 때 바로 알려 주려고). 틀리면 <see cref="BizException"/>.</summary>
    void Validate(ReportDefinition definition, IReadOnlyDictionary<string, string?> input);

    /// <summary>개인정보 레포트의 다운로드 사유를 확인해 돌려준다. 개인정보 레포트가 아니면 null, 사유가 없으면 <see cref="BizException"/>.</summary>
    string? RequireReason(ReportDefinition definition, ReportRunRequest request);

    /// <summary>내려받을 파일 이름 (<c>레포트ID-yyyyMMddHHmm.확장자</c>).</summary>
    string FileName(ReportDefinition definition, string extension);
}

/// <inheritdoc />
public sealed partial class ReportService(
    ISpExecutor sp,
    ICurrentUser currentUser,
    IPortalLocalizer localizer,
    MenuCatalog menus,
    IPiiAccessLog pii,
    IPdfConverter pdf,
    ReportRenderer renderer,
    IWebHostEnvironment environment,
    TimeProvider time) : IReportService
{
    internal const string GetSp = "FW.USP_REPORT_GET";

    /// <summary>화면에서 바로(미리보기·PDF·엑셀) 만드는 최대 행 수. 레포트의 <c>MAX_ROWS</c>가 더 커도 이 수를 넘으면 백그라운드 생성을 써야 한다.</summary>
    public const int SyncMaxRows = 5000;

    private const string TemplateFolder = "~/Areas/Rpt/Views/Templates/";
    private const string DefaultTemplate = "Default";

    /// <inheritdoc />
    public async Task<ReportDefinition?> GetAsync(string reportId, CancellationToken cancellationToken = default)
    {
        if (!ReportIdPattern().IsMatch(reportId ?? ""))
        {
            return null;
        }

        var definition = await sp.QuerySingleAsync<ReportDefinition>(GetSp, new { P_REPORT_ID = reportId }, cancellationToken);
        if (definition is not null && !DataSpPattern().IsMatch(definition.DataSp))
        {
            throw new InvalidOperationException($"TB_REPORT.DATA_SP '{definition.DataSp}'는 레포트 데이터 SP 형식(스키마.USP_RPT_대상_GET_LIST)이 아닙니다.");
        }

        return definition;
    }

    /// <inheritdoc />
    public ReportViewerModel Viewer(ReportDefinition definition)
    {
        var parameters = ReportParams.Parse(definition.ParamsJson);
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        return new ReportViewerModel(definition, Title(definition), parameters, ReportParams.Defaults(parameters, today));
    }

    /// <inheritdoc />
    public async Task<ReportPreview> PreviewAsync(ActionContext context, ReportDefinition definition, IReadOnlyDictionary<string, string?> input, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(definition, input, SyncLimit(definition), cancellationToken);
        if (definition.HasPii)
        {
            await pii.WriteAsync(definition.ReportId, PiiActions.View, TargetDesc(result, "PREVIEW"), null, cancellationToken);
        }

        var html = await renderer.RenderAsync(context, TemplatePath(definition), new ReportDocumentModel(result, null));
        return new ReportPreview(html, result.Rows.Count);
    }

    /// <inheritdoc />
    public async Task<byte[]> PdfAsync(ActionContext context, ReportDefinition definition, ReportRunRequest request, CancellationToken cancellationToken = default) =>
        (await BuildAsync(context, definition, request, ReportFormats.Pdf, SyncLimit(definition), cancellationToken)).Content;

    /// <inheritdoc />
    public async Task<Stream> ExcelAsync(ReportDefinition definition, ReportRunRequest request, CancellationToken cancellationToken = default) =>
        new MemoryStream((await BuildAsync(null, definition, request, ReportFormats.Excel, SyncLimit(definition), cancellationToken)).Content, writable: false);

    /// <inheritdoc />
    public Task<ReportOutput> GenerateAsync(ActionContext context, ReportDefinition definition, ReportRunRequest request, string formatCd, CancellationToken cancellationToken = default) =>
        BuildAsync(context, definition, request, formatCd, definition.MaxRows, cancellationToken);

    /// <inheritdoc />
    public void Validate(ReportDefinition definition, IReadOnlyDictionary<string, string?> input) =>
        ReportParams.Bind(ReportParams.Parse(definition.ParamsJson), input, key => localizer[key]);

    /// <inheritdoc />
    public string? RequireReason(ReportDefinition definition, ReportRunRequest request) => RequireReasonCore(definition, request);

    /// <inheritdoc />
    public string FileName(ReportDefinition definition, string extension) =>
        string.Create(CultureInfo.InvariantCulture, $"{definition.ReportId}-{time.GetLocalNow():yyyyMMddHHmm}.{extension}");

    /// <summary>템플릿 뷰 경로. 이름은 영문·숫자만 허용해 Templates 폴더 밖을 가리킬 수 없다.</summary>
    internal static string TemplatePath(ReportDefinition definition)
    {
        var name = string.IsNullOrWhiteSpace(definition.TemplateView) ? DefaultTemplate : definition.TemplateView;
        return TemplateNamePattern().IsMatch(name)
            ? TemplateFolder + name + ".cshtml"
            : throw new InvalidOperationException($"TB_REPORT.TEMPLATE_VIEW '{name}'은 영문·숫자 이름이어야 합니다.");
    }

    /// <summary>개인정보 접속기록의 대상: 건수·형식·조건 이름 (조건 값은 개인정보일 수 있어 넣지 않는다).</summary>
    internal static string TargetDesc(ReportResult result, string format) =>
        JsonSerializer.Serialize(new { cnt = result.Rows.Count, format, conditions = result.Conditions.Select(c => c.Label) });

    private async Task<ReportResult> RunAsync(ReportDefinition definition, IReadOnlyDictionary<string, string?> input, int limit, CancellationToken cancellationToken)
    {
        var (parameters, conditions) = ReportParams.Bind(ReportParams.Parse(definition.ParamsJson), input, key => localizer[key]);
        parameters.Add(new(ReportParams.MaxRowsParam, limit));

        var rows = await sp.QueryAsync<SpRow>(definition.DataSp, parameters, cancellationToken);
        if (rows.Count > limit)
        {
            throw limit < definition.MaxRows
                ? new BizException(ReportMessageKeys.UseBackground, limit)
                : new BizException(ReportMessageKeys.TooMany, limit);
        }

        var names = rows.Count > 0 ? rows[0].Columns : [];
        var columns = ReportFormat.Columns(names, rows, name => ColumnLabel(definition.ReportId, name));
        return new ReportResult
        {
            Definition = definition,
            Title = Title(definition),
            Conditions = conditions,
            Columns = columns,
            Rows = rows,
            Totals = definition.TotalYn == "Y" && rows.Count > 0 ? ReportFormat.Totals(columns, rows, localizer["RPT.TOTAL"]) : null,
            PrintedBy = currentUser.UserId ?? "",
            PrintedAt = time.GetLocalNow().DateTime,
        };
    }

    private string Title(ReportDefinition definition) => menus.ProgramTitle(definition.ReportId, currentUser.LangCd);

    /// <summary>컬럼 머리글: 문구 <c>RPT.{레포트 ID}.{컬럼}</c>, 등록하지 않았으면 컬럼 이름.</summary>
    private string ColumnLabel(string reportId, string column)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"RPT.{reportId}.{column}");
        var text = localizer[key];
        return text == key ? column : text;
    }

    private string Printed(ReportResult result) =>
        localizer["RPT.PRINTED", result.PrintedBy, result.PrintedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)];

    /// <summary>화면에서 바로 만들 때의 최대 행 수.</summary>
    private static int SyncLimit(ReportDefinition definition) => Math.Min(definition.MaxRows, SyncMaxRows);

    /// <summary>PDF 또는 엑셀을 만들고 개인정보 레포트면 다운로드 접속기록을 남긴다. <paramref name="context"/>는 PDF에만 쓴다.</summary>
    private async Task<ReportOutput> BuildAsync(ActionContext? context, ReportDefinition definition, ReportRunRequest request, string formatCd, int limit, CancellationToken cancellationToken)
    {
        var reason = RequireReasonCore(definition, request);
        var result = await RunAsync(definition, request.Params, limit, cancellationToken);
        byte[] content;
        string extension;
        if (formatCd == ReportFormats.Pdf)
        {
            var html = await renderer.RenderAsync(
                context ?? throw new ArgumentNullException(nameof(context)),
                TemplatePath(definition),
                new ReportDocumentModel(result, await ReportStyle.InlineAsync(environment, cancellationToken)));
            content = await pdf.ConvertAsync(html, new PdfPageOptions(Printed(result), definition.Landscape), cancellationToken);
            extension = "pdf";
        }
        else if (formatCd == ReportFormats.Excel)
        {
            var columns = result.Columns
                .Select((c, i) => new ExcelColumn<SpRow>(c.Label, r => ReportFormat.Cell(r[i]), c.Numeric ? 14 : 20))
                .ToList();
            IEnumerable<SpRow> rows = result.Totals is { } totals ? [.. result.Rows, totals] : result.Rows;
            await using var stream = ExcelWriter.Write(new ExcelSheet<SpRow>(result.Title, columns, rows));
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
            extension = "xlsx";
        }
        else
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        if (definition.HasPii)
        {
            await pii.WriteAsync(definition.ReportId, PiiActions.Download, TargetDesc(result, formatCd == ReportFormats.Pdf ? "PDF" : "EXCEL"), reason, cancellationToken);
        }

        return new ReportOutput(content, result.Rows.Count, FileName(definition, extension));
    }

    private static string? RequireReasonCore(ReportDefinition definition, ReportRunRequest request)
    {
        if (!definition.HasPii)
        {
            return null;
        }

        var reason = request.Reason?.Trim();
        return reason is { Length: >= 2 } ? reason : throw new BizException(MessageKeys.PiiReasonRequired);
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,29}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReportIdPattern();

    [GeneratedRegex(@"^[A-Z][A-Z0-9]{1,9}\.USP_RPT_[A-Z0-9_]+_GET_LIST$", RegexOptions.CultureInvariant)]
    private static partial Regex DataSpPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateNamePattern();
}
