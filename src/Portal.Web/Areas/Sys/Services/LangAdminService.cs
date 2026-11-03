using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Localization;
using Portal.Core.Results;
using Portal.Data;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Excel;
using Portal.Infrastructure.Localization;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>다국어 문구 관리 (<c>SYS_LANG_MGT</c>). 키 단위로 모든 언어의 문구를 한 행에 보여 주고 추가·수정·삭제·엑셀 내보내기·업로드를 한다.</summary>
public interface ILangAdminService
{
    /// <summary>문구 키 목록 (한 쪽).</summary>
    Task<PageResult<LangResourceDto>> GetListAsync(LangSearch search, CancellationToken cancellationToken = default);

    /// <summary>바뀐 행을 한 번에 저장한다.</summary>
    Task<LangSaveResult> SaveAsync(LangSaveRequest request, CancellationToken cancellationToken = default);

    /// <summary>검색 조건에 맞는 문구 전체(최대 <see cref="LangAdminService.MaxExportKeys"/>개 키)를 엑셀로 만든다.</summary>
    Task<Stream> ExportAsync(LangSearch search, CancellationToken cancellationToken = default);

    /// <summary>엑셀을 읽어 그리드에 반영할 행과 행별 오류를 돌려준다 (저장하지 않는다).</summary>
    Task<LangUploadResult> PreviewUploadAsync(Stream content, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class LangAdminService(ISpExecutor sp, LangResources langs, IPortalLocalizer localizer) : ILangAdminService
{
    /// <summary>한 쪽에 읽는 키 수.</summary>
    public const int PageSize = 50;

    /// <summary>엑셀로 내보내거나 업로드와 비교할 수 있는 키 수 상한.</summary>
    public const int MaxExportKeys = 5000;

    /// <summary>엑셀 업로드 한 번에 읽는 행 수 상한.</summary>
    public const int MaxUploadRows = 1000;

    internal const string ListSp = "FW.USP_LANG_RESOURCE_ADMIN_GET_LIST";
    internal const string SaveSp = "FW.USP_LANG_RESOURCE_SAVE";

    private const int ExportPageSize = 200;
    private const int TextMaxLength = 1000;
    private const string KeyHeader = "KEY";
    private const string TypeHeader = "TYPE";

    private static readonly string[] Types = ["LABEL", "MSG", "BTN", "TOOLTIP"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public Task<PageResult<LangResourceDto>> GetListAsync(LangSearch search, CancellationToken cancellationToken = default) =>
        ReadPageAsync(search, search.PageNo, PageSize, cancellationToken);

    /// <inheritdoc />
    public async Task<LangSaveResult> SaveAsync(LangSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Rows.Count == 0)
        {
            throw new BizException(MessageKeys.NoChanges);
        }

        var known = KnownLangs();
        var defaultLang = DefaultLang();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<object>(request.Rows.Count);
        foreach (var row in request.Rows)
        {
            if (row.RowStat is not (RowStates.Inserted or RowStates.Updated or RowStates.Deleted)
                || !IsKey(row.ResKey)
                || !seen.Add(row.ResKey)
                || (row.RowStat != RowStates.Inserted && row.RowVer is not { Length: 8 }))
            {
                throw new BizException(MessageKeys.InvalidInput);
            }

            var save = row.RowStat != RowStates.Deleted;
            if (save)
            {
                ValidateTexts(row, known, defaultLang);
            }

            rows.Add(new
            {
                row.RowStat,
                row.ResKey,
                row.ResType,
                RowVer = row.RowVer is null ? null : Convert.ToHexString(row.RowVer),
                Texts = save
                    ? row.Texts.Where(t => !string.IsNullOrWhiteSpace(t.Value)).Select(t => new { LangCd = t.Key, ResText = t.Value!.Trim() })
                    : [],
            });
        }

        var saved = await sp.ExecuteAsync(SaveSp, new { P_ROWS_JSON = JsonSerializer.Serialize(rows, Json) }, cancellationToken);
        return new LangSaveResult(saved.AffectedCnt);
    }

    /// <inheritdoc />
    public async Task<Stream> ExportAsync(LangSearch search, CancellationToken cancellationToken = default)
    {
        var items = await ReadAllAsync(search, cancellationToken);
        var columns = new List<ExcelColumn<LangResourceDto>>
        {
            new(KeyHeader, r => r.ResKey, 40),
            new(TypeHeader, r => r.ResType, 10),
        };
        columns.AddRange(langs.Languages.Select(l => new ExcelColumn<LangResourceDto>(l.LangCd, r => r.Texts.GetValueOrDefault(l.LangCd), 40)));
        return ExcelWriter.Write(new ExcelSheet<LangResourceDto>("LANG", columns, items));
    }

    /// <inheritdoc />
    public async Task<LangUploadResult> PreviewUploadAsync(Stream content, CancellationToken cancellationToken = default)
    {
        var table = ExcelReader.Read(content, MaxUploadRows);
        var known = KnownLangs();
        var defaultLang = DefaultLang();
        var keyIndex = IndexOf(table.Headers, KeyHeader);
        var typeIndex = IndexOf(table.Headers, TypeHeader);
        var langIndex = table.Headers.Select((h, i) => (Header: h, Index: i)).Where(x => known.Contains(x.Header)).ToList();
        var unknown = table.Headers.Where(h => h.Length > 0 && !known.Contains(h)
            && !string.Equals(h, KeyHeader, StringComparison.OrdinalIgnoreCase) && !string.Equals(h, TypeHeader, StringComparison.OrdinalIgnoreCase)).ToList();
        if (keyIndex < 0 || typeIndex < 0 || unknown.Count > 0 || langIndex.Count == 0)
        {
            throw new BizException(ExcelMessageKeys.HeaderMismatch, unknown.Count > 0 ? string.Join(", ", unknown) : KeyHeader);
        }

        var existing = (await ReadAllAsync(new LangSearch(), cancellationToken)).ToDictionary(r => r.ResKey, StringComparer.Ordinal);
        var result = new LangUploadResult();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in table.Rows)
        {
            var key = line[keyIndex]?.Trim() ?? "";
            var candidate = new LangSaveRow
            {
                ResKey = key,
                ResType = line[typeIndex]?.Trim().ToUpperInvariant(),
                Texts = langIndex.ToDictionary(x => x.Header, x => (string?)line[x.Index]?.Trim(), StringComparer.Ordinal),
            };
            if (existing.TryGetValue(key, out var current))
            {
                candidate.RowStat = RowStates.Updated;
                candidate.RowVer = current.RowVer;
                // 엑셀에 없는 언어 열은 현재 값을 그대로 둔다
                foreach (var (lang, text) in current.Texts.Where(t => !candidate.Texts.ContainsKey(t.Key)))
                {
                    candidate.Texts[lang] = text;
                }
            }
            else
            {
                candidate.RowStat = RowStates.Inserted;
            }

            try
            {
                if (!IsKey(key) || !seen.Add(key))
                {
                    throw new BizException(MessageKeys.InvalidInput);
                }

                ValidateTexts(candidate, known, defaultLang);
            }
            catch (BizException ex)
            {
                result.Errors.Add(new LangUploadError(line.RowNo, localizer[ex.MessageKey, [.. ex.Args]]));
                continue;
            }

            if (current is not null && SameAs(candidate, current))
            {
                result.UnchangedCnt++;
            }
            else
            {
                result.Rows.Add(candidate);
            }
        }

        return result;
    }

    /// <summary>문구 키 형식: 대문자·숫자·밑줄을 점으로 이은 형식 (예: <c>SYS.USER.TITLE</c>).</summary>
    internal static bool IsKey(string? value) =>
        value is { Length: >= 3 and <= 100 }
        && value.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '.')
        && value.Contains('.', StringComparison.Ordinal)
        && !value.StartsWith('.') && !value.EndsWith('.') && !value.Contains("..", StringComparison.Ordinal);

