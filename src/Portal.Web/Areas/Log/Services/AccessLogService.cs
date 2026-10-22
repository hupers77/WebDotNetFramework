using System.Text.Json;
using System.Text.Json.Serialization;
using Portal.Core.Context;
using Portal.Core.Errors;
using Portal.Core.Localization;
using Portal.Core.Results;
using Portal.Data;
using Portal.Framework.Web.Logging;
using Portal.Framework.Web.Navigation;
using Portal.Infrastructure.Excel;
using Portal.Web.Areas.Log.Models;

namespace Portal.Web.Areas.Log.Services;

/// <summary>로그인·접근 기록 조회 (<c>LOG_ACCESS_INQ</c>). 로그는 10년 보관하고 해시 체인으로 위·변조를 막으며, 여기서는 읽기만 한다.</summary>
/// <remarks>기간은 31일까지(SP가 강제). 데이터 범위가 전체가 아니면 범위 안 계정의 기록만 보인다.
/// 접속 IP가 들어 있어 엑셀은 사유를 받고 개인정보 접속기록을 남긴 뒤 만든다.</remarks>
public interface IAccessLogService
{
    /// <summary>로그인 기록 한 쪽.</summary>
    Task<PageResult<LoginLogDto>> GetLoginsAsync(AccessLogSearch search, CancellationToken cancellationToken = default);

    /// <summary>화면·API 접근 기록 한 쪽 (화면 이름은 메뉴명).</summary>
    Task<PageResult<AccessLogDto>> GetAccessesAsync(AccessLogSearch search, CancellationToken cancellationToken = default);

    /// <summary>검색 조건에 맞는 기록 전체를 엑셀로 (<see cref="AccessLogService.MaxExportRows"/>건까지).</summary>
    Task<Stream> ExportAsync(AccessLogSearch search, string reason, CancellationToken cancellationToken = default);

    /// <summary>메뉴에 있는 프로그램 (ID, 화면 이름), 메뉴 순서.</summary>
    IReadOnlyList<(string Code, string Name)> GetPrograms();
}

