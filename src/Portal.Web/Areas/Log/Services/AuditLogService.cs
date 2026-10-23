using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portal.Core.Errors;
using Portal.Core.Localization;
using Portal.Core.Results;
using Portal.Data;
using Portal.Framework.Web.Logging;
using Portal.Infrastructure.Excel;
using Portal.Web.Areas.Log.Models;

namespace Portal.Web.Areas.Log.Services;

/// <summary>데이터 변경 기록 조회 (<c>LOG_AUDIT_INQ</c>). 변경 SP가 남긴 변경 전후 값(JSON)을 읽기만 한다.</summary>
/// <remarks>변경 내용에는 여러 회사의 데이터가 섞이므로 데이터 범위가 전체인 사용자만 본다(SP가 강제). 기간은 31일까지.
/// 변경 내용에 이름 등이 있을 수 있어 한 건을 열면 개인정보 접속기록(조회)을, 엑셀은 사유를 받아 (다운로드)를 남긴다.</remarks>
public interface IAuditLogService
{
    /// <summary>기록이 있는 테이블 이름.</summary>
    Task<IReadOnlyList<string>> GetTablesAsync(CancellationToken cancellationToken = default);

    /// <summary>변경 기록 한 쪽 (변경 전후 값 없이).</summary>
    Task<PageResult<AuditLogDto>> GetListAsync(AuditLogSearch search, CancellationToken cancellationToken = default);

    /// <summary>변경 기록 한 건 (변경 전후 값 포함). 없거나 볼 수 없으면 null.</summary>
    Task<AuditLogDto?> GetAsync(long logId, DateTime logDt, CancellationToken cancellationToken = default);

    /// <summary>검색 조건에 맞는 기록 전체를 변경 전후 값과 함께 엑셀로 (<see cref="AuditLogService.MaxExportRows"/>건까지).</summary>
    Task<Stream> ExportAsync(AuditLogSearch search, string reason, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class AuditLogService(ISpExecutor sp, IPortalLocalizer localizer, IPiiAccessLog pii) : IAuditLogService
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "LOG_AUDIT_INQ";

    /// <summary>쪽 크기.</summary>
    public const int PageSize = 50;

    /// <summary>한 번에 내보낼 수 있는 최대 행 수 (변경 내용이 길어 접근 기록보다 적다).</summary>
    public const int MaxExportRows = 5000;

    internal const string TableListSp = "LOG.USP_AUDIT_TABLE_GET_LIST";
    internal const string ListSp = "LOG.USP_AUDIT_LOG_GET_LIST";
    internal const string GetSp = "LOG.USP_AUDIT_LOG_GET";

    // 목록 SP의 페이지 크기 상한
    private const int ExportPageSize = 1000;

    // 엑셀 셀 하나에 넣을 수 있는 글자 수(32,767)보다 조금 작게 자른다
    private const int MaxCellLength = 32000;

    private static readonly JsonSerializerOptions TargetJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetTablesAsync(CancellationToken cancellationToken = default) =>
        [.. (await sp.QueryAsync<TableRow>(TableListSp, null, cancellationToken)).Select(r => r.TableNm)];

    /// <inheritdoc />
    public Task<PageResult<AuditLogDto>> GetListAsync(AuditLogSearch search, CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<AuditLogDto>(ListSp, Parameters(search, search.PageNo, PageSize, false), cancellationToken);

    /// <inheritdoc />
    public async Task<AuditLogDto?> GetAsync(long logId, DateTime logDt, CancellationToken cancellationToken = default)
    {
        var row = await sp.QuerySingleAsync<AuditLogDto>(
            GetSp,
            new { P_LOG_ID = logId, P_LOG_DT = DateTime.SpecifyKind(logDt, DateTimeKind.Utc) },
            cancellationToken);
        if (row is not null)
        {
            await pii.WriteAsync(ProgramId, PiiActions.View, JsonSerializer.Serialize(new { logId, table = row.TableNm }), null, cancellationToken);
        }

        return row;
    }

    /// <inheritdoc />
    public async Task<Stream> ExportAsync(AuditLogSearch search, string reason, CancellationToken cancellationToken = default)
    {
        var rows = new List<AuditLogDto>();
        for (var pageNo = 1; ; pageNo++)
        {
            var page = await sp.QueryPageAsync<AuditLogDto>(ListSp, Parameters(search, pageNo, ExportPageSize, true), cancellationToken);
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

        await pii.WriteAsync(ProgramId, PiiActions.Download, TargetDesc(search, rows.Count), reason, cancellationToken);
        return ExcelWriter.Write(new ExcelSheet<AuditLogDto>(localizer["LBL.AUDIT_LOG"], Columns(), rows));
    }

    /// <summary>개인정보 접속기록의 대상: 건수·기간과 조건. 대상 키·사용자 ID·IP는 개인정보일 수 있어 넣었는지만 남긴다.</summary>
    internal static string TargetDesc(AuditLogSearch search, int count) =>
        JsonSerializer.Serialize(
            new
            {
                cnt = count,
                from = search.FromDt?.UtcDateTime,
                to = search.ToDt?.UtcDateTime,
                table = Blank(search.TableNm),
                action = Blank(search.ActionCd),
                keyYn = string.IsNullOrWhiteSpace(search.RowKey) ? null : "Y",
                userYn = string.IsNullOrWhiteSpace(search.UserId) ? null : "Y",
                ipYn = string.IsNullOrWhiteSpace(search.Ip) ? null : "Y",
            },
            TargetJson);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Cut(string? value) => value is { Length: > MaxCellLength } ? value[..MaxCellLength] : value;

    private static object Parameters(AuditLogSearch search, int pageNo, int pageSize, bool withJson) => new
    {
        P_FROM_DT = search.FromDt?.UtcDateTime,
        P_TO_DT = search.ToDt?.UtcDateTime,
        P_TABLE_NM = Blank(search.TableNm),
        P_ACTION_CD = Blank(search.ActionCd),
        P_ROW_KEY = Blank(search.RowKey),
        P_TARGET_USER_ID = Blank(search.UserId),
        P_IP = Blank(search.Ip),
        P_JSON_YN = withJson ? "Y" : "N",
        P_PAGE_NO = pageNo,
        P_PAGE_SIZE = pageSize,
    };

    private List<ExcelColumn<AuditLogDto>> Columns() =>
    [
        new(localizer["LBL.LOG_DT"], r => DateTime.SpecifyKind(r.LogDt, DateTimeKind.Utc).ToLocalTime(), 20),
        new(localizer["LBL.CHG_USER"], r => r.UserId, 16),
        new(localizer["LBL.TABLE"], r => r.TableNm, 26),
        new(localizer["LBL.ACTION"], r => localizer[string.Create(CultureInfo.InvariantCulture, $"LOG.AUDIT.ACTION_{r.ActionCd}")]),
        new(localizer["LBL.ROW_KEY"], r => r.RowKey, 24),
        new(localizer["LBL.OLD_VAL"], r => Cut(r.BeforeJson), 50),
        new(localizer["LBL.NEW_VAL"], r => Cut(r.AfterJson), 50),
        new(localizer["LBL.CLIENT_IP"], r => r.ClientIp, 16),
    ];

    /// <summary>테이블 이름 한 줄 (<c>LOG.USP_AUDIT_TABLE_GET_LIST</c>).</summary>
    internal sealed class TableRow
    {
        /// <summary>테이블.</summary>
        public string TableNm { get; set; } = "";
    }
}
