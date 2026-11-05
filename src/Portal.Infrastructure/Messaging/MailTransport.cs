using System.Net;
using System.Net.Mail;
using System.Text;
using Portal.Core.Config;
using Portal.Core.Keys;
using Portal.Core.Security;

namespace Portal.Infrastructure.Messaging;

/// <summary>보낼 메일 한 통.</summary>
/// <param name="To">받는 주소 (한 명).</param>
/// <param name="Subject">제목.</param>
/// <param name="HtmlBody">본문 (HTML).</param>
/// <param name="FromAddress">보내는 주소. 없으면 설정 <c>MAIL.FROM</c>.</param>
/// <param name="FromName">보내는 사람 표시 이름. 없으면 설정 <c>MAIL.FROM_NAME</c>, 그것도 없으면 주소만.</param>
/// <param name="ReplyTo">회신 주소 (선택).</param>
public sealed record MailMessageData(string To, string Subject, string HtmlBody, string? FromAddress = null, string? FromName = null, string? ReplyTo = null);

/// <summary>메일 전송. 실패하면 예외를 던진다(호출하는 쪽이 재시도를 정한다).</summary>
public interface IMailTransport
{
    /// <summary>한 통을 보낸다.</summary>
    Task SendAsync(MailMessageData message, CancellationToken cancellationToken = default);
}

/// <summary>설정(<c>MAIL.*</c>)이 비어 있어 보낼 수 없을 때.</summary>
public sealed class MailNotConfiguredException(string key) : InvalidOperationException($"메일 설정 {key}이(가) 비어 있습니다.");

/// <summary>
/// SMTP 전송 (.NET 기본 <see cref="SmtpClient"/>, 외부 패키지 없음). 서버·포트·암호화·계정은 설정 관리 화면 값(<c>MAIL.*</c>)을 매번 읽는다.
/// 비밀번호는 설정 비밀값(SECRET)으로 암호화되어 있어 보낼 때만 푼다.
/// </summary>
internal sealed class SmtpMailTransport(IPortalConfig config, ISecretProtector secrets) : IMailTransport
{
    /// <inheritdoc />
    public async Task SendAsync(MailMessageData message, CancellationToken cancellationToken = default)
    {
        var host = config.GetString(ConfigKeys.MailSmtpHost) ?? throw new MailNotConfiguredException(ConfigKeys.MailSmtpHost);
        var from = message.FromAddress ?? config.GetString(ConfigKeys.MailFrom) ?? throw new MailNotConfiguredException(ConfigKeys.MailFrom);
        var fromName = message.FromName ?? config.GetString(ConfigKeys.MailFromName);

        using var client = new SmtpClient(host, config.GetInt(ConfigKeys.MailSmtpPort))
        {
            EnableSsl = config.GetBool(ConfigKeys.MailSmtpSsl),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30_000,
        };
        if (config.GetString(ConfigKeys.MailSmtpUser) is { } user)
        {
            var password = config.GetString(ConfigKeys.MailSmtpPassword) is { } encrypted ? secrets.Unprotect(encrypted) : "";
            client.Credentials = new NetworkCredential(user, password);
        }

        using var mail = new MailMessage
        {
            From = fromName is { Length: > 0 } ? new MailAddress(from, fromName, Encoding.UTF8) : new MailAddress(from),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
        };
        mail.To.Add(message.To);
        if (message.ReplyTo is { Length: > 0 } replyTo)
        {
            mail.ReplyToList.Add(replyTo);
        }

        await client.SendMailAsync(mail, cancellationToken);
    }
}