/// <inheritdoc />
public sealed class AccessLogService(ISpExecutor sp, MenuCatalog menus, ICurrentUser currentUser, IPortalLocalizer localizer, IPiiAccessLog pii) : IAccessLogService
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "LOG_ACCESS_INQ";

    /// <summary>쪽 크기.</summary>
    public const int PageSize = 50;

    /// <summary>한 번에 내보낼 수 있는 최대 행 수. 넘으면 조건을 좁혀 달라고 한다.</summary>
    public const int MaxExportRows = 10000;

    internal const string LoginListSp = "LOG.USP_LOGIN_LOG_GET_LIST";
    internal const string AccessListSp = "LOG.USP_ACCESS_LOG_GET_LIST";

    // 목록 SP의 페이지 크기 상한
    private const int ExportPageSize = 1000;

    private static readonly JsonSerializerOptions TargetJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <inheritdoc />
    public Task<PageResult<LoginLogDto>> GetLoginsAsync(AccessLogSearch search, CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<LoginLogDto>(LoginListSp, Parameters(search, search.PageNo, PageSize), cancellationToken);

    /// <inheritdoc />
    public async Task<PageResult<AccessLogDto>> GetAccessesAsync(AccessLogSearch search, CancellationToken cancellationToken = default)
    {
        var page = await sp.QueryPageAsync<AccessLogDto>(AccessListSp, Parameters(search, search.PageNo, PageSize), cancellationToken);
        FillProgramNames(page.Items);
        return page;
    }

    /// <inheritdoc />
    public async Task<Stream> ExportAsync(AccessLogSearch search, string reason, CancellationToken cancellationToken = default)
    {
        if (search.Kind == AccessLogKinds.Login)
        {
            var rows = await AllAsync<LoginLogDto>(LoginListSp, search, cancellationToken);
            await pii.WriteAsync(ProgramId, PiiActions.Download, TargetDesc(search, rows.Count), reason, cancellationToken);
            return ExcelWriter.Write(new ExcelSheet<LoginLogDto>(localizer["LOG.ACCESS.LOGIN"], LoginColumns(), rows));
        }

        var accesses = await AllAsync<AccessLogDto>(AccessListSp, search, cancellationToken);
        FillProgramNames(accesses);
        await pii.WriteAsync(ProgramId, PiiActions.Download, TargetDesc(search, accesses.Count), reason, cancellationToken);
        return ExcelWriter.Write(new ExcelSheet<AccessLogDto>(localizer["LOG.ACCESS.ACCESS"], AccessColumns(), accesses));
    }

    /// <inheritdoc />
    public IReadOnlyList<(string Code, string Name)> GetPrograms()
    {
        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Walk(MenuNode node)
        {
            if (node.ProgramId is { } programId && seen.Add(programId))
            {
                result.Add((programId, menus.ProgramTitle(programId, currentUser.LangCd)));
            }

            foreach (var child in node.Children)
            {
                Walk(child);
            }
        }

        foreach (var root in menus.Roots(MenuSets.Portal))
        {
            Walk(root);
        }

        return result;
    }

    /// <summary>개인정보 접속기록의 대상: 종류·건수·기간과 조건을 넣었는지. 사용자 ID·IP는 남기지 않는다.</summary>
    internal static string TargetDesc(AccessLogSearch search, int count) =>
        JsonSerializer.Serialize(
            new
            {
                kind = search.Kind,
                cnt = count,
                from = search.FromDt?.UtcDateTime,
                to = search.ToDt?.UtcDateTime,
                userYn = string.IsNullOrWhiteSpace(search.UserId) ? null : "Y",
                ipYn = string.IsNullOrWhiteSpace(search.Ip) ? null : "Y",
                resultCd = Blank(search.ResultCd),
                programId = Blank(search.ProgramId),
                errorYn = Blank(search.ErrorYn),
            },
            TargetJson);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    // 두 SP의 파라미터가 달라 종류별로 만든다 (없는 파라미터를 보내면 SP가 거부한다)
    private static object Parameters(AccessLogSearch search, int pageNo, int pageSize) => search.Kind == AccessLogKinds.Login
        ? new
        {
            P_FROM_DT = search.FromDt?.UtcDateTime,
            P_TO_DT = search.ToDt?.UtcDateTime,
            P_TARGET_USER_ID = Blank(search.UserId),
            P_RESULT_CD = Blank(search.ResultCd),
            P_IP = Blank(search.Ip),
            P_PAGE_NO = pageNo,
            P_PAGE_SIZE = pageSize,
        }
        : new
        {
            P_FROM_DT = search.FromDt?.UtcDateTime,
            P_TO_DT = search.ToDt?.UtcDateTime,
            P_TARGET_USER_ID = Blank(search.UserId),
            P_PROGRAM_ID = Blank(search.ProgramId),
            P_ERROR_YN = Blank(search.ErrorYn),
            P_IP = Blank(search.Ip),
            P_PAGE_NO = pageNo,
            P_PAGE_SIZE = pageSize,
        };

    private async Task<List<T>> AllAsync<T>(string spName, AccessLogSearch search, CancellationToken cancellationToken)
    {
        var rows = new List<T>();
        for (var pageNo = 1; ; pageNo++)
        {
            var page = await sp.QueryPageAsync<T>(spName, Parameters(search, pageNo, ExportPageSize), cancellationToken);
            if (page.TotalCount > MaxExportRows)
            {
                throw new BizException(LogMessageKeys.ExportTooMany, MaxExportRows);
            }

            rows.AddRange(page.Items);
            if (page.Items.Count < ExportPageSize || rows.Count >= page.TotalCount)
            {
                return rows;
            }
        }
    }

    private void FillProgramNames(IEnumerable<AccessLogDto> rows)
    {
        foreach (var row in rows.Where(r => r.ProgramId is not null))
        {
            row.ProgramNm = menus.ProgramTitle(row.ProgramId!, currentUser.LangCd);
        }
    }

    private List<ExcelColumn<LoginLogDto>> LoginColumns() =>
    [
        new(localizer["LBL.LOG_DT"], r => Local(r.LogDt), 20),
        new(localizer["LBL.USER_ID"], r => r.UserId, 16),
        new(localizer["LBL.RESULT"], r => r.LoginResultNm, 16),
        new(localizer["LBL.AUTH_METHOD"], r => r.AuthMethodCd),
        new(localizer["LBL.CLIENT_IP"], r => r.ClientIp, 16),
        new(localizer["LBL.USER_AGENT"], r => r.UserAgent, 40),
    ];

    private List<ExcelColumn<AccessLogDto>> AccessColumns() =>
    [
        new(localizer["LBL.LOG_DT"], r => Local(r.LogDt), 20),
        new(localizer["LBL.USER_ID"], r => r.UserId, 16),
        new(localizer["LBL.PROGRAM"], r => r.ProgramNm ?? r.ProgramId, 20),
        new(localizer["LBL.FUNC"], r => r.FuncCd),
        new(localizer["LBL.HTTP_METHOD"], r => r.HttpMethod),
        new(localizer["LBL.REQUEST_PATH"], r => r.RequestPath, 36),
        new(localizer["LBL.STATUS_CODE"], r => r.StatusCode),
        new(localizer["LBL.ELAPSED_MS"], r => r.ElapsedMs),
        new(localizer["LBL.CLIENT_IP"], r => r.ClientIp, 16),
        new(localizer["LBL.TRACE_ID"], r => r.TraceId, 34),
    ];
}
