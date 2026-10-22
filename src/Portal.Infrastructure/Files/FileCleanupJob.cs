using Microsoft.Extensions.Logging;
using Portal.Core.Scheduling;
using Portal.Data;

namespace Portal.Infrastructure.Files;

/// <summary>
/// 배치 작업 <c>JOB_TEMP_CLEANUP</c>(매일 05:00): 업무 저장으로 확정되지 않은 첨부(올린 지 하루 지남)와
/// 삭제한 지 <c>FILE.DELETED_KEEP_DAYS</c>가 지난 첨부를 지운다. DB 행을 먼저 지우고(정리 대상인지 다시 확인), 지운 경우에만 저장소 파일을 지운다.
/// 저장소 파일을 못 지우면(잠김 등) 건수만 결과에 남기고 계속한다. 그 파일은 DB에서 연결이 끊긴 채 저장소에 남는다.
/// </summary>
public sealed partial class FileCleanupJob(ISpExecutor sp, IFileStorage storage, ILogger<FileCleanupJob> logger) : IScheduledJob
{
    internal const string ListSp = "FW.USP_FILE_CLEANUP_GET_LIST";
    internal const string DeleteSp = "FW.USP_FILE_CLEANUP_DEL";
    internal const int BatchSize = 500;

    // 한 번 실행에 최대 50만 건. 남으면 다음 날 이어서 한다
    private const int MaxBatches = 1000;

    /// <inheritdoc />
    public string Name => "FileCleanup";

    /// <inheritdoc />
    public async Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        int deleted = 0, storageFailed = 0;
        for (var batch = 0; batch < MaxBatches; batch++)
        {
            var files = await sp.QueryAsync<CleanupFile>(ListSp, new { P_BATCH_SIZE = BatchSize }, cancellationToken);
            var deletedInBatch = 0;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await sp.ExecuteAsync(DeleteSp, new { P_FILE_ID = file.FileId }, cancellationToken);
                if (result.AffectedCnt != 1)
                {
                    continue;
                }

                deletedInBatch++;
                try
                {
                    storage.Delete(file.StoredPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    storageFailed++;
                    LogStorageDeleteFailed(logger, file.FileId, ex);
                }
            }

            deleted += deletedInBatch;

            // 다 지웠거나, 목록은 받았는데 하나도 못 지웠으면(그 사이 확정 등) 멈춘다
            if (files.Count < BatchSize || deletedInBatch == 0)
            {
                break;
            }
        }

        return JobRunResult.Done(deleted, storageFailed > 0 ? $"저장소 파일 삭제 실패 {storageFailed}건 (서버 로그 확인)" : null);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "첨부 {FileId}의 저장소 파일을 지우지 못했습니다")]
    private static partial void LogStorageDeleteFailed(ILogger logger, long fileId, Exception ex);

    /// <summary>정리할 파일 (USP_FILE_CLEANUP_GET_LIST).</summary>
    internal sealed class CleanupFile
    {
        public long FileId { get; set; }

        public string StoredPath { get; set; } = "";
    }
}
