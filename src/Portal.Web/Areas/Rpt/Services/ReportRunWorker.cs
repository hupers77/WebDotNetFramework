using Microsoft.Extensions.Options;

namespace Portal.Web.Areas.Rpt.Services;

/// <summary>백그라운드 생성 서비스 설정 (<c>Portal:ReportRuns</c>).</summary>
public sealed class ReportRunOptions
{
    /// <summary>설정 구역 이름.</summary>
    public const string SectionName = "Portal:ReportRuns";

    /// <summary>끄면 요청이 대기열에 쌓이기만 한다 (점검 중).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>할 일이 없을 때 다음 확인까지 기다리는 시간(초).</summary>
    public int PollSeconds { get; set; } = 5;

    /// <summary>만료된 요청·파일을 정리하는 간격(분).</summary>
    public int CleanupMinutes { get; set; } = 30;
}

/// <summary>
/// 레포트 백그라운드 생성 서비스: 웹 서버 안에서 대기열을 보며 요청을 하나씩 만든다(한 번에 하나, 서버 부담을 한정).
/// 한 번의 실패(DB 연결 등)로 멈추지 않고 다음 간격에 다시 한다. 서버를 내리면 만들던 요청은 그대로 두고 다음에 '멈춘 요청'으로 정리된다.
/// </summary>
public sealed partial class ReportRunWorker(ReportRunProcessor processor, IOptions<ReportRunOptions> options, TimeProvider time, ILogger<ReportRunWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var poll = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds));
        var cleanupEvery = TimeSpan.FromMinutes(Math.Max(1, options.Value.CleanupMinutes));
        var nextCleanup = time.GetUtcNow();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var worked = await RunOnceAsync(stoppingToken);
                if (time.GetUtcNow() >= nextCleanup)
                {
                    await CleanupOnceAsync(stoppingToken);
                    nextCleanup = time.GetUtcNow() + cleanupEvery;
                }

                if (!worked)
                {
                    await Task.Delay(poll, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 서비스 중지
        }
    }

    /// <summary>요청 하나를 만든다. 오류는 기록만 한다. 만든 요청이 있으면 true.</summary>
    internal async Task<bool> RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            return await processor.ProcessNextAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRunFailed(logger, ex);
            return false;
        }
    }

    private async Task CleanupOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await processor.CleanupAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCleanupFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "레포트 생성 대기열 처리 오류 (다음 간격에 다시 합니다)")]
    private static partial void LogRunFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "레포트 결과 정리 오류 (다음 간격에 다시 합니다)")]
    private static partial void LogCleanupFailed(ILogger logger, Exception ex);
}
