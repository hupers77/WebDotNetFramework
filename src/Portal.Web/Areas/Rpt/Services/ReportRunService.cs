using System.Text.Json;
using Portal.Core.Context;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Localization;
using Portal.Data;
using Portal.Framework.Web.Navigation;
using Portal.Infrastructure.Files;
using Portal.Web.Areas.Rpt.Models;

namespace Portal.Web.Areas.Rpt.Services;

/// <summary>
/// 레포트 백그라운드 생성 요청 (설계서 11장 '레포트'). 행이 많아 화면에서 바로 만들 수 없는 레포트를 요청자가 대기열(<c>TB_REPORT_RUN</c>)에 넣고,
/// 내 레포트 화면에서 진행을 보고 완성된 파일을 내려받는다. 만드는 일은 <see cref="ReportRunWorker"/>가 한다.
/// 권한은 화면(Controller)이 확인한다: 요청은 레포트의 PRINT(PDF)·EXPORT(엑셀), 목록·내려받기·삭제는 내 레포트 화면(<c>RPT_RUNS</c>)과 내려받을 때 한 번 더 레포트의 같은 기능.
/// </summary>
public interface IReportRunService
{
    /// <summary>요청을 대기열에 넣는다. 조건이 틀리면 바로 오류를 돌려준다. 요청 번호를 돌려준다.</summary>
    Task<long> QueueAsync(ReportDefinition definition, ReportQueueRequest request, CancellationToken cancellationToken = default);

    /// <summary>내 요청 목록 (최근 것부터).</summary>
    Task<IReadOnlyList<ReportRunItem>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>내 요청 한 건. 없거나 남의 요청이면 null.</summary>
    Task<ReportRunFile?> GetAsync(long runId, CancellationToken cancellationToken = default);

    /// <summary>완성된 파일을 연다.</summary>
    Stream Open(ReportRunFile file);

    /// <summary>내 요청을 지운다.</summary>
    Task DeleteAsync(long runId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ReportRunService(ISpExecutor sp, IReportService reports, IFileStorage storage, IPortalLocalizer localizer, MenuCatalog menus, ICurrentUser currentUser) : IReportRunService
{
    internal const string SaveSp = "FW.USP_REPORT_RUN_SAVE";
    internal const string ListSp = "FW.USP_REPORT_RUN_GET_LIST";
    internal const string GetSp = "FW.USP_REPORT_RUN_GET";
    internal const string DeleteSp = "FW.USP_REPORT_RUN_DEL";

    /// <summary>조건 JSON 최대 길이 (표 컬럼과 같다).</summary>
    private const int MaxParamsLength = 2000;

    /// <inheritdoc />
    public async Task<long> QueueAsync(ReportDefinition definition, ReportQueueRequest request, CancellationToken cancellationToken = default)
    {
        var format = request.Format?.Trim().ToUpperInvariant();
        if (format is not (ReportFormats.Pdf or ReportFormats.Excel))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var reason = reports.RequireReason(definition, request);
        reports.Validate(definition, request.Params);

        // 정의에 있는 조건만, 앞뒤 공백을 뺀 값으로 보관한다
        var names = ReportParams.Parse(definition.ParamsJson).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var values = request.Params
            .Where(p => names.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value?.Trim() ?? "", StringComparer.Ordinal);
        var json = JsonSerializer.Serialize(values);
        if (json.Length > MaxParamsLength)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var saved = await sp.ExecuteAsync(
            SaveSp,
            new { P_REPORT_ID = definition.ReportId, P_FORMAT_CD = format, P_PARAMS_JSON = json, P_REASON = reason },
            cancellationToken);
        return long.TryParse(saved.NewKey, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var runId)
            ? runId
            : throw new InvalidOperationException($"{SaveSp}가 요청 번호를 돌려주지 않았습니다.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReportRunItem>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var items = await sp.QueryAsync<ReportRunItem>(ListSp, null, cancellationToken);
        foreach (var item in items)
        {
            item.ReportNm = menus.ProgramTitle(item.ReportId, currentUser.LangCd);
            if (item.ErrKey is { Length: > 0 } key)
            {
                item.ErrMsg = item.ErrArg is null ? localizer[key] : localizer[key, item.ErrArg];
            }
        }

        return items;
    }

    /// <inheritdoc />
    public Task<ReportRunFile?> GetAsync(long runId, CancellationToken cancellationToken = default) =>
        sp.QuerySingleAsync<ReportRunFile>(GetSp, new { P_RUN_ID = runId }, cancellationToken);

    /// <inheritdoc />
    public Stream Open(ReportRunFile file) =>
        file is { StatusCd: "DONE", StoredPath: { Length: > 0 } path } ? storage.OpenRead(path) : throw new BizException(MessageKeys.NotFound);

    /// <inheritdoc />
    public async Task DeleteAsync(long runId, CancellationToken cancellationToken = default)
    {
        var result = await sp.ExecuteAsync(DeleteSp, new { P_RUN_ID = runId }, cancellationToken);
        if (result.AffectedCnt == 0)
        {
            throw new BizException(MessageKeys.NotFound);
        }
    }
}
