using System.Text.RegularExpressions;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Localization;
using Portal.Core.Results;
using Portal.Data;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Localization;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>공통코드 관리 (<c>SYS_CODE_MGT</c>). 그룹 목록, 그룹별 코드 목록(언어별 명칭 포함), 그리드 일괄 저장.</summary>
public interface ICodeAdminService
{
    /// <summary>코드 그룹 목록. 그룹 ID 앞부분 또는 그룹명 일부로 찾는다.</summary>
    Task<PageResult<CodeGroupDto>> GetGroupsAsync(string? keyword, CancellationToken cancellationToken = default);

    /// <summary>그룹의 코드 목록 (사용 안 함 포함, 정렬 순서).</summary>
    Task<CodeListDto> GetCodesAsync(string groupCd, CancellationToken cancellationToken = default);

    /// <summary>그리드 변경 행을 한 번에 저장한다. 행 상태별 권한은 Controller가 먼저 확인한다.</summary>
    Task<SaveResult> SaveAsync(CodeSaveRequest request, CancellationToken cancellationToken = default);

    /// <summary>그룹의 코드를 화면 그리드와 같은 컬럼으로 엑셀에 쓴다. 그룹이 없으면 업무 오류(NOT_FOUND).</summary>
    Task<Stream> ExportAsync(string groupCd, CancellationToken cancellationToken = default);

