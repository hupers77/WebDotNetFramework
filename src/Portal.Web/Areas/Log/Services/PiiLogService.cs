using System.Globalization;
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

/// <summary>개인정보 접속기록 조회 (<c>LOG_PII_INQ</c>). 개인정보를 조회·다운로드·마스킹 해제한 기록을 읽기만 한다(10년 보관, 해시 체인).</summary>
/// <remarks>기간은 31일까지(SP가 강제). 데이터 범위가 전체가 아니면 범위 안 계정의 기록만 보인다.
/// 사용자 ID·접속 IP가 들어 있어 엑셀은 사유를 받고, 그 내려받기도 개인정보 접속기록에 남긴다.</remarks>
public interface IPiiLogService
{
    /// <summary>개인정보 접속기록 한 쪽 (화면 이름은 메뉴명).</summary>
    Task<PageResult<PiiLogDto>> GetListAsync(PiiLogSearch search, CancellationToken cancellationToken = default);

    /// <summary>검색 조건에 맞는 기록 전체를 엑셀로 (<see cref="PiiLogService.MaxExportRows"/>건까지).</summary>
    Task<Stream> ExportAsync(PiiLogSearch search, string reason, CancellationToken cancellationToken = default);

    /// <summary>메뉴에 있는 프로그램 (ID, 화면 이름), 메뉴 순서.</summary>
    IReadOnlyList<(string Code, string Name)> GetPrograms();
}

/// <inheritdoc />
public sealed class PiiLogService(ISpExecutor sp, MenuCatalog menus, ICurrentUser currentUser, IPortalLocalizer localizer, IPiiAccessLog pii) : IPiiLogService
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "LOG_PII_INQ";

    /// <summary>쪽 크기.</summary>
    public const int PageSize = 50;

    /// <summary>한 번에 내보낼 수 있는 최대 행 수. 넘으면 조건을 좁혀 달라고 한다.</summary>
    public const int MaxExportRows = 10000;

    internal const string ListSp = "LOG.USP_PII_ACCESS_LOG_GET_LIST";

    // 목록 SP의 페이지 크기 상한
    private const int ExportPageSize = 1000;

    private static readonly JsonSerializerOptions TargetJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <inheritdoc />
    public async Task<PageResult<PiiLogDto>> GetListAsync(PiiLogSearch search, CancellationToken cancellationToken = default)
    {
        var page = await sp.QueryPageAsync<PiiLogDto>(ListSp, Parameters(search, search.PageNo, PageSize), cancellationToken);
        FillProgramNames(page.Items);
        return page;
    }

    /// <inheritdoc />
    public async Task<Stream> ExportAsync(PiiLogSearch search, string reason, CancellationToken cancellationToken = default)
    {
        var rows = new List<PiiLogDto>();
        for (var pageNo = 1; ; pageNo++)
        {
            var page = await sp.QueryPageAsync<PiiLogDto>(ListSp, Parameters(search, pageNo, ExportPageSize), cancellationToken);
            if (page.TotalCount > MaxExportRows)
            {
                throw new BizException(LogMessageKeys.ExportTooMany, MaxExportRows);
            }

            rows.AddRange(page.Items);
            if (page.Items.Count < ExportPageSize || rows.Count >= page.TotalCount)
            {
                break;
            }
        }

        FillProgramNames(rows);
        await pii.WriteAsync(ProgramId, PiiActions.Download, TargetDesc(search, rows.Count), reason, cancellationToken);
        return ExcelWriter.Write(new ExcelSheet<PiiLogDto>(localizer["LBL.PII_LOG"], Columns(), rows));
    }

    /// <inheritdoc />
    public IReadOnlyList<(string Code, string Name)> GetPrograms() => LogPrograms.List(menus, currentUser.LangCd);

    /// <summary>개인정보 접속기록의 대상: 건수·기간과 조건. 사용자 ID·IP는 넣었는지만 남긴다.</summary>
    internal static string TargetDesc(PiiLogSearch search, int count) =>
        JsonSerializer.Serialize(
            new
            {
                cnt = count,
                from = search.FromDt?.UtcDateTime,
                to = search.ToDt?.UtcDateTime,
                programId = Blank(search.ProgramId),
                action = Blank(search.ActionCd),
                userYn = string.IsNullOrWhiteSpace(search.UserId) ? null : "Y",
                ipYn = string.IsNullOrWhiteSpace(search.Ip) ? null : "Y",
            },
            TargetJson);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static object Parameters(PiiLogSearch search, int pageNo, int pageSize) => new
    {
        P_FROM_DT = search.FromDt?.UtcDateTime,
        P_TO_DT = search.ToDt?.UtcDateTime,
        P_TARGET_USER_ID = Blank(search.UserId),
        P_PROGRAM_ID = Blank(search.ProgramId),
        P_ACTION_CD = Blank(search.ActionCd),
        P_IP = Blank(search.Ip),
        P_PAGE_NO = pageNo,
        P_PAGE_SIZE = pageSize,
    };

    private void FillProgramNames(IEnumerable<PiiLogDto> rows)
    {
        foreach (var row in rows)
        {
            row.ProgramNm = menus.ProgramTitle(row.ProgramId, currentUser.LangCd);
        }
    }

    private List<ExcelColumn<PiiLogDto>> Columns() =>
    [
        new(localizer["LBL.LOG_DT"], r => DateTime.SpecifyKind(r.LogDt, DateTimeKind.Utc).ToLocalTime(), 20),
        new(localizer["LBL.USER_ID"], r => r.UserId, 16),
        new(localizer["LBL.PROGRAM"], r => r.ProgramNm ?? r.ProgramId, 20),
        new(localizer["LBL.ACTION"], r => localizer[string.Create(CultureInfo.InvariantCulture, $"LOG.PII.ACTION_{r.ActionCd}")]),
        new(localizer["LBL.TARGET_DESC"], r => r.TargetDesc, 50),
        new(localizer["LBL.REASON"], r => r.Reason, 30),
        new(localizer["LBL.CLIENT_IP"], r => r.ClientIp, 16),
    ];
}
