using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Data;
using Portal.Infrastructure.Localization;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>공지 팝업 관리 (<c>SYS_POPUP_MGT</c>).</summary>
public interface IPopupAdminService
{
    /// <summary>팝업 목록 (언어별 문구 포함).</summary>
    Task<PageResult<PopupDto>> GetListAsync(PopupSearch search, CancellationToken cancellationToken = default);

    /// <summary>팝업 저장 (등록/수정).</summary>
    Task<PopupSaveResult> SaveAsync(PopupSaveRequest request, CancellationToken cancellationToken = default);

    /// <summary>팝업 삭제.</summary>
    Task DeleteAsync(PopupDeleteRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PopupAdminService(ISpExecutor sp, LangResources langs) : IPopupAdminService
{
    /// <summary>한 쪽에 읽는 팝업 수.</summary>
    public const int PageSize = 20;

    internal const string ListSp = "FW.USP_POPUP_GET_LIST";
    internal const string SaveSp = "FW.USP_POPUP_SAVE";
    internal const string DeleteSp = "FW.USP_POPUP_DEL";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public Task<PageResult<PopupDto>> GetListAsync(PopupSearch search, CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<PopupDto>(
            ListSp,
            new
            {
                P_KEYWORD = string.IsNullOrWhiteSpace(search.Keyword) ? null : search.Keyword.Trim(),
                P_USE_YN = string.IsNullOrEmpty(search.UseYn) ? null : search.UseYn,
                P_PAGE_NO = search.PageNo,
                P_PAGE_SIZE = PageSize,
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<PopupSaveResult> SaveAsync(PopupSaveRequest request, CancellationToken cancellationToken = default)
    {
        // 사용 중인 언어만, 같은 언어는 한 번만, 기본 언어는 반드시. 제목·내용은 공백만 있으면 안 된다
        var known = langs.Languages.Select(l => l.LangCd).ToHashSet(StringComparer.Ordinal);
        var defaultLang = langs.Languages.FirstOrDefault(l => l.IsDefault)?.LangCd ?? LangResources.DefaultLang;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (request.Langs.Any(l => !known.Contains(l.LangCd) || !seen.Add(l.LangCd) || string.IsNullOrWhiteSpace(l.Title) || string.IsNullOrWhiteSpace(l.Content))
            || !seen.Contains(defaultLang))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var texts = request.Langs.Select(l => new { l.LangCd, Title = l.Title.Trim(), Content = l.Content.Trim() });
        var result = await sp.ExecuteAsync(
            SaveSp,
            new
            {
                P_POPUP_ID = request.RowVer is null ? null : request.PopupId,
                P_ROW_VER = request.RowVer,
                P_OPEN_SCOPE_CD = request.OpenScopeCd,
                P_START_DT = request.StartDt,
                P_END_DT = request.EndDt,
                P_USE_YN = request.UseYn,
                P_LANGS_JSON = JsonSerializer.Serialize(texts, Json),
            },
            cancellationToken);
        return new PopupSaveResult(long.TryParse(result.NewKey, out var id) ? id : request.PopupId ?? 0);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(PopupDeleteRequest request, CancellationToken cancellationToken = default) =>
        await sp.ExecuteAsync(DeleteSp, new { P_POPUP_ID = request.PopupId, P_ROW_VER = request.RowVer }, cancellationToken);
}
