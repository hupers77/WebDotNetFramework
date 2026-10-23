using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Portal.Core.Context;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Security.Authorization;
using Portal.Web.Areas.Dash.Models;

namespace Portal.Web.Areas.Dash.Services;

/// <summary>대시보드 위젯. 현재 사용자가 볼 수 있는 위젯과 그 데이터를 준다.</summary>
public interface IDashService
{
    /// <summary>볼 수 있는 위젯 (사용 중이고 위젯의 프로그램 읽기 권한이 있는 것).</summary>
    Task<IReadOnlyList<WidgetDto>> GetWidgetsAsync(PermissionSet permissions, CancellationToken cancellationToken = default);

    /// <summary>위젯 하나의 데이터. 볼 수 없거나 없는 위젯은 없는 것과 같다(NOT_FOUND).</summary>
    Task<IReadOnlyList<WidgetRowDto>> GetDataAsync(string widgetCd, PermissionSet permissions, CancellationToken cancellationToken = default);

    /// <summary>지금 홈 화면에 띄울 공지 팝업 (기간·공개 대상이 맞는 것).</summary>
    Task<IReadOnlyList<PopupActiveDto>> GetPopupsAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed partial class DashService(ISpExecutor sp, IMemoryCache cache, ICurrentUser currentUser) : IDashService
{
    internal const string ListSp = "FW.USP_WIDGET_GET_LIST";
    internal const string PopupSp = "FW.USP_POPUP_ACTIVE_GET_LIST";

    /// <summary>화면에 줄 최대 행 수 (목록 위젯이 SP 실수로 너무 많이 돌려줘도 막는다).</summary>
    internal const int MaxRows = 20;

    [GeneratedRegex("^[A-Z]{2,10}\\.USP_WIDGET_[A-Z0-9_]{1,60}$", RegexOptions.CultureInvariant)]
    private static partial Regex DataSpPattern();

    /// <inheritdoc />
    public async Task<IReadOnlyList<WidgetDto>> GetWidgetsAsync(PermissionSet permissions, CancellationToken cancellationToken = default) =>
        (await sp.QueryAsync<WidgetDto>(ListSp, cancellationToken: cancellationToken))
            .Where(w => w.TypeCd is WidgetTypes.Card or WidgetTypes.List && CanView(w, permissions))
            .ToList();

    /// <inheritdoc />
    public async Task<IReadOnlyList<WidgetRowDto>> GetDataAsync(string widgetCd, PermissionSet permissions, CancellationToken cancellationToken = default)
    {
        var widget = (await GetWidgetsAsync(permissions, cancellationToken)).FirstOrDefault(w => w.WidgetCd == widgetCd)
            ?? throw new BizException(MessageKeys.NotFound);
        // DB에서 온 값이지만 위젯 SP가 아닌 것을 실행하지 않도록 한 번 더 확인한다
        if (!DataSpPattern().IsMatch(widget.DataSp))
        {
            throw new BizException(MessageKeys.NotFound);
        }

        var key = $"dash:{currentUser.UserId}:{currentUser.LangCd}:{widget.WidgetCd}";
        if (widget.CacheSec > 0 && cache.TryGetValue(key, out IReadOnlyList<WidgetRowDto>? cached) && cached is not null)
        {
            return cached;
        }

        var rows = (await sp.QueryAsync<WidgetRowDto>(widget.DataSp, cancellationToken: cancellationToken))
            .Take(MaxRows)
            .Select(Clean)
            .ToList();
        if (widget.CacheSec > 0)
        {
            cache.Set(key, (IReadOnlyList<WidgetRowDto>)rows, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(widget.CacheSec) });
        }

        return rows;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PopupActiveDto>> GetPopupsAsync(CancellationToken cancellationToken = default) =>
        sp.QueryAsync<PopupActiveDto>(PopupSp, cancellationToken: cancellationToken);

    private static bool CanView(WidgetDto widget, PermissionSet permissions) =>
        widget.ProgramId is null || permissions.Can(widget.ProgramId, FuncCodes.Read);

    /// <summary>바로가기는 사이트 안 경로만 허용한다 (다른 사이트·스크립트 주소 차단).</summary>
    private static WidgetRowDto Clean(WidgetRowDto row)
    {
        if (row.Link is { } link && !(link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal) && !link.Contains('\\')))
        {
            row.Link = null;
        }

        return row;
    }
}
