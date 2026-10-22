using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Infrastructure.Localization;
using Portal.Infrastructure.Messaging;
using Portal.IntegrationTests.Infrastructure;
using Portal.Testing;

namespace Portal.IntegrationTests.Fw;

/// <summary>
/// 포털 알림 SP: 메시지 저장 SP의 포털 채널, 내 알림만 보기·읽음·삭제, 화면 언어 문구와 여는 화면, 기능 권한자 목록, 보관 기간 정리.
/// </summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class NotificationSpTests(DbFixture fixture)
{
    private const string QueueSp = "FW.USP_MSG_QUEUE_SAVE";
    private const string PortalTemplate = "TPL_JOB_FAILED";
    private const string MailOnlyTemplate = "TPL_PARTNER_APPROVED";
    private const string Params = """{"JOB_NM":"메일 발송","JOB_ID":"JOB_MAIL_SEND","RESULT_MSG":"SMTP_<5>"}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NotificationService NewService(TestDb db) =>
        new(db.Sp, db.User, new FakePortalConfig(), new LangResources(new ServiceCollection().AddSingleton(db.Sp).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>()));

    /// <summary>사이트명 문구(SITE.NAME)까지 읽은 서비스.</summary>
    private static async Task<NotificationService> NewLoadedServiceAsync(TestDb db)
    {
        var langs = new LangResources(new ServiceCollection().AddSingleton(db.Sp).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
        await langs.ReloadAsync(Ct);
        return new(db.Sp, db.User, new FakePortalConfig(), langs);
    }

    private static Task<int> CountAsync(TestDb db, string userId) =>
        db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_NOTIFICATION WHERE USER_ID = @U", ("@U", userId));

    private static Task<long> AddAsync(TestDb db, string userId, string templateId = PortalTemplate, string? paramsJson = Params, int daysAgo = 0) =>
        db.ScalarAsync<long>(
            "INSERT INTO FW.TB_NOTIFICATION (USER_ID, TEMPLATE_ID, PARAMS_JSON, REG_DT) OUTPUT INSERTED.NOTI_ID VALUES (@U, @T, @P, DATEADD(DAY, -@D, SYSUTCDATETIME()))",
            ("@U", userId), ("@T", templateId), ("@P", paramsJson), ("@D", daysAgo));

    [Fact]
    public async Task 포털채널템플릿_사용자에게메일과알림을함께쌓는다_메일만인템플릿은알림없음()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.noti", "HQ", "EMP");
        await db.ExecAsync("UPDATE FW.TB_USER SET EMAIL_ENC = 0x0A0B WHERE USER_ID = 'it.noti'");

        var both = await db.Sp.ExecuteAsync(QueueSp, new { P_TEMPLATE_ID = PortalTemplate, P_TO_USER_ID = "it.noti", P_PARAMS_JSON = Params }, Ct);
        var mailOnly = await db.Sp.ExecuteAsync(QueueSp, new { P_TEMPLATE_ID = MailOnlyTemplate, P_TO_USER_ID = "it.noti" }, Ct);

        Assert.Equal(2, both.AffectedCnt);
        Assert.NotNull(both.NewKey);
        Assert.Equal(1, mailOnly.AffectedCnt);
        Assert.Equal($"{PortalTemplate}|{Params}|unread|{db.User.UserId}", await db.ScalarAsync<string>(
            "SELECT CONCAT(TEMPLATE_ID, '|', PARAMS_JSON, '|', IIF(READ_DT IS NULL, 'unread', 'read'), '|', REG_ID) FROM FW.TB_NOTIFICATION WHERE USER_ID = 'it.noti'"));
    }

    [Fact]
    public async Task 주소가없어도_포털알림은쌓는다_사용중이아닌사용자와포털채널끈템플릿은쌓지않는다()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.noti.nomail", "HQ", "EMP");
        await db.SeedUserAsync("it.noti.retired", "HQ", "EMP");
        await db.ExecAsync("UPDATE FW.TB_USER SET USER_STAT_CD = 'RETIRED' WHERE USER_ID = 'it.noti.retired'");

        var noMail = await db.Sp.ExecuteAsync(QueueSp, new { P_TEMPLATE_ID = PortalTemplate, P_TO_USER_ID = "it.noti.nomail" }, Ct);
        var retired = await db.Sp.ExecuteAsync(QueueSp, new { P_TEMPLATE_ID = PortalTemplate, P_TO_USER_ID = "it.noti.retired" }, Ct);
        await db.ExecAsync("UPDATE FW.TB_MSG_TEMPLATE SET PORTAL_YN = 'N' WHERE TEMPLATE_ID = @T", ("@T", PortalTemplate));
        var off = await db.Sp.ExecuteAsync(QueueSp, new { P_TEMPLATE_ID = PortalTemplate, P_TO_USER_ID = "it.noti.nomail" }, Ct);

        Assert.Equal((1, (string?)null), (noMail.AffectedCnt, noMail.NewKey));
        Assert.Equal(0, retired.AffectedCnt);
        Assert.Equal(0, off.AffectedCnt);
        Assert.Equal(1, await CountAsync(db, "it.noti.nomail"));
        Assert.Equal(0, await CountAsync(db, "it.noti.retired"));
    }

    [Fact]
    public async Task 목록_내알림만_최근것부터_화면언어문구와여는화면()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.noti.me", "HQ", "EMP");
        await db.SeedUserAsync("it.noti.other", "HQ", "EMP");
        var older = await AddAsync(db, "it.noti.me", MailOnlyTemplate, """{"USER_NM":"홍","USER_ID":"it.noti.me"}""", daysAgo: 1);
        var newer = await AddAsync(db, "it.noti.me");
        await AddAsync(db, "it.noti.other");
        db.AsUser("it.noti.me", "HQ", "EMP");
        db.User.LangCd = "en";

        var page = await (await NewLoadedServiceAsync(db)).GetListAsync(unreadOnly: false, pageNo: 1, Ct);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal([newer, older], page.Items.Select(i => i.NotiId));
        var job = page.Items[0];
        Assert.Equal(("Scheduled job failed: 메일 발송", "N", "SYS_JOB_MGT", "/Sys/Job"), (job.Title, job.ReadYn, job.LinkProgramId, job.LinkUrl));
        Assert.StartsWith("The scheduled job 메일 발송 (JOB_MAIL_SEND) failed.\nReason: SMTP_<5>\n", job.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("<", job.Content.Replace("SMTP_<5>", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Null(page.Items[1].LinkUrl);
    }

    [Fact]
    public async Task 읽음_하나와전체_내것만바뀌고_안읽은수와안읽은목록이맞다()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.noti.me", "HQ", "EMP");
        await db.SeedUserAsync("it.noti.other", "HQ", "EMP");
        var first = await AddAsync(db, "it.noti.me");
        await AddAsync(db, "it.noti.me");
        await AddAsync(db, "it.noti.me");
        var others = await AddAsync(db, "it.noti.other");
        db.AsUser("it.noti.me", "HQ", "EMP");
        var service = NewService(db);

        Assert.Equal(3, await service.GetUnreadCountAsync(Ct));
        Assert.Equal(1, await service.MarkReadAsync(first, Ct));
        Assert.Equal(0, await service.MarkReadAsync(others, Ct));
        Assert.Equal(2, await service.GetUnreadCountAsync(Ct));
        Assert.Equal(2, (await service.GetListAsync(unreadOnly: true, pageNo: 1, Ct)).TotalCount);
        Assert.Equal(2, await service.MarkReadAsync(null, Ct));

        Assert.Equal(0, await service.GetUnreadCountAsync(Ct));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_NOTIFICATION WHERE USER_ID = 'it.noti.other' AND READ_DT IS NULL"));
    }

    [Fact]
    public async Task 안읽은수는_100까지만센다()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.noti.many", "HQ", "EMP");
        await db.ExecAsync(
            "INSERT INTO FW.TB_NOTIFICATION (USER_ID, TEMPLATE_ID) SELECT TOP (120) 'it.noti.many', @T FROM sys.all_objects",
            ("@T", PortalTemplate));
        db.AsUser("it.noti.many", "HQ", "EMP");

        Assert.Equal(100, await NewService(db).GetUnreadCountAsync(Ct));
    }

    [Fact]
    public async Task 삭제_내것만지운다_형식이틀리면입력오류()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.noti.me", "HQ", "EMP");
        await db.SeedUserAsync("it.noti.other", "HQ", "EMP");
        var mine = await AddAsync(db, "it.noti.me");
        var keep = await AddAsync(db, "it.noti.me");
        var others = await AddAsync(db, "it.noti.other");
        db.AsUser("it.noti.me", "HQ", "EMP");
        var service = NewService(db);

        Assert.Equal(1, await service.DeleteAsync([mine, others], Ct));
        Assert.Equal([keep], await db.ColumnAsync<long>("SELECT NOTI_ID FROM FW.TB_NOTIFICATION WHERE USER_ID = 'it.noti.me'"));
        Assert.Equal(1, await CountAsync(db, "it.noti.other"));

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(NotificationService.DeleteSp, new { P_NOTI_IDS = "{\"id\":1}" }, Ct));
        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }

    [Fact]
    public async Task 기능권한자_역할허용_GRANT_SYS_ADMIN은포함_DENY_역할없음_잠김은제외()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.fu.role", "HQ", "EMP", null, "PORTAL_ADMIN");
        await db.SeedUserAsync("it.fu.grant", "HQ", "EMP", null, "EMP_USER");
        await db.SeedUserAsync("it.fu.sys", "HQ", "EMP", null, "SYS_ADMIN");
        await db.SeedUserAsync("it.fu.deny", "HQ", "EMP", null, "PORTAL_ADMIN");
        await db.SeedUserAsync("it.fu.norole", "HQ", "EMP");
        await db.SeedUserAsync("it.fu.lock", "HQ", "EMP", null, "PORTAL_ADMIN");
        await db.SeedUserAsync("it.fu.plain", "HQ", "EMP", null, "EMP_USER");
        await db.ExecAsync("UPDATE FW.TB_USER SET LOCK_YN = 'Y' WHERE USER_ID = 'it.fu.lock'");
        await db.ExecAsync("INSERT INTO FW.TB_USER_PROGRAM_FUNC (USER_ID, PROGRAM_ID, FUNC_CD, GRANT_TYPE) VALUES ('it.fu.grant', 'SYS_PARTNER_APPROVE', 'APPROVE', 'GRANT'), ('it.fu.norole', 'SYS_PARTNER_APPROVE', 'APPROVE', 'GRANT'), ('it.fu.deny', 'SYS_PARTNER_APPROVE', 'APPROVE', 'DENY')");

        var users = await db.ColumnAsync<string>(
            "SELECT USER_ID FROM FW.UFN_FUNC_USERS('SYS_PARTNER_APPROVE', 'APPROVE') WHERE USER_ID LIKE 'it.fu.%' ORDER BY USER_ID");

        Assert.Equal(["it.fu.grant", "it.fu.role", "it.fu.sys"], users);
    }

    [Fact]
    public async Task 정리작업_보관기간지난알림을지운다()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.noti.old", "HQ", "EMP");
        var old = await AddAsync(db, "it.noti.old", daysAgo: 91);
        var recent = await AddAsync(db, "it.noti.old", daysAgo: 89);

        await db.Sp.QuerySingleAsync<Portal.Infrastructure.Scheduling.JobScheduler.SpJobRow>("FW.USP_JOB_DATA_CLEANUP_EXEC", null, Ct);

        Assert.Equal([recent], await db.ColumnAsync<long>("SELECT NOTI_ID FROM FW.TB_NOTIFICATION WHERE USER_ID = 'it.noti.old'"));
        Assert.NotEqual(old, recent);
    }
}
