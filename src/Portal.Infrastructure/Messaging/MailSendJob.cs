using Portal.Core.Scheduling;

namespace Portal.Infrastructure.Messaging;

/// <summary>
/// 배치 작업 <c>JOB_MAIL_SEND</c>(1분마다): 메일 대기열을 묶음씩 보낸다. 묶음이 가득 차면 바로 다음 묶음을 보내되,
/// 한 번에 <see cref="MaxBatches"/>묶음까지만 보내고 나머지는 다음 실행에 넘긴다. <c>MAIL.ENABLED = N</c>이면 건너뛴다(SKIPPED).
/// </summary>
public sealed class MailSendJob(MailQueueSender sender) : IScheduledJob
{
    /// <summary>한 번 실행에 보내는 최대 묶음 수.</summary>
    internal const int MaxBatches = 10;

    /// <inheritdoc />
    public string Name => "MailSend";

    /// <inheritdoc />
    public async Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        int sent = 0, failed = 0;
        for (var i = 0; i < MaxBatches; i++)
        {
            var summary = await sender.RunOnceAsync(cancellationToken);
            if (!summary.Enabled)
            {
                return JobRunResult.Skip("MAIL.ENABLED = N");
            }

            (sent, failed) = (sent + summary.Sent, failed + summary.Failed);
            if (summary.Sent + summary.Failed < MailQueueSender.BatchSize)
            {
                break;
            }
        }

        return JobRunResult.Done(sent + failed, failed > 0 ? $"성공 {sent}, 실패 {failed}" : null);
    }
}
