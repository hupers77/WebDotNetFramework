using System.Text.Json;
using Portal.Core.Config;
using Portal.Core.Context;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Data;
using Portal.Infrastructure.Localization;

namespace Portal.Infrastructure.Messaging;

/// <summary>포털 알림 한 건 (내 정보 > 알림함, 헤더 배지).</summary>
public sealed class NotificationItem
{
    /// <summary>알림 번호.</summary>
    public long NotiId { get; init; }

    /// <summary>제목 (템플릿 제목을 지금 화면 언어로 치환, 앞의 [사이트명]은 뺀다).</summary>
    public string Title { get; init; } = "";

    /// <summary>내용 (템플릿 본문을 글자만 남겨 치환). 화면에는 글자로만 넣는다.</summary>
    public string Content { get; init; } = "";

    /// <summary>읽었으면 Y.</summary>
    public string ReadYn { get; init; } = "N";

    /// <summary>받은 시각 (UTC).</summary>
    public DateTime RegDt { get; init; }

    /// <summary>누르면 여는 화면의 프로그램 ID. 없으면 null.</summary>
    public string? LinkProgramId { get; init; }

    /// <summary>누르면 여는 화면 경로 (<c>/Sys/Job</c>). 없으면 null.</summary>
    public string? LinkUrl { get; init; }
}

/// <summary>
/// 포털 알림함 (설계서 13장 알림센터). 알림은 업무 SP가 <c>FW.USP_MSG_QUEUE_SAVE</c>로 메일과 함께 쌓고(템플릿 <c>PORTAL_YN = 'Y'</c>),
/// 여기서는 현재 사용자 자신의 알림만 읽고 읽음 표시·삭제한다. 문구는 볼 때 지금 화면 언어의 템플릿으로 만든다.
/// </summary>
public sealed class NotificationService(ISpExecutor sp, ICurrentUser currentUser, IPortalConfig config, LangResources langs)
{
    /// <summary>한 쪽에 보이는 건수.</summary>
    public const int PageSize = 20;

    /// <summary>한 번에 지울 수 있는 최대 건수.</summary>
    public const int MaxDelete = 100;

    internal const string ListSp = "FW.USP_NOTI_GET_LIST";
    internal const string UnreadSp = "FW.USP_NOTI_UNREAD_GET";
    internal const string ReadSp = "FW.USP_NOTI_READ_EXEC";
    internal const string DeleteSp = "FW.USP_NOTI_DEL";

    /// <summary>내 알림 목록 (최근 것부터).</summary>
    public async Task<PageResult<NotificationItem>> GetListAsync(bool unreadOnly, int pageNo, CancellationToken cancellationToken = default)
    {
        var page = await sp.QueryPageAsync<NotificationRow>(
            ListSp,
            new { P_UNREAD_YN = unreadOnly ? "Y" : "N", P_PAGE_NO = Math.Max(1, pageNo), P_PAGE_SIZE = PageSize },
            cancellationToken);
        var siteName = SiteName();
        return new PageResult<NotificationItem>
        {
            Items = [.. page.Items.Select(r => ToItem(r, siteName))],
            TotalCount = page.TotalCount,
            PageNo = page.PageNo,
            PageSize = page.PageSize,
        };
    }

    /// <summary>안 읽은 알림 수 (100까지만 센다).</summary>
    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default) =>
        (await sp.QuerySingleAsync<UnreadRow>(UnreadSp, null, cancellationToken))?.UnreadCnt ?? 0;

    /// <summary>읽음 표시. <paramref name="notiId"/>가 null이면 안 읽은 알림 전부. 바꾼 수를 돌려준다.</summary>
    public async Task<int> MarkReadAsync(long? notiId, CancellationToken cancellationToken = default) =>
        (await sp.ExecuteAsync(ReadSp, new { P_NOTI_ID = notiId }, cancellationToken)).AffectedCnt;

    /// <summary>알림을 지운다(최대 <see cref="MaxDelete"/>건). 지운 수를 돌려준다.</summary>
    public async Task<int> DeleteAsync(IReadOnlyCollection<long> notiIds, CancellationToken cancellationToken = default) =>
        (await sp.ExecuteAsync(DeleteSp, new { P_NOTI_IDS = JsonSerializer.Serialize(notiIds) }, cancellationToken)).AffectedCnt;

    /// <summary>템플릿 문구를 치환해 화면용 한 건으로 만든다. 링크(<c>{{LINK}}</c>)는 화면 열기 버튼이 대신하므로 비운다.</summary>
    internal static NotificationItem ToItem(NotificationRow row, string siteName)
    {
        Dictionary<string, string?> values;
        try
        {
            values = MessageRenderer.ParseValues(row.ParamsJson);
        }
        catch (JsonException)
        {
            values = new(StringComparer.Ordinal);
        }

        values["SITE_NM"] = siteName;
        values["LINK"] = "";
        var title = MessageRenderer.RenderSubject(row.Subject, values);
        var prefix = $"[{siteName}]";
        if (title.StartsWith(prefix, StringComparison.Ordinal))
        {
            title = title[prefix.Length..].TrimStart();
        }

        return new NotificationItem
        {
            NotiId = row.NotiId,
            Title = title,
            Content = MessageRenderer.ToPlainText(MessageRenderer.RenderBody(row.Body, values)),
            ReadYn = row.ReadYn,
            RegDt = row.RegDt,
            LinkProgramId = row.LinkProgramId,
            LinkUrl = row.LinkUrl,
        };
    }

    private string SiteName()
    {
        var siteName = langs.Get(currentUser.LangCd, ConfigKeys.SiteName, []);
        return siteName != ConfigKeys.SiteName ? siteName : config.GetString(ConfigKeys.SiteName) ?? "";
    }

    /// <summary><c>USP_NOTI_GET_LIST</c> 결과 행.</summary>
    internal sealed class NotificationRow
    {
        public long NotiId { get; set; }

        public string TemplateId { get; set; } = "";

        public string? ParamsJson { get; set; }

        public DateTime RegDt { get; set; }

        public string ReadYn { get; set; } = "N";

        public string Subject { get; set; } = "";

        public string Body { get; set; } = "";

        public string? LinkProgramId { get; set; }

        public string? LinkUrl { get; set; }
    }

    /// <summary><c>USP_NOTI_UNREAD_GET</c> 결과 행.</summary>
    internal sealed class UnreadRow
    {
        public int UnreadCnt { get; set; }
    }
}
