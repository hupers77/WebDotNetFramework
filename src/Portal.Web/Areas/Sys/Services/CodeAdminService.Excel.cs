using System.Globalization;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Excel;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>공통코드 엑셀 내보내기·업로드. 두 기능이 같은 컬럼 정의를 써서, 내려받은 파일이 그대로 업로드 양식이 된다.</summary>
public sealed partial class CodeAdminService
{
    private const string ValExcelFormat = "VAL.EXCEL_FORMAT";

    /// <inheritdoc />
    public async Task<Stream> ExportAsync(string groupCd, CancellationToken cancellationToken = default)
    {
        var group = await GroupAsync(groupCd, cancellationToken);
        var codes = (await GetCodesAsync(groupCd, cancellationToken)).Codes.Items;
        var columns = Columns(group).Select(c => new ExcelColumn<CodeDto>(c.Header, c.Get, c.Width)).ToList();
        return ExcelWriter.Write(new ExcelSheet<CodeDto>(group.GroupCd, columns, codes));
    }

    /// <inheritdoc />
    public async Task<CodeUploadResult> PreviewUploadAsync(string groupCd, Stream content, CancellationToken cancellationToken = default)
    {
        var group = await GroupAsync(groupCd, cancellationToken);
        var table = ExcelReader.Read(content, MaxRows);
        var columns = Columns(group);
        // 번역이 같아 머리글이 겹치면 앞 컬럼을 쓴다 (번역 누락으로 화면이 멈추지 않게)
        var byHeader = new Dictionary<string, CodeColumn>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            byHeader.TryAdd(column.Header, column);
        }

        var unknown = table.Headers.Where(h => h.Length > 0 && !byHeader.ContainsKey(h)).ToList();
        var codeIndex = IndexOf(table.Headers, columns[0].Header);
        if (unknown.Count > 0 || codeIndex < 0)
        {
            throw new BizException(ExcelMessageKeys.HeaderMismatch, unknown.Count > 0 ? string.Join(", ", unknown) : columns[0].Header);
        }