    /// <summary>
    /// 엑셀 업로드 미리보기: 내려받은 양식과 같은 머리글의 파일을 읽어 추가(I)·수정(U) 행과 행별 오류를 돌려준다.
    /// 저장하지 않으며, 화면이 행을 그리드에 반영한 뒤 사용자가 저장한다(저장은 <see cref="SaveAsync"/>가 다시 검사).
    /// </summary>
    Task<CodeUploadResult> PreviewUploadAsync(string groupCd, Stream content, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed partial class CodeAdminService(ISpExecutor sp, LangResources langs, IPortalLocalizer localizer) : ICodeAdminService
{
    /// <summary>한 번에 읽고 저장하는 최대 행 수 (목록 SP의 페이지 크기 상한과 같다).</summary>
    public const int MaxRows = 500;

    internal const string GroupListSp = "FW.USP_CODE_GROUP_GET_LIST";
    internal const string ListSp = "FW.USP_CODE_GET_LIST";
    internal const string NameListSp = "FW.USP_CODE_LANG_GET_LIST";
    internal const string SaveSp = "FW.USP_CODE_SAVE";

    private const int CodeMaxLength = 30;
    private const int TextMaxLength = 200;

    /// <inheritdoc />
    public Task<PageResult<CodeGroupDto>> GetGroupsAsync(string? keyword, CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<CodeGroupDto>(
            GroupListSp,
            new { P_KEYWORD = NullIfBlank(keyword), P_PAGE_NO = 1, P_PAGE_SIZE = MaxRows },
            cancellationToken);

    /// <inheritdoc />
    public async Task<CodeListDto> GetCodesAsync(string groupCd, CancellationToken cancellationToken = default)
    {
        var codes = await sp.QueryPageAsync<CodeDto>(ListSp, new { P_GROUP_CD = groupCd, P_PAGE_NO = 1, P_PAGE_SIZE = MaxRows }, cancellationToken);
        var names = (await sp.QueryAsync<NameRow>(NameListSp, new { P_GROUP_CD = groupCd }, cancellationToken)).ToLookup(n => n.Code, StringComparer.Ordinal);
        foreach (var code in codes.Items)
        {
            foreach (var name in names[code.Code])
            {
                code.Names[name.LangCd] = name.CodeNm;
            }
        }

        return new CodeListDto { Codes = codes };
    }

    /// <inheritdoc />
    public async Task<SaveResult> SaveAsync(CodeSaveRequest request, CancellationToken cancellationToken = default)
    {
        var (rows, names) = Validate(request);
        return await sp.ExecuteAsync(
            SaveSp,
            new
            {
                P_GROUP_CD = request.GroupCd,
                P_ROWS = Tvp.From("FW.TVP_CODE", rows),
                P_NAMES = Tvp.From("FW.TVP_CODE_LANG", names),
            },
            cancellationToken);
    }

    /// <summary>
    /// 화면 검증과 같은 규칙을 서버에서 다시 확인하고 TVP 행으로 바꾼다. 시스템 그룹·중복·동시성은 SP가 확인한다.
    /// </summary>
    internal (List<CodeTvpRow> Rows, List<NameTvpRow> Names) Validate(CodeSaveRequest request)
    {
        if (request.Rows.Count == 0)
        {
            throw new BizException(MessageKeys.NoChanges);
        }

        var languages = langs.Languages.Select(l => l.LangCd).ToHashSet(StringComparer.Ordinal);
        var defaultLang = langs.Languages.FirstOrDefault(l => l.IsDefault)?.LangCd ?? LangResources.DefaultLang;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<CodeTvpRow>(request.Rows.Count);
        var names = new List<NameTvpRow>();

        foreach (var row in request.Rows)
        {
            var code = row.Code.Trim();
            if (code.Length == 0)
            {
                throw new BizException(ValRequired, localizer["LBL.CODE"]);
            }

            if (row.RowStat is not (RowStates.Inserted or RowStates.Updated or RowStates.Deleted)
                || (row.RowStat != RowStates.Inserted && row.RowVer is not { Length: 8 }))
            {
                throw new BizException(MessageKeys.InvalidInput);
            }

            if (row.RowStat == RowStates.Inserted && !CodePattern().IsMatch(code))
            {
                throw new BizException(ValCodeFormat);
            }

            if (!seen.Add(code))
            {
                throw new BizException(ValDupRow, code);
            }

            if (row.RowStat != RowStates.Deleted)
            {
                CheckValues(row);
                foreach (var (langCd, name) in row.Names)
                {
                    if (!languages.Contains(langCd))
                    {
                        throw new BizException(MessageKeys.InvalidInput);
                    }

                    var text = name?.Trim();
                    if (text?.Length > TextMaxLength)
                    {
                        throw new BizException(ValMaxLength, localizer["LBL.CODE_NM_IN", langCd], TextMaxLength);
                    }

                    names.Add(new NameTvpRow { Code = code, LangCd = langCd, CodeNm = text });
                }

                var hasDefault = row.Names.TryGetValue(defaultLang, out var defaultName) && !string.IsNullOrWhiteSpace(defaultName);
                if (!hasDefault && (row.RowStat == RowStates.Inserted || row.Names.ContainsKey(defaultLang)))
                {
                    throw new BizException(ValRequired, localizer["LBL.CODE_NM_IN", defaultLang]);
                }
            }

            rows.Add(new CodeTvpRow
            {
                RowStat = row.RowStat,
                Code = code,
                ParentCode = NullIfBlank(row.ParentCode),
                SortNo = row.SortNo,
                Attr1 = NullIfBlank(row.Attr1),
                Attr2 = NullIfBlank(row.Attr2),
                Attr3 = NullIfBlank(row.Attr3),
                Attr4 = NullIfBlank(row.Attr4),
                Attr5 = NullIfBlank(row.Attr5),
                StartDt = row.StartDt,
                EndDt = row.EndDt,
                UseYn = row.UseYn,
                RowVer = row.RowVer,
            });
        }

        return (rows, names);
    }

    private const string ValRequired = "VAL.REQUIRED";
    private const string ValMaxLength = "VAL.MAX_LENGTH";
    private const string ValCodeFormat = "VAL.CODE_FORMAT";
    private const string ValDupRow = "VAL.DUP_ROW";
    private const string ValDateRange = "VAL.DATE_RANGE";

    private void CheckValues(CodeSaveRow row)
    {
        if (row.ParentCode?.Trim().Length > CodeMaxLength)
        {
            throw new BizException(ValMaxLength, localizer["LBL.PARENT_CODE"], CodeMaxLength);
        }

        string?[] attrs = [row.Attr1, row.Attr2, row.Attr3, row.Attr4, row.Attr5];
        for (var i = 0; i < attrs.Length; i++)
        {
            if (attrs[i]?.Trim().Length > TextMaxLength)
            {
                throw new BizException(ValMaxLength, localizer["LBL.ATTR", i + 1], TextMaxLength);
            }
        }

        if (row.StartDt is { } start && row.EndDt is { } end && end < start)
        {
            throw new BizException(ValDateRange);
        }

        if (row.UseYn is not (null or "Y" or "N"))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,30}$")]
    private static partial Regex CodePattern();

    /// <summary><c>FW.TVP_CODE</c> 컬럼 순서와 같은 순서로 선언한다. 명칭은 <see cref="NameTvpRow"/>로 보내므로 비워 둔다.</summary>
    internal sealed class CodeTvpRow
    {
        public string RowStat { get; set; } = "";

        public string Code { get; set; } = "";

        public string? CodeNm { get; set; }

        public string? ParentCode { get; set; }

        public int? SortNo { get; set; }

        public string? Attr1 { get; set; }

        public string? Attr2 { get; set; }

        public string? Attr3 { get; set; }

        public string? Attr4 { get; set; }

        public string? Attr5 { get; set; }

        public DateOnly? StartDt { get; set; }

        public DateOnly? EndDt { get; set; }

        public string? UseYn { get; set; }

        public byte[]? RowVer { get; set; }
    }

    /// <summary><c>FW.TVP_CODE_LANG</c> 컬럼 순서.</summary>
    internal sealed class NameTvpRow
    {
        public string Code { get; set; } = "";

        public string LangCd { get; set; } = "";

        public string? CodeNm { get; set; }
    }

    internal sealed class NameRow
    {
        public string Code { get; set; } = "";

        public string LangCd { get; set; } = "";

        public string CodeNm { get; set; } = "";
    }
}
