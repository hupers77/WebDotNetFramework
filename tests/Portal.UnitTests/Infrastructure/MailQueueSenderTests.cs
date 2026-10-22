using System.Net.Mail;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Portal.Data;
using Portal.Infrastructure.Localization;
using Portal.Infrastructure.Messaging;
using Portal.Security.Secrets;
using Portal.Testing;

namespace Portal.UnitTests.Infrastructure;

public sealed class MailQueueSenderTests
{
    private readonly FakeSpExecutor _sp = new();
    private readonly FakePortalConfig _config = new FakePortalConfig()
        .Set("MAIL.ENABLED", "Y").Set("SITE.BASE_URL", "https://portal.example.com").Set("SITE.NAME", "설정 사이트명");
    private readonly DataProtectionPersonalDataProtector _protector = new(new EphemeralDataProtectionProvider());
    private readonly FakeTransport _transport = new();
    private readonly LangResources _langs;

    public MailQueueSenderTests()
    {
        _sp.Returns(LangResources.ListSp, new List<LangResources.ResourceRow>
        {
            new() { LangCd = "ko", ResKey = "SITE.NAME", ResText = "포털" },
            new() { LangCd = "en", ResKey = "SITE.NAME", ResText = "Portal" },
        });
        _sp.Returns(LangResources.LanguageSp, new List<LangResources.LanguageRow> { new() { LangCd = "ko", LangNm = "한국어", DefaultYn = "Y" } });
        _langs = new LangResources(new ServiceCollection().AddSingleton<ISpExecutor>(_sp).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
        _langs.ReloadAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private MailQueueSender Sender => new(_sp, _transport, _protector, _config, _langs, NullLogger<MailQueueSender>.Instance);

    private MailQueueSender.QueuedMail Row(long id, string? email = "kim@partner.example.com", string lang = "ko", string? json = """{"USER_NM":"<김>","CNT":3}""") => new()
    {
        MsgId = id,
        TemplateId = "TPL_T",
        ToEmailEnc = email is null ? null : _protector.Protect(email),
        LangCd = lang,
        ParamsJson = json,
        TryCnt = 1,
        Subject = "[{{SITE_NM}}] {{USER_NM}}",
        Body = "<p>{{USER_NM}} {{CNT}} <a href=\"{{LINK}}\">x</a></p>",
    };

    private List<(long Id, string Sent, string? Error)> Results() =>
        [.. _sp.Calls.Where(c => c.SpName == MailQueueSender.ResultSp)
            .Select(c => ((long)c.Parameters["P_MSG_ID"]!, (string)c.Parameters["P_SENT_YN"]!, (string?)c.Parameters["P_ERROR_MSG"]))];

    [Fact]
    public async Task 꺼져있으면_대기열을건드리지않는다()
    {
        _config.Set("MAIL.ENABLED", "N");

        var summary = await Sender.RunOnceAsync();

        Assert.False(summary.Enabled);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == MailQueueSender.ClaimSp);
    }

    [Fact]
    public async Task 받는사람언어의사이트명과_치환값으로_보내고_성공기록()
    {
        _sp.Returns(MailQueueSender.ClaimSp, new List<MailQueueSender.QueuedMail> { Row(1), Row(2, lang: "en") });

        var summary = await Sender.RunOnceAsync();

        Assert.Equal(new MailSendSummary(true, 2, 0), summary);
        Assert.Equal(new MailMessageData("kim@partner.example.com", "[포털] <김>", "<p>&lt;김&gt; 3 <a href=\"https://portal.example.com/\">x</a></p>"), _transport.Sent[0]);
        Assert.Equal("[Portal] <김>", _transport.Sent[1].Subject);
        Assert.Equal([(1L, "Y", (string?)null), (2L, "Y", null)], Results());
        var claim = _sp.LastCall(MailQueueSender.ClaimSp).Parameters;
        Assert.Equal((MailQueueSender.BatchSize, MailQueueSender.MaxTry), ((int)claim["P_BATCH_SIZE"]!, (int)claim["P_MAX_TRY"]!));
    }

    [Fact]
    public async Task 주소를풀수없거나_형식이아니면_보내지않고실패기록()
    {
        var other = new DataProtectionPersonalDataProtector(new EphemeralDataProtectionProvider());
        var broken = Row(1);
        broken.ToEmailEnc = other.Protect("x@example.com");
        _sp.Returns(MailQueueSender.ClaimSp, new List<MailQueueSender.QueuedMail> { broken, Row(2, email: "not-an-address"), Row(3, email: null) });

        var summary = await Sender.RunOnceAsync();

        Assert.Equal(new MailSendSummary(true, 0, 3), summary);
        Assert.Empty(_transport.Sent);
        Assert.Equal([(1L, "N", "ADDRESS_UNREADABLE"), (2L, "N", "ADDRESS_INVALID"), (3L, "N", "ADDRESS_INVALID")], Results());
    }

    [Fact]
    public async Task SMTP오류_주소없이상태코드만기록_다음메일은계속()
    {
        _transport.Fail = new SmtpFailedRecipientException(SmtpStatusCode.MailboxUnavailable, "kim@partner.example.com");
        _sp.Returns(MailQueueSender.ClaimSp, new List<MailQueueSender.QueuedMail> { Row(1), Row(2) });

        var summary = await Sender.RunOnceAsync();

        Assert.Equal(2, summary.Failed);
        Assert.All(Results(), r => Assert.Equal("SMTP_MailboxUnavailable", r.Error));
        Assert.DoesNotContain(Results(), r => r.Error!.Contains('@', StringComparison.Ordinal));
    }

    [Fact]
    public async Task 메일설정이비어있으면_설정키를기록()
    {
        _transport.Fail = new MailNotConfiguredException("MAIL.SMTP_HOST");
        _sp.Returns(MailQueueSender.ClaimSp, new List<MailQueueSender.QueuedMail> { Row(1) });

        await Sender.RunOnceAsync();

        Assert.Contains("MAIL.SMTP_HOST", Results()[0].Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 사이트주소가없으면_링크는빈값_치환값이없어도보낸다()
    {
        var config = new FakePortalConfig().Set("MAIL.ENABLED", "Y");
        _sp.Returns(MailQueueSender.ClaimSp, new List<MailQueueSender.QueuedMail> { Row(1, json: null) });

        await new MailQueueSender(_sp, _transport, _protector, config, _langs, NullLogger<MailQueueSender>.Instance).RunOnceAsync();

        Assert.Equal("<p>  <a href=\"\">x</a></p>", _transport.Sent[0].HtmlBody);
    }

    private sealed class FakeTransport : IMailTransport
    {
        public List<MailMessageData> Sent { get; } = [];

        public Exception? Fail { get; set; }

        public Task SendAsync(MailMessageData message, CancellationToken cancellationToken = default)
        {
            if (Fail is not null)
            {
                throw Fail;
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
