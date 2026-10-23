using System.Globalization;
using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Data;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>휴일·달력 관리 (<c>SYS_HOLIDAY_MGT</c>). 한 해씩 조회하고 추가·수정·삭제를 한 번에 저장한다.</summary>
public interface IHolidayAdminService
{
    /// <summary>한 해의 등록된 날짜.</summary>
    Task<PageResult<HolidayDto>> GetListAsync(int year, CancellationToken cancellationToken = default);

    /// <summary>바뀐 행을 한 번에 저장한다.</summary>
    Task<HolidaySaveResult> SaveAsync(HolidaySaveRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class HolidayAdminService(ISpExecutor sp) : IHolidayAdminService
{
    internal const string ListSp = "FW.USP_HOLIDAY_GET_LIST";
    internal const string SaveSp = "FW.USP_HOLIDAY_SAVE";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public Task<PageResult<HolidayDto>> GetListAsync(int year, CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<HolidayDto>(ListSp, new { P_YEAR = year }, cancellationToken);

    /// <inheritdoc />
    public async Task<HolidaySaveResult> SaveAsync(HolidaySaveRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Rows.Count == 0)
        {
            throw new BizException(MessageKeys.NoChanges);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<object>(request.Rows.Count);
        foreach (var row in request.Rows)
        {
            if (!DateOnly.TryParseExact(row.HolidayDt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Year is < 1990 or > 2100)
            {
                throw new BizException(MessageKeys.InvalidInput);
            }

            if (!seen.Add(row.HolidayDt))
            {
                throw new BizException("VAL.DUP_ROW", row.HolidayDt);
            }

            var needsVer = row.RowStat is RowStates.Updated or RowStates.Deleted;
            var needsName = row.RowStat is RowStates.Inserted or RowStates.Updated;
            if (row.RowStat is not (RowStates.Inserted or RowStates.Updated or RowStates.Deleted)
                || (needsVer && row.RowVer is not { Length: 8 })
                || (needsName && string.IsNullOrWhiteSpace(row.HolidayNm))
                || row.WorkYn is not ("Y" or "N"))
            {
                throw new BizException(MessageKeys.InvalidInput);
            }

            rows.Add(new
            {
                row.RowStat,
                row.HolidayDt,
                HolidayNm = row.HolidayNm?.Trim(),
                row.WorkYn,
                RowVer = row.RowVer is null ? null : Convert.ToHexString(row.RowVer),
            });
        }

        var saved = await sp.ExecuteAsync(SaveSp, new { P_ROWS_JSON = JsonSerializer.Serialize(rows, Json) }, cancellationToken);
        return new HolidaySaveResult(saved.AffectedCnt);
    }
}
