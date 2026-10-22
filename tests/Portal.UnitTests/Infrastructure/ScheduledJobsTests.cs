using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Portal.Core.Scheduling;
using Portal.Data;
using Portal.Infrastructure.Files;
using Portal.Infrastructure.Localization;
using Portal.Infrastructure.Messaging;
using Portal.Testing;

namespace Portal.UnitTests.Infrastructure;

public sealed class ScheduledJobsTests
{
    private static readonly JobRunContext Context = new("JOB_X", "SCHEDULE", null);
    private readonly FakeSpExecutor _sp = new();

    private MailSendJob MailJob(FakePortalConfig config)
    {
        _sp.Returns(LangResources.ListSp, new List<LangResources.ResourceRow>());
        var langs = new LangResources(new ServiceCollection().AddSingleton<ISpExecutor>(_sp).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
        var sender = new MailQueueSender(_sp, new NoTransport(), new NoProtector(), config, langs, NullLogger<MailQueueSender>.Instance);
        return new MailSendJob(sender);
    }

    [Fact]
    public async Task 메일발송_가득찬묶음이면이어보내고_최대묶음에서멈춘다()
    {
        var full = Enumerable.Range(1, MailQueueSender.BatchSize).Select(i => new MailQueueSender.QueuedMail { MsgId = i, LangCd = "ko" }).ToList();
        _sp.Returns(MailQueueSender.ClaimSp, full);

        var result = await MailJob(new FakePortalConfig().Set("MAIL.ENABLED", "Y")).RunAsync(Context, CancellationToken.None);

        Assert.Equal(MailSendJob.MaxBatches, _sp.Calls.Count(c => c.SpName == MailQueueSender.ClaimSp));
        Assert.Equal(MailSendJob.MaxBatches * MailQueueSender.BatchSize, result.ProcessedCount);
        Assert.False(result.Skipped);
    }

    [Fact]
    public async Task 메일발송_꺼져있으면_대기열을건드리지않고SKIPPED()
    {
        var result = await MailJob(new FakePortalConfig()).RunAsync(Context, CancellationToken.None);

        Assert.True(result.Skipped);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == MailQueueSender.ClaimSp);
    }

    [Fact]
    public async Task 첨부정리_행을지운파일만저장소에서지운다()
    {
        _sp.Returns(FileCleanupJob.ListSp, new List<FileCleanupJob.CleanupFile> { new() { FileId = 1, StoredPath = "a" }, new() { FileId = 2, StoredPath = "b" }, new() { FileId = 3, StoredPath = "c" } });
        _sp.Returns(FileCleanupJob.DeleteSp, p => new SaveResult((long)p["P_FILE_ID"]! == 2 ? 0 : 1, null)); // 2는 그 사이 확정됨
        var storage = new RecordingStorage { FailOn = "c" };

        var result = await new FileCleanupJob(_sp, storage, NullLogger<FileCleanupJob>.Instance).RunAsync(Context, CancellationToken.None);

        Assert.Equal(["a", "c"], storage.Deleted);
        Assert.Equal(2, result.ProcessedCount);
        Assert.Contains("1건", result.Message, StringComparison.Ordinal);
        Assert.Single(_sp.Calls, c => c.SpName == FileCleanupJob.ListSp); // 한 묶음이 덜 찼으면 끝
    }

    [Fact]
    public async Task 첨부정리_목록을받고하나도못지우면_멈춘다()
    {
        var full = Enumerable.Range(1, FileCleanupJob.BatchSize).Select(i => new FileCleanupJob.CleanupFile { FileId = i, StoredPath = $"p{i}" }).ToList();
        _sp.Returns(FileCleanupJob.ListSp, full);
        _sp.Returns(FileCleanupJob.DeleteSp, new SaveResult(0, null));

        var result = await new FileCleanupJob(_sp, new RecordingStorage(), NullLogger<FileCleanupJob>.Instance).RunAsync(Context, CancellationToken.None);

        Assert.Equal(0, result.ProcessedCount);
        Assert.Single(_sp.Calls, c => c.SpName == FileCleanupJob.ListSp);
    }

    private sealed class RecordingStorage : IFileStorage
    {
        public List<string> Deleted { get; } = [];

        public string? FailOn { get; init; }

        public Task WriteAsync(string storedPath, Stream content, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Stream OpenRead(string storedPath) => Stream.Null;

        public void Delete(string storedPath)
        {
            Deleted.Add(storedPath);
            if (storedPath == FailOn)
            {
                throw new IOException("잠김");
            }
        }
    }

    private sealed class NoTransport : IMailTransport
    {
        public Task SendAsync(MailMessageData message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoProtector : Portal.Core.Security.IPersonalDataProtector
    {
        public byte[] Protect(string plainText) => [];

        public string Unprotect(byte[] protectedData) => "";
    }
}
