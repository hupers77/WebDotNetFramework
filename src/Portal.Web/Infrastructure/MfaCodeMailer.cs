using System.Net.Mail;
using Portal.Core.Config;
using Portal.Core.Keys;
using Portal.Core.Security;
using Portal.Infrastructure.Localization;
using Portal.Infrastructure.Messaging;
using Portal.Security.Mfa;

namespace Portal.Web.Infrastructure;

/// <summary>
/// 2단계 인증 이메일 인증번호 발송. 번호를 메일 대기열(<c>TB_MSG_QUEUE</c>)에 남기지 않으려고 로그인 도중 바로 보낸다.
/// 제목·본문은 받는 사람 언어의 리소스 <c>MAIL.OTP_SUBJECT</c>({0} = 사이트명)·<c>MAIL.OTP_BODY</c>({0} = 번호)를 쓴다.
/// 메일 발송을 끈 환경(<c>MAIL.ENABLED = N</c>)에서는 보내지 않고 예외를 던져 화면에 실패로 보인다.
/// </summary>
internal sealed class MfaCodeMailer(IMailTransport transport, IPersonalDataProtector personalData, IPortalConfig config, LangResources langs) : IMfaCodeMailer
{
    /// <inheritdoc />
    public async Task SendAsync(byte[] toEmailEnc, string langCd, string code, CancellationToken cancellationToken = default)
    {
        if (!config.GetBool(ConfigKeys.MailEnabled))
        {
            throw new InvalidOperationException("메일 발송이 꺼져 있습니다 (MAIL.ENABLED).");
        }

        var to = personalData.Unprotect(toEmailEnc);
        if (!MailAddress.TryCreate(to, out _))
        {
            throw new FormatException("받는 주소가 올바르지 않습니다.");
        }

        var siteName = langs.Get(langCd, ConfigKeys.SiteName, []);
        if (siteName == ConfigKeys.SiteName)
        {
            siteName = config.GetString(ConfigKeys.SiteName) ?? "";
        }

        // 사이트명은 HTML이 아닌 제목에만 들어간다. 본문에는 숫자로만 된 번호만 넣는다
        await transport.SendAsync(
            new MailMessageData(to, langs.Get(langCd, "MAIL.OTP_SUBJECT", [siteName]), langs.Get(langCd, "MAIL.OTP_BODY", [code])),
            cancellationToken);
    }
}