        var existing = (await GetCodesAsync(groupCd, cancellationToken)).Codes.Items.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
        var result = new CodeUploadResult();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in table.Rows)
        {
            var error = PreviewRow(group, table.Headers, byHeader, line, existing, seen, out var row);
            if (error is not null)
            {
                result.Errors.Add(new CodeUploadError(line.RowNo, error));
            }
            else if (row is null)
            {
                result.UnchangedCnt++;
            }
            else
            {
                result.Rows.Add(row);
            }
        }

        return result;
    }

    /// <summary>엑셀 한 행을 저장 행으로 바꾼다. 오류면 메시지, 바뀐 게 없으면 <paramref name="row"/>가 null.</summary>
    private string? PreviewRow(
        CodeGroupDto group, IReadOnlyList<string> headers, Dictionary<string, CodeColumn> byHeader, ExcelRow line,
        Dictionary<string, CodeDto> existing, HashSet<string> seen, out CodeSaveRow? row)
    {
        row = null;
        var code = line[IndexOf(headers, Columns(group)[0].Header)]?.Trim() ?? "";
        if (code.Length == 0)
        {
            return localizer[ValRequired, localizer["LBL.CODE"]];
        }

        if (!seen.Add(code))
        {
            return localizer[ValDupRow, code];
        }

        var current = existing.GetValueOrDefault(code);
        if (current is null && group.SystemYn == "Y" && group.AllowAddYn != "Y")
        {
            return localizer[MessageKeys.SystemCode];
        }

        var candidate = current is null
            ? new CodeSaveRow { RowStat = RowStates.Inserted, Code = code, UseYn = "Y" }
            : ToSaveRow(current);
        for (var i = 0; i < headers.Count; i++)
        {
            if (byHeader.TryGetValue(headers[i], out var column) && column.Set is { } set && !set(candidate, line[i]))
            {
                return localizer[ValExcelFormat, column.Header];
            }
        }

        try
        {
            Validate(new CodeSaveRequest { GroupCd = group.GroupCd, Rows = [candidate] });
        }
        catch (BizException ex)
        {
            return localizer[ex.MessageKey, [.. ex.Args]];
        }

        if (current is not null && SameAs(candidate, ToSaveRow(current)))
        {
            return null;
        }

        row = candidate;
        return null;
    }

    private async Task<CodeGroupDto> GroupAsync(string groupCd, CancellationToken cancellationToken) =>
        (await GetGroupsAsync(groupCd, cancellationToken)).Items.FirstOrDefault(g => g.GroupCd == groupCd)
        ?? throw new BizException(MessageKeys.NotFound);

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

    private static CodeSaveRow ToSaveRow(CodeDto c) => new()
    {
        RowStat = RowStates.Updated,
        Code = c.Code,
        Names = c.Names.ToDictionary(n => n.Key, n => (string?)n.Value, StringComparer.Ordinal),
        ParentCode = c.ParentCode,
        SortNo = c.SortNo,
        Attr1 = c.Attr1,
        Attr2 = c.Attr2,
        Attr3 = c.Attr3,
        Attr4 = c.Attr4,
        Attr5 = c.Attr5,
        StartDt = c.StartDt,
        EndDt = c.EndDt,
        UseYn = c.UseYn,
        RowVer = c.RowVer,
    };

    private static bool SameAs(CodeSaveRow a, CodeSaveRow b)
    {
        static string? N(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var langs = a.Names.Keys.Union(b.Names.Keys, StringComparer.Ordinal);
        return langs.All(l => N(a.Names.GetValueOrDefault(l)) == N(b.Names.GetValueOrDefault(l)))
            && N(a.ParentCode) == N(b.ParentCode) && a.SortNo == b.SortNo
            && N(a.Attr1) == N(b.Attr1) && N(a.Attr2) == N(b.Attr2) && N(a.Attr3) == N(b.Attr3)
            && N(a.Attr4) == N(b.Attr4) && N(a.Attr5) == N(b.Attr5)
            && a.StartDt == b.StartDt && a.EndDt == b.EndDt && a.UseYn == b.UseYn;
    }

    /// <summary>엑셀 컬럼: 머리글, 내보낼 값, 업로드 값 넣기(형식이 틀리면 false, 코드 컬럼은 없음).</summary>
    private sealed record CodeColumn(string Header, Func<CodeDto, object?> Get, Func<CodeSaveRow, string?, bool>? Set, double? Width = null);

    /// <summary>화면 그리드와 같은 컬럼 (언어별 명칭, 그룹에 의미가 정해진 속성만).</summary>
    private List<CodeColumn> Columns(CodeGroupDto group)
    {
        var columns = new List<CodeColumn> { new(localizer["LBL.CODE"], c => c.Code, null) };
        foreach (var lang in langs.Languages)
        {
            var langCd = lang.LangCd;
            columns.Add(new(localizer["LBL.CODE_NM_IN", lang.LangNm], c => c.Names.GetValueOrDefault(langCd), (r, v) => Assign(() => r.Names[langCd] = v), 30));
        }

        columns.Add(new(localizer["LBL.PARENT_CODE"], c => c.ParentCode, (r, v) => Assign(() => r.ParentCode = v)));
        columns.Add(new(localizer["LBL.SORT_NO"], c => c.SortNo, (r, v) =>
        {
            if (v is null)
            {
                r.SortNo = null;
                return true;
            }

            var ok = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n);
            r.SortNo = ok ? n : r.SortNo;
            return ok;
        }));

        string?[] attrNames = [group.Attr1Nm, group.Attr2Nm, group.Attr3Nm, group.Attr4Nm, group.Attr5Nm];
        Func<CodeDto, string?>[] gets = [c => c.Attr1, c => c.Attr2, c => c.Attr3, c => c.Attr4, c => c.Attr5];
        Action<CodeSaveRow, string?>[] sets = [(r, v) => r.Attr1 = v, (r, v) => r.Attr2 = v, (r, v) => r.Attr3 = v, (r, v) => r.Attr4 = v, (r, v) => r.Attr5 = v];
        for (var i = 0; i < attrNames.Length; i++)
        {
            // 화면과 같이 그룹에 의미가 정해진 속성만 쓴다
            if (attrNames[i] is { Length: > 0 } name)
            {
                var (get, set) = (gets[i], sets[i]);
                columns.Add(new(name, c => get(c), (r, v) => Assign(() => set(r, v))));
            }
        }

        columns.Add(new(localizer["LBL.START_DT"], c => c.StartDt, (r, v) => ParseDate(v, d => r.StartDt = d)));
        columns.Add(new(localizer["LBL.END_DT"], c => c.EndDt, (r, v) => ParseDate(v, d => r.EndDt = d)));
        columns.Add(new(localizer["LBL.USE_YN"], c => c.UseYn, (r, v) =>
        {
            // 비우면 지금 값(추가 행은 Y)을 둔다
            var yn = v?.Trim().ToUpperInvariant();
            r.UseYn = yn is "Y" or "N" ? yn : r.UseYn;
            return yn is null or "Y" or "N";
        }));
        return columns;
    }

    private static bool Assign(Action assign)
    {
        assign();
        return true;
    }

    /// <summary>yyyy-MM-dd 날짜. 비우면 기본값(SP가 정한다)으로 둔다.</summary>
    private static bool ParseDate(string? value, Action<DateOnly?> set)
    {
        if (value is null)
        {
            set(null);
            return true;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            set(date);
            return true;
        }

        return false;
    }
}
