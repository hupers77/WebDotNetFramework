using System.Text.Json;
using System.Text.RegularExpressions;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Infrastructure.Localization;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>메시지 템플릿 관리 (<c>SYS_MSG_TEMPLATE_MGT</c>). 템플릿 ID는 코드가 정하므로 목록 조회와 수정만 있다.</summary>
public interface IMsgTemplateAdminService
{
    /// <summary>템플릿 전체 (언어별 제목·본문 포함).</summary>
    Task<IReadOnlyList<MsgTemplateDto>> GetListAsync(MsgTemplateSearch search, CancellationToken cancellationToken = default);

    /// <summary>템플릿 하나를 수정한다.</summary>
    Task<MsgTemplateSaveResult> SaveAsync(MsgTemplateSaveRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed partial class MsgTemplateAdminService(ISpExecutor sp, LangResources langs) : IMsgTemplateAdminService
{
    internal const string ListSp = "FW.USP_MSG_TEMPLATE_ADMIN_GET_LIST";
    internal const string SaveSp = "FW.USP_MSG_TEMPLATE_SAVE";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public Task<IReadOnlyList<MsgTemplateDto>> GetListAsync(MsgTemplateSearch search, CancellationToken cancellationToken = default) =>
        sp.QueryMultipleAsync<IReadOnlyList<MsgTemplateDto>>(
            ListSp,
            new { P_KEYWORD = string.IsNullOrWhiteSpace(search.Keyword) ? null : search.Keyword.Trim() },
            async reader =>
            {
                var items = await reader.ReadAsync<MsgTemplateDto>(cancellationToken);
                var texts = (await reader.ReadAsync<MsgTemplateLangDto>(cancellationToken)).ToLookup(l => l.TemplateId, StringComparer.Ordinal);
                foreach (var item in items)
                {
                    item.Langs = [.. texts[item.TemplateId]];
                }

                return items;
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<MsgTemplateSaveResult> SaveAsync(MsgTemplateSaveRequest request, CancellationToken cancellationToken = default)
    {
        var known = langs.Languages.Select(l => l.LangCd).ToHashSet(StringComparer.Ordinal);
        var defaultLang = langs.Languages.FirstOrDefault(l => l.IsDefault)?.LangCd ?? LangResources.DefaultLang;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ok = request.RowVer is { Length: 8 }
            && request.MailYn is "Y" or "N" && request.PortalYn is "Y" or "N" && request.UseYn is "Y" or "N"
            && (request.MailYn == "Y" || request.PortalYn == "Y")
            && request.Langs.Count > 0
            && request.Langs.All(l => known.Contains(l.LangCd) && seen.Add(l.LangCd) && IsSafeSubject(l.Subject) && IsSafeBody(l.Body))
            && seen.Contains(defaultLang);
        if (!ok)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var saved = await sp.ExecuteAsync(
            SaveSp,
            new
            {
                P_TEMPLATE_ID = request.TemplateId,
                P_ROW_VER = request.RowVer,
                P_MAIL_YN = request.MailYn,
                P_PORTAL_YN = request.PortalYn,
                P_USE_YN = request.UseYn,
                P_LANGS_JSON = JsonSerializer.Serialize(request.Langs.Select(l => new { l.LangCd, Subject = l.Subject.Trim(), Body = l.Body.Trim() }), Json),
            },
            cancellationToken);
        return new MsgTemplateSaveResult(saved.AffectedCnt);
    }

    /// <summary>제목은 한 줄이어야 한다 (메일 헤더 주입 방지).</summary>
    internal static bool IsSafeSubject(string? subject) =>
        !string.IsNullOrWhiteSpace(subject) && subject.Trim().Length <= 200 && !subject.Any(c => c is '\r' or '\n' || char.IsControl(c));

    /// <summary>본문(HTML)에는 스크립트·프레임·이벤트 속성·script 계열 주소를 넣을 수 없다. SP도 같은 규칙으로 다시 거른다.</summary>
    internal static bool IsSafeBody(string? body) =>
        !string.IsNullOrWhiteSpace(body) && body.Trim().Length <= 4000 && !Forbidden().IsMatch(body);

    [GeneratedRegex(@"<\s*(script|iframe|object|embed)|javascript\s*:|vbscript\s*:|data\s*:\s*text/html|[\s""'/]on[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Forbidden();
}
