using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Localization;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>메뉴 관리 (<c>SYS_MENU_MGT</c>). 한 메뉴 세트의 트리를 읽고 추가·수정·삭제를 한 번에 저장한다.</summary>
public interface IMenuAdminService
{
    /// <summary>한 메뉴 세트의 메뉴(트리 순서)와 고를 수 있는 프로그램.</summary>
    Task<MenuListResult> GetListAsync(string menuSetCd, CancellationToken cancellationToken = default);

    /// <summary>바뀐 행을 한 번에 저장한다.</summary>
    Task<MenuSaveResult> SaveAsync(MenuSaveRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class MenuAdminService(ISpExecutor sp, LangResources langs) : IMenuAdminService
{
    internal const string ListSp = "FW.USP_MENU_ADMIN_GET_LIST";
    internal const string SaveSp = "FW.USP_MENU_SAVE";

    /// <summary>메뉴 세트.</summary>
    internal static readonly string[] MenuSets = ["PORTAL", "ADMIN", "MOBILE"];

    private static readonly string[] Types = ["FOLDER", "PAGE", "LINK", "POPUP"];
    private static readonly string[] Scopes = ["ALL", "EMP", "PARTNER"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<MenuListResult> GetListAsync(string menuSetCd, CancellationToken cancellationToken = default)
    {
        if (!MenuSets.Contains(menuSetCd, StringComparer.Ordinal))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        return await sp.QueryMultipleAsync(
            ListSp,
            new { P_MENU_SET_CD = menuSetCd },
            async reader =>
            {
                var items = await reader.ReadAsync<MenuAdminDto>(cancellationToken);
                var names = (await reader.ReadAsync<MenuNameRow>(cancellationToken)).ToLookup(n => n.MenuKey, StringComparer.Ordinal);
                var programs = await reader.ReadAsync<MenuProgramOption>(cancellationToken);
                var total = await reader.ReadSingleAsync<TotalRow>(cancellationToken);
                foreach (var item in items)
                {
                    item.Names = names[item.MenuKey].ToDictionary(n => n.LangCd, n => n.MenuNm, StringComparer.Ordinal);
                }

                return new MenuListResult { Items = items, Programs = programs, TotalCount = total?.TotalCnt ?? items.Count };
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MenuSaveResult> SaveAsync(MenuSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Rows.Count == 0)
        {
            throw new BizException(MessageKeys.NoChanges);
        }

        if (!MenuSets.Contains(request.MenuSetCd, StringComparer.Ordinal))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var known = langs.Languages.Select(l => l.LangCd).ToHashSet(StringComparer.Ordinal);
        var defaultLang = langs.Languages.FirstOrDefault(l => l.IsDefault)?.LangCd ?? LangResources.DefaultLang;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<object>(request.Rows.Count);
        foreach (var row in request.Rows)
        {
            var save = row.RowStat is RowStates.Inserted or RowStates.Updated;
            if (row.RowStat is not (RowStates.Inserted or RowStates.Updated or RowStates.Deleted)
                || !IsKey(row.MenuKey)
                || !seen.Add(row.MenuKey)
                || (row.RowStat != RowStates.Inserted && row.RowVer is not { Length: 8 }))
            {
                throw new BizException(MessageKeys.InvalidInput);
            }

            if (save)
            {
                Validate(row, known, defaultLang);
            }

            rows.Add(new
            {
                row.RowStat,
                row.MenuKey,
                ParentMenuKey = Blank(row.ParentMenuKey),
                MenuTypeCd = row.MenuTypeCd,
                ProgramId = Blank(row.ProgramId),
                LinkUrl = Blank(row.LinkUrl),
                IconCd = Blank(row.IconCd),
                row.SortNo,
                row.NewWinYn,
                row.OpenScopeCd,
                row.UseYn,
                RowVer = row.RowVer is null ? null : Convert.ToHexString(row.RowVer),
                Names = save
                    ? row.Names.Where(n => !string.IsNullOrWhiteSpace(n.Value)).Select(n => new { LangCd = n.Key, MenuNm = n.Value!.Trim() })
                    : [],
            });
        }

        var saved = await sp.ExecuteAsync(SaveSp, new { P_MENU_SET_CD = request.MenuSetCd, P_ROWS_JSON = JsonSerializer.Serialize(rows, Json) }, cancellationToken);
        return new MenuSaveResult(saved.AffectedCnt);
    }

    private static void Validate(MenuSaveRow row, HashSet<string> known, string defaultLang)
    {
        var type = row.MenuTypeCd ?? "";
        var program = Blank(row.ProgramId);
        var link = Blank(row.LinkUrl);
        var parent = Blank(row.ParentMenuKey);
        var ok = Types.Contains(type, StringComparer.Ordinal)
            && Scopes.Contains(row.OpenScopeCd, StringComparer.Ordinal)
            && row.NewWinYn is "Y" or "N"
            && row.UseYn is "Y" or "N"
            && (parent is null || (IsKey(parent) && parent != row.MenuKey))
            && type switch
            {
                "FOLDER" => program is null && link is null,
                "LINK" => program is null && IsSafeLink(link),
                _ => program is not null && link is null,
            }
            && row.Names.Keys.All(known.Contains)
            && !string.IsNullOrWhiteSpace(row.Names.GetValueOrDefault(defaultLang))
            && row.Names.Values.All(v => v is null || v.Trim().Length <= 100);
        if (!ok)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }
    }

    /// <summary>링크는 같은 사이트 경로(<c>/…</c>)나 https 주소만 쓴다 (javascript:·상대 스킴 주소 차단).</summary>
    internal static bool IsSafeLink(string? link) =>
        link is { Length: <= 500 }
        && !link.Any(c => char.IsWhiteSpace(c) || c is '<' or '>' or '"' or '\'' or '\\')
        && ((link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal))
            || link.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    private static bool IsKey(string? value) =>
        value is { Length: > 0 and <= 50 } && value.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-');

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class TotalRow
    {
        public int TotalCnt { get; set; }
    }
}