    private static bool SameAs(LangSaveRow row, LangResourceDto current) =>
        row.ResType == current.ResType
        && row.Texts.Where(t => !string.IsNullOrWhiteSpace(t.Value)).ToDictionary(t => t.Key, t => t.Value!.Trim(), StringComparer.Ordinal)
            .OrderBy(t => t.Key, StringComparer.Ordinal).SequenceEqual(current.Texts.OrderBy(t => t.Key, StringComparer.Ordinal));

    private static void ValidateTexts(LangSaveRow row, HashSet<string> known, string defaultLang)
    {
        var ok = Types.Contains(row.ResType ?? "", StringComparer.Ordinal)
            && row.Texts.Keys.All(known.Contains)
            && !string.IsNullOrWhiteSpace(row.Texts.GetValueOrDefault(defaultLang))
            && row.Texts.Values.All(v => v is null || v.Trim().Length <= TextMaxLength);
        if (!ok)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }
    }

    private HashSet<string> KnownLangs() => langs.Languages.Select(l => l.LangCd).ToHashSet(StringComparer.Ordinal);

    private string DefaultLang() => langs.Languages.FirstOrDefault(l => l.IsDefault)?.LangCd ?? LangResources.DefaultLang;

    private async Task<List<LangResourceDto>> ReadAllAsync(LangSearch search, CancellationToken cancellationToken)
    {
        var all = new List<LangResourceDto>();
        for (var page = 1; ; page++)
        {
            var result = await ReadPageAsync(search, page, ExportPageSize, cancellationToken);
            if (result.TotalCount > MaxExportKeys)
            {
                throw new BizException(ExcelMessageKeys.TooManyRows, MaxExportKeys);
            }

            all.AddRange(result.Items);
            if (all.Count >= result.TotalCount || result.Items.Count == 0)
            {
                return all;
            }
        }
    }

    private Task<PageResult<LangResourceDto>> ReadPageAsync(LangSearch search, int pageNo, int pageSize, CancellationToken cancellationToken) =>
        sp.QueryMultipleAsync(
            ListSp,
            new
            {
                P_KEYWORD = string.IsNullOrWhiteSpace(search.Keyword) ? null : search.Keyword.Trim(),
                P_RES_TYPE = string.IsNullOrEmpty(search.ResType) ? null : search.ResType,
                P_MISSING_YN = search.MissingYn == "Y" ? "Y" : null,
                P_PAGE_NO = pageNo,
                P_PAGE_SIZE = pageSize,
            },
            async reader =>
            {
                var rows = await reader.ReadAsync<LangResourceRow>(cancellationToken);
                var total = await reader.ReadSingleAsync<TotalRow>(cancellationToken);
                var items = rows.GroupBy(r => r.ResKey, StringComparer.Ordinal).Select(g => new LangResourceDto
                {
                    ResKey = g.Key,
                    ResType = g.First().ResType,
                    RowVer = g.First().KeyVer,
                    Texts = g.ToDictionary(r => r.LangCd, r => r.ResText, StringComparer.Ordinal),
                }).ToList();
                return new PageResult<LangResourceDto> { Items = items, TotalCount = total?.TotalCnt ?? items.Count, PageNo = pageNo, PageSize = pageSize };
            },
            cancellationToken);

    private static int IndexOf(IReadOnlyList<string> headers, string header)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i], header, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class TotalRow
    {
        public int TotalCnt { get; set; }
    }
}
