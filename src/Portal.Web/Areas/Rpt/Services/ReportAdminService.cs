using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Razor;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Rpt.Models;

namespace Portal.Web.Areas.Rpt.Services;

/// <summary>레포트 양식(Razor 뷰) 존재 확인. 등록할 때 없는 양식을 거절해 미리보기에서 오류가 나지 않게 한다.</summary>
public interface IReportTemplateCatalog
{
    /// <summary>양식 뷰가 있는지.</summary>
    bool Exists(string templateView);
}

/// <inheritdoc />
public sealed class ReportTemplateCatalog(IRazorViewEngine engine) : IReportTemplateCatalog
{
    /// <inheritdoc />
    public bool Exists(string templateView) =>
        engine.GetView(executingFilePath: null, $"~/Areas/Rpt/Views/Templates/{templateView}.cshtml", isMainPage: true).Success;
}

/// <summary>레포트 관리 (<c>RPT_REPORT_MGT</c>). 프로그램 하나를 레포트로 등록하면 조건 입력·미리보기·PDF·엑셀 화면(<c>/Rpt/Viewer</c>)이 생긴다.</summary>
public interface IReportAdminService
{
    /// <summary>레포트 전체와 등록할 수 있는 프로그램.</summary>
    Task<ReportAdminListResult> GetListAsync(ReportAdminSearch search, CancellationToken cancellationToken = default);

    /// <summary>바뀐 행을 한 번에 저장한다.</summary>
    Task<ReportSaveResult> SaveAsync(ReportSaveRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed partial class ReportAdminService(ISpExecutor sp, IReportTemplateCatalog templates) : IReportAdminService
{
    internal const string ListSp = "FW.USP_REPORT_ADMIN_GET_LIST";
    internal const string SaveSp = "FW.USP_REPORT_SAVE";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public Task<ReportAdminListResult> GetListAsync(ReportAdminSearch search, CancellationToken cancellationToken = default) =>
        sp.QueryMultipleAsync(
            ListSp,
            new { P_KEYWORD = string.IsNullOrWhiteSpace(search.Keyword) ? null : search.Keyword.Trim() },
            async reader =>
            {
                var items = await reader.ReadAsync<ReportAdminDto>(cancellationToken);
                var programs = await reader.ReadAsync<ReportProgramOption>(cancellationToken);
                var total = await reader.ReadSingleAsync<TotalRow>(cancellationToken);
                return new ReportAdminListResult { Items = items, Programs = programs, TotalCount = total?.TotalCnt ?? items.Count };
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<ReportSaveResult> SaveAsync(ReportSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Rows.Count == 0)
        {
            throw new BizException(MessageKeys.NoChanges);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<object>(request.Rows.Count);
        foreach (var row in request.Rows)
        {
            if (row.RowStat is not (RowStates.Inserted or RowStates.Updated or RowStates.Deleted)
                || !IdPattern().IsMatch(row.ReportId)
                || !seen.Add(row.ReportId)
                || (row.RowStat != RowStates.Inserted && row.RowVer is not { Length: 8 }))
            {
                throw new BizException(MessageKeys.InvalidInput);
            }

            if (row.RowStat != RowStates.Deleted)
            {
                Validate(row);
            }

            rows.Add(new
            {
                row.RowStat,
                row.ReportId,
                DataSp = row.DataSp?.Trim(),
                TemplateView = Blank(row.TemplateView),
                ParamsJson = Blank(row.ParamsJson),
                row.OrientCd,
                row.ApprovalYn,
                row.TotalYn,
                row.MaxRows,
                row.UseYn,
                RowVer = row.RowVer is null ? null : Convert.ToHexString(row.RowVer),
            });
        }

        var saved = await sp.ExecuteAsync(SaveSp, new { P_ROWS_JSON = JsonSerializer.Serialize(rows, Json) }, cancellationToken);
        return new ReportSaveResult(saved.AffectedCnt);
    }

    /// <summary>데이터 SP 이름: <c>스키마.USP_RPT_대상_GET_LIST</c> 형식만 (레포트 화면이 이 이름만 실행한다).</summary>
    internal static bool IsDataSp(string? value) => value is { Length: <= 128 } && DataSpPattern().IsMatch(value.Trim());

    private void Validate(ReportSaveRow row)
    {
        var templateView = Blank(row.TemplateView);
        var ok = IsDataSp(row.DataSp)
            && (templateView is null || (TemplatePattern().IsMatch(templateView) && templates.Exists(templateView)))
            && row.OrientCd is "P" or "L"
            && row.ApprovalYn is "Y" or "N" && row.TotalYn is "Y" or "N" && row.UseYn is "Y" or "N"
            && row.MaxRows is >= 1 and <= 100000
            && ParamsValid(Blank(row.ParamsJson));
        if (!ok)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }
    }

    /// <summary>조건 정의는 레포트 화면이 읽는 규칙(이름·형식·기본값)대로 읽혀야 한다.</summary>
    private static bool ParamsValid(string? json)
    {
        if (json is null)
        {
            return true;
        }

        try
        {
            ReportParams.Parse(json);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,29}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*\.USP_RPT_[A-Za-z0-9_]+_GET_LIST$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex DataSpPattern();

    [GeneratedRegex("^[A-Za-z0-9_]{1,50}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex TemplatePattern();

    private sealed class TotalRow
    {
        public int TotalCnt { get; set; }
    }
}
