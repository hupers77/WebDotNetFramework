using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Portal.Core.Config;
using Portal.Core.Keys;
using Portal.Core.Security;
using Portal.Data;
using Portal.Infrastructure.Localization;

namespace Portal.Infrastructure.Messaging;

/// <summary>한 번 실행한 결과.</summary>
/// <param name="Enabled">메일 발송을 켰는지 (<c>MAIL.ENABLED</c>). 꺼져 있으면 대기열을 건드리지 않는다.</param>
/// <param name="Sent">보낸 수.</param>
/// <param name="Failed">실패한 수 (다시 보낼 것 포함).</param>
public sealed record MailSendSummary(bool Enabled, int Sent, int Failed);

/// <summary>
/// 메일 대기열 발송 (배치 작업 <c>JOB_MAIL_SEND</c>, Portal.Worker가 1분마다 부른다). 한 번에 <see cref="BatchSize"/>통까지 가져가
/// 받는 사람 언어의 템플릿으로 본문을 만들어 보낸다. 실패하면 <see cref="MaxTry"/>회까지 시간을 두고 다시 보낸다.
/// 개발계처럼 <c>MAIL.ENABLED = N</c>이면 보내지 않고 쌓아 둔다. 로그·오류 기록에는 받는 주소를 남기지 않는다.
/// </summary>
public sealed partial class MailQueueSender(
    ISpExecutor sp,
    IMailTransport transport,
    IPersonalDataProtector personalData,
    IPortalConfig config,
    LangResources langs,
    ILogger<MailQueueSender> logger)
{
    /// <summary>한 번에 가져가는 수.</summary>
    public const int BatchSize = 20;

    /// <summary>최대 시도 횟수.</summary>
    public const int MaxTry = 3;

    /// <summary>다시 보낼 간격(분) × 시도 횟수.</summary>
    public const int RetryMinutes = 5;

    /// <summary>작업이 멈췄다고 보고 다시 가져가기까지의 시간(분).</summary>
    public const int LockMinutes = 10;

    internal const string ClaimSp = "FW.USP_MSG_QUEUE_CLAIM_EXEC";
    internal const string ResultSp = "FW.USP_MSG_QUEUE_RESULT_EXEC";

    /// <summary>대기열에서 한 묶음을 보낸다.</summary>
    public async Task<MailSendSummary> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!config.GetBool(ConfigKeys.MailEnabled))
        {
            return new MailSendSummary(false, 0, 0);
        }

        var rows = await sp.QueryAsync<QueuedMail>(
            ClaimSp,
            new { P_BATCH_SIZE = BatchSize, P_LOCK_MINUTES = LockMinutes, P_MAX_TRY = MaxTry },
            cancellationToken);
        int sent = 0, failed = 0;
        foreach (var row in rows)
        {
            var error = await SendOneAsync(row, cancellationToken);
            if (error is null)
            {
                sent++;
            }
            else
            {
                failed++;
                LogSendFailed(logger, row.MsgId, row.TemplateId, row.TryCnt, error);
            }

            await sp.ExecuteAsync(
                ResultSp,
                new { P_MSG_ID = row.MsgId, P_SENT_YN = error is null ? "Y" : "N", P_ERROR_MSG = error, P_MAX_TRY = MaxTry, P_RETRY_MINUTES = RetryMinutes },
                cancellationToken);
        }

        return new MailSendSummary(true, sent, failed);
    }

    /// <summary>한 통을 보낸다. 성공하면 null, 실패하면 주소가 들어가지 않은 오류 사유.</summary>
    private async Task<string?> SendOneAsync(QueuedMail row, CancellationToken cancellationToken)
    {
        string to;
        try
        {
            to = row.ToEmailEnc is null ? "" : personalData.Unprotect(row.ToEmailEnc);
        }
        catch (CryptographicException)
        {
            return "ADDRESS_UNREADABLE";
        }

        if (!MailAddress.TryCreate(to, out _))
        {
            return "ADDRESS_INVALID";
        }

        var values = Values(row);
        try
        {
            await transport.SendAsync(
                new MailMessageData(to, MessageRenderer.RenderSubject(row.Subject, values), MessageRenderer.RenderBody(row.Body, values)),
                cancellationToken);
            return null;
        }
        catch (MailNotConfiguredException ex)
        {
            return ex.Message;
        }
        catch (SmtpException ex)
        {
            // SmtpFailedRecipientException 메시지에는 주소가 들어가므로 상태 코드만 남긴다
            return $"SMTP_{ex.StatusCode}";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            return ex.GetType().Name;
        }
    }

    /// <summary>치환값: 업무 SP가 넘긴 값 + 사이트명(받는 사람 언어) + 포털 주소.</summary>
    private Dictionary<string, string?> Values(QueuedMail row)
    {
        var values = MessageRenderer.ParseValues(row.ParamsJson);
        var siteName = langs.Get(row.LangCd, ConfigKeys.SiteName, []);
        values["SITE_NM"] = siteName != ConfigKeys.SiteName ? siteName : config.GetString(ConfigKeys.SiteName) ?? "";
        values["LINK"] = config.GetString(ConfigKeys.SiteBaseUrl) is { } baseUrl ? baseUrl.TrimEnd('/') + "/" : "";
        return values;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "메일 {MsgId}({TemplateId}) 발송 실패 ({TryCnt}회째): {Error}")]
    private static partial void LogSendFailed(ILogger logger, long msgId, string templateId, int tryCnt, string error);

    /// <summary><c>USP_MSG_QUEUE_CLAIM_EXEC</c> 결과 행.</summary>
    internal sealed class QueuedMail
    {
        public long MsgId { get; set; }

        public string TemplateId { get; set; } = "";

        public byte[]? ToEmailEnc { get; set; }

        public string LangCd { get; set; } = "";

        public string? ParamsJson { get; set; }

        public int TryCnt { get; set; }

        public string Subject { get; set; } = "";

        public string Body { get; set; } = "";
    }
}
