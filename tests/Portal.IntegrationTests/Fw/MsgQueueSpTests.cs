using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Infrastructure.Localization;
using Portal.Infrastructure.Messaging;
using Portal.IntegrationTests.Infrastructure;
using Portal.Security.Secrets;
using Portal.Testing;

namespace Portal.IntegrationTests.Fw;

/// <summary>메일 대기열 SP: 받는 사람 주소·언어 결정, 쓰지 않는 템플릿·주소 없음은 건너뜀, 치환값 형식.</summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class MsgQueueSpTests(DbFixture fixture)
{
    private const string SaveSp = "FW.USP_MSG_QUEUE_SAVE";
    private const string Template = "TPL_PARTNER_APPROVED";
    private static readonly byte[] Email = [1, 2, 3, 4];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<string> RowAsync(TestDb db, string? msgId) =>
        db.ScalarAsync<string>(
            "SELECT CONCAT(TEMPLATE_ID, '|', CHANNEL_CD, '|', ISNULL(TO_USER_ID, '-'), '|', LANG_CD, '|', STAT_CD, '|', TRY_CNT, '|', REG_ID, '|', CONVERT(VARCHAR(20), TO_EMAIL_ENC, 2))"
            + " FROM FW.TB_MSG_QUEUE WHERE MSG_ID = @M",
            ("@M", long.Parse(msgId!, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public async Task 사용자에게_사용자주소와언어로()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.mail", "HQ", "EMP");
        await db.ExecAsync("UPDATE FW.TB_USER SET EMAIL_ENC = 0x0A0B, LANG_CD = 'en' WHERE USER_ID = 'it.mail'");

        var result = await db.Sp.ExecuteAsync(SaveSp, new { P_TEMPLATE_ID = Template, P_TO_USER_ID = "it.mail", P_PARAMS_JSON = """{"USER_NM":"홍"}""" }, Ct);

        Assert.Equal(1, result.AffectedCnt);
        Assert.Equal($"{Template}|MAIL|it.mail|en|READY|0|{db.User.UserId}|0A0B", await RowAsync(db, result.NewKey));
    }

    [Fact]
    public async Task 계정없는사람_주소직접_쓰지않는언어는기본언어로()
    {
        await using var db = await fixture.BeginAsync();

        var result = await db.Sp.ExecuteAsync(SaveSp, new { P_TEMPLATE_ID = Template, P_TO_EMAIL_ENC = Email, P_TO_LANG_CD = "fr" }, Ct);

        Assert.Equal($"{Template}|MAIL|-|ko|READY|0|{db.User.UserId}|01020304", await RowAsync(db, result.NewKey));
    }

    [Fact]
    public async Task 주소가없거나_쓰지않는템플릿이면_쌓지않는다()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.nomail", "HQ", "EMP");
        var before = await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_MSG_QUEUE");

        var noEmail = await db.Sp.ExecuteAsync(SaveSp, new { P_TEMPLATE_ID = Template, P_TO_USER_ID = "it.nomail" }, Ct);
        var unknown = await db.Sp.ExecuteAsync(SaveSp, new { P_TEMPLATE_ID = "TPL_NONE", P_TO_EMAIL_ENC = Email }, Ct);
        await db.ExecAsync("UPDATE FW.TB_MSG_TEMPLATE SET USE_YN = 'N' WHERE TEMPLATE_ID = @T", ("@T", Template));
        var unused = await db.Sp.ExecuteAsync(SaveSp, new { P_TEMPLATE_ID = Template, P_TO_EMAIL_ENC = Email }, Ct);

        Assert.Equal((0, (string?)null), (noEmail.AffectedCnt, noEmail.NewKey));
        Assert.Equal(0, unknown.AffectedCnt);
        Assert.Equal(0, unused.AffectedCnt);
        Assert.Equal(before, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_MSG_QUEUE"));
    }

    [Fact]
    public async Task 초기템플릿_3개언어_치환변수형식()
    {
        await using var db = await fixture.BeginAsync();

        var rows = await db.ColumnAsync<string>(
            "SELECT CONCAT(T.TEMPLATE_ID, '|', L.LANG_CD) FROM FW.TB_MSG_TEMPLATE T JOIN FW.TB_MSG_TEMPLATE_LANG L ON L.TEMPLATE_ID = T.TEMPLATE_ID"
            + " WHERE T.TEMPLATE_ID IN ('TPL_PARTNER_APPROVED', 'TPL_PARTNER_REJECTED') ORDER BY 1");

        Assert.Equal(6, rows.Count);
        Assert.Equal(0, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM FW.TB_MSG_TEMPLATE_LANG WHERE SUBJECT LIKE '%' + CHAR(10) + '%' OR BODY NOT LIKE '%{{USER_NM}}%'"));
    }

    [Fact]
    public async Task 치환값이JSON이아니면_오류()
    {
        await using var db = await fixture.BeginAsync();

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(SaveSp, new { P_TEMPLATE_ID = Template, P_TO_EMAIL_ENC = Email, P_PARAMS_JSON = "{bad" }, Ct));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }
}

/// <summary>메일 발송 SP와 발송기: 가져가기(잠금·재시도·멈춘 작업), 결과 기록(성공·재시도·최종 실패 시 주소 삭제), 실제 템플릿으로 본문 만들기.</summary>
/// <remarks>다른 테스트가 남긴 대기 행과 섞이지 않게 각 테스트는 대기열을 비우고 시작한다(트랜잭션이라 끝나면 되돌아간다).</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class MsgSendSpTests(DbFixture fixture)
{
    private const string ClaimSp = "FW.USP_MSG_QUEUE_CLAIM_EXEC";
    private const string ResultSp = "FW.USP_MSG_QUEUE_RESULT_EXEC";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class Claimed
    {
        public long MsgId { get; set; }

        public string LangCd { get; set; } = "";

        public int TryCnt { get; set; }

        public string Subject { get; set; } = "";

        public string Body { get; set; } = "";
    }

    private static async Task<TestDb> EmptyQueueAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.ExecAsync("DELETE FROM FW.TB_MSG_QUEUE");
        return db;
    }

    private static async Task<long> EnqueueAsync(TestDb db, string lang = "ko", string template = "TPL_PARTNER_APPROVED")
    {
        await db.ExecAsync(
            "INSERT INTO FW.TB_MSG_QUEUE (TEMPLATE_ID, TO_EMAIL_ENC, LANG_CD, PARAMS_JSON) VALUES (@T, 0x01, @L, N'{\"USER_NM\":\"홍\"}')",
            ("@T", template), ("@L", lang));
        return await db.ScalarAsync<long>("SELECT MAX(MSG_ID) FROM FW.TB_MSG_QUEUE");
    }

    private static Task<string> StateAsync(TestDb db, long id) =>
        db.ScalarAsync<string>(
            "SELECT CONCAT(STAT_CD, '|', TRY_CNT, '|', IIF(TO_EMAIL_ENC IS NULL, 'noaddr', 'addr'), '|', IIF(PARAMS_JSON IS NULL, 'noparams', 'params'), '|', ISNULL(ERROR_MSG, '-'))"
            + " FROM FW.TB_MSG_QUEUE WHERE MSG_ID = @M", ("@M", id));

    private static Task<IReadOnlyList<Claimed>> ClaimAsync(TestDb db, int batch = 20) =>
        db.Sp.QueryAsync<Claimed>(ClaimSp, new { P_BATCH_SIZE = batch, P_LOCK_MINUTES = 10, P_MAX_TRY = 3 }, Ct);

    [Fact]
    public async Task 가져가면_SENDING_시도횟수증가_언어별문구_없는언어는기본언어()
    {
        await using var db = await EmptyQueueAsync(fixture);
        var en = await EnqueueAsync(db, "en");
        await db.ExecAsync("DELETE FROM FW.TB_MSG_TEMPLATE_LANG WHERE TEMPLATE_ID = 'TPL_PARTNER_APPROVED' AND LANG_CD = 'zh-CN'");
        var zh = await EnqueueAsync(db, "zh-CN");

        var rows = await ClaimAsync(db);

        Assert.Equal([en, zh], rows.Select(r => r.MsgId));
        Assert.StartsWith("[{{SITE_NM}}] Your partner account", rows[0].Subject, StringComparison.Ordinal);
        Assert.Contains("승인되었습니다", rows[1].Subject, StringComparison.Ordinal);
        Assert.Equal("SENDING|1|addr|params|-", await StateAsync(db, en));
        Assert.Empty(await ClaimAsync(db)); // 이미 가져간 행은 다시 가져가지 않는다
    }

    [Fact]
    public async Task 묶음크기만큼만_다음시도시각전은가져가지않는다()
    {
        await using var db = await EmptyQueueAsync(fixture);
        await EnqueueAsync(db);
        await EnqueueAsync(db);
        var later = await EnqueueAsync(db);
        await db.ExecAsync("UPDATE FW.TB_MSG_QUEUE SET NEXT_TRY_DT = DATEADD(MINUTE, 5, SYSUTCDATETIME()) WHERE MSG_ID = @M", ("@M", later));

        Assert.Single(await ClaimAsync(db, batch: 1));
        Assert.Single(await ClaimAsync(db));
        Assert.Empty(await ClaimAsync(db));
    }

    [Fact]
    public async Task 성공하면_주소와치환값을지운다()
    {
        await using var db = await EmptyQueueAsync(fixture);
        var id = await EnqueueAsync(db);
        await ClaimAsync(db);

        var result = await db.Sp.ExecuteAsync(ResultSp, new { P_MSG_ID = id, P_SENT_YN = "Y" }, Ct);

        Assert.Equal(1, result.AffectedCnt);
        Assert.Equal("SENT|1|noaddr|noparams|-", await StateAsync(db, id));
        Assert.Equal(0, (await db.Sp.ExecuteAsync(ResultSp, new { P_MSG_ID = id, P_SENT_YN = "N" }, Ct)).AffectedCnt); // SENDING이 아니면 바꾸지 않는다
    }

    [Fact]
    public async Task 실패는_간격을두고재시도_최대횟수면FAILED와주소삭제()
    {
        await using var db = await EmptyQueueAsync(fixture);
        var id = await EnqueueAsync(db);

        for (var i = 1; i <= 3; i++)
        {
            await db.ExecAsync("UPDATE FW.TB_MSG_QUEUE SET NEXT_TRY_DT = SYSUTCDATETIME() WHERE MSG_ID = @M", ("@M", id));
            Assert.Single(await ClaimAsync(db));
            await db.Sp.ExecuteAsync(ResultSp, new { P_MSG_ID = id, P_SENT_YN = "N", P_ERROR_MSG = $"SMTP_{i}", P_MAX_TRY = 3, P_RETRY_MINUTES = 5 }, Ct);
            if (i == 1)
            {
                Assert.Equal("READY|1|addr|params|SMTP_1", await StateAsync(db, id));
                Assert.InRange(await db.ScalarAsync<int>("SELECT DATEDIFF(SECOND, SYSUTCDATETIME(), NEXT_TRY_DT) FROM FW.TB_MSG_QUEUE WHERE MSG_ID = @M", ("@M", id)), 290, 300);
            }
        }

        Assert.Equal("FAILED|3|noaddr|noparams|SMTP_3", await StateAsync(db, id));
    }

    [Fact]
    public async Task 멈춘작업의행은_다시가져가고_마지막시도였으면FAILED()
    {
        await using var db = await EmptyQueueAsync(fixture);
        var again = await EnqueueAsync(db);
        var last = await EnqueueAsync(db);
        await db.ExecAsync("UPDATE FW.TB_MSG_QUEUE SET STAT_CD = 'SENDING', LOCK_DT = DATEADD(MINUTE, -11, SYSUTCDATETIME()), TRY_CNT = 1 WHERE MSG_ID = @M", ("@M", again));
        await db.ExecAsync("UPDATE FW.TB_MSG_QUEUE SET STAT_CD = 'SENDING', LOCK_DT = DATEADD(MINUTE, -11, SYSUTCDATETIME()), TRY_CNT = 3 WHERE MSG_ID = @M", ("@M", last));

        var rows = await ClaimAsync(db);

        Assert.Equal([again], rows.Select(r => r.MsgId));
        Assert.Equal(2, rows[0].TryCnt);
        Assert.Equal("FAILED|3|noaddr|noparams|SEND_INTERRUPTED", await StateAsync(db, last));
    }

    [Fact]
    public async Task 템플릿을쓰지않게되면_보내지않고FAILED()
    {
        await using var db = await EmptyQueueAsync(fixture);
        var id = await EnqueueAsync(db);
        await db.ExecAsync("UPDATE FW.TB_MSG_TEMPLATE SET MAIL_YN = 'N' WHERE TEMPLATE_ID = 'TPL_PARTNER_APPROVED'");

        Assert.Empty(await ClaimAsync(db));
        Assert.Equal("FAILED|1|noaddr|noparams|TEMPLATE_UNAVAILABLE", await StateAsync(db, id));
    }

    [Fact]
    public async Task 발송기_실제템플릿과사이트명으로본문을만들어보내고_SENT()
    {
        await using var db = await EmptyQueueAsync(fixture);
        var protector = new DataProtectionPersonalDataProtector(new EphemeralDataProtectionProvider());
        await db.Sp.ExecuteAsync(
            "FW.USP_MSG_QUEUE_SAVE",
            new { P_TEMPLATE_ID = "TPL_PARTNER_REJECTED", P_TO_EMAIL_ENC = protector.Protect("kim@partner.example.com"), P_TO_LANG_CD = "ko", P_PARAMS_JSON = """{"USER_NM":"김<협력>","USER_ID":"it.kim","REJECT_REASON":"소속 확인 불가"}""" },
            Ct);
        var langs = new LangResources(new ServiceCollection().AddSingleton(db.Sp).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
        await langs.ReloadAsync(Ct);
        var transport = new CapturingTransport();
        var config = new FakePortalConfig().Set("MAIL.ENABLED", "Y").Set("SITE.BASE_URL", "https://portal.example.com");

        var summary = await new MailQueueSender(db.Sp, transport, protector, config, langs, NullLogger<MailQueueSender>.Instance).RunOnceAsync(Ct);

        Assert.Equal(new MailSendSummary(true, 1, 0), summary);
        var mail = Assert.Single(transport.Sent);
        Assert.Equal("kim@partner.example.com", mail.To);
        Assert.Equal("[엔터프라이즈 포털] 협력사 계정 신청이 반려되었습니다", mail.Subject);
        Assert.Contains("김&lt;협력&gt;님", mail.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("사유: 소속 확인 불가", mail.HtmlBody, StringComparison.Ordinal);
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_MSG_QUEUE WHERE STAT_CD <> 'SENT' OR TO_EMAIL_ENC IS NOT NULL"));
    }

    private sealed class CapturingTransport : IMailTransport
    {
        public List<MailMessageData> Sent { get; } = [];

        public Task SendAsync(MailMessageData message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
