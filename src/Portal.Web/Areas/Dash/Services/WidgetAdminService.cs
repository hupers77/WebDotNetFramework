using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Data;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Dash.Models;

namespace Portal.Web.Areas.Dash.Services;

/// <summary>위젯 관리 (<c>DSH_WIDGET_MGT</c>). 제목·칸 수·캐시·정렬·사용 여부만 고친다. 위젯 추가·삭제와 데이터 SP 변경은 없다.</summary>
public interface IWidgetAdminService
{
    /// <summary>위젯 목록 (사용 안 함 포함).</summary>
    Task<PageResult<WidgetAdminDto>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>바뀐 위젯을 한 번에 저장한다. 홈의 캐시된 데이터는 캐시 시간이 지나면 새로 읽는다.</summary>
    Task<WidgetAdminSaveResult> SaveAsync(WidgetAdminSaveRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class WidgetAdminService(ISpExecutor sp) : IWidgetAdminService
{
    internal const string ListSp = "FW.USP_WIDGET_ADMIN_GET_LIST";
    internal const string SaveSp = "FW.USP_WIDGET_SAVE";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public Task<PageResult<WidgetAdminDto>> GetListAsync(CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<WidgetAdminDto>(ListSp, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<WidgetAdminSaveResult> SaveAsync(WidgetAdminSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Rows.Count == 0)
        {
            throw new BizException(MessageKeys.NoChanges);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<object>(request.Rows.Count);
        foreach (var row in request.Rows)
        {
            if (row.RowStat != RowStates.Updated || row.RowVer is not { Length: 8 } || !seen.Add(row.WidgetCd) || row.UseYn is not ("Y" or "N"))
            {
                throw new BizException(MessageKeys.InvalidInput);
            }

            rows.Add(new
            {
                row.WidgetCd,
                NameKo = row.NameKo.Trim(),
                NameEn = row.NameEn.Trim(),
                NameZh = row.NameZh.Trim(),
                row.SpanCols, row.CacheSec, row.SortNo, row.UseYn,
                RowVer = Convert.ToHexString(row.RowVer),
            });
        }

        var saved = await sp.ExecuteAsync(SaveSp, new { P_ROWS_JSON = JsonSerializer.Serialize(rows, Json) }, cancellationToken);
        return new WidgetAdminSaveResult(saved.AffectedCnt);
    }
}
