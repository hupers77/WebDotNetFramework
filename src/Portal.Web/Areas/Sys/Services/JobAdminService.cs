using Portal.Core.Errors;
using Portal.Core.Results;
using Portal.Data;
using Portal.Infrastructure.Scheduling;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>스케줄 관리 (<c>SYS_JOB_MGT</c>). 작업 목록·설정 저장·수동 실행 요청·실행 이력. 실행은 Portal.Worker가 한다.</summary>
public interface IJobAdminService
{
    /// <summary>작업 목록 (작업 ID 순, 최대 <see cref="JobAdminService.MaxRows"/>건).</summary>
    Task<PageResult<JobDto>> GetListAsync(JobSearch search, CancellationToken cancellationToken = default);

    /// <summary>작업 하나의 최근 실행 이력 (<see cref="JobAdminService.HistoryPageSize"/>건).</summary>
    Task<PageResult<JobHistDto>> GetHistoryAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>주기·제한 시간·사용 여부·설명을 저장한다. 주기 형식이 틀리면 <see cref="JobMessageKeys.CronInvalid"/>.</summary>
    Task SaveAsync(JobSaveRequest request, CancellationToken cancellationToken = default);

    /// <summary>수동 실행을 요청한다. Worker가 15초 안에 시작한다.</summary>
    Task RequestRunAsync(JobRunRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class JobAdminService(ISpExecutor sp) : IJobAdminService
{
    /// <summary>한 번에 읽는 최대 작업 수.</summary>
    public const int MaxRows = 500;

    /// <summary>이력 화면에 보이는 건수.</summary>
    public const int HistoryPageSize = 50;

    internal const string ListSp = "FW.USP_JOB_GET_LIST";
    internal const string HistorySp = "FW.USP_JOB_HIST_GET_LIST";
    internal const string SaveSp = "FW.USP_JOB_SAVE";
    internal const string RunSp = "FW.USP_JOB_RUN_REQ_EXEC";

    /// <inheritdoc />
    public Task<PageResult<JobDto>> GetListAsync(JobSearch search, CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<JobDto>(
            ListSp,
            new { P_KEYWORD = string.IsNullOrWhiteSpace(search.Keyword) ? null : search.Keyword.Trim(), P_PAGE_NO = 1, P_PAGE_SIZE = MaxRows },
            cancellationToken);

    /// <inheritdoc />
    public Task<PageResult<JobHistDto>> GetHistoryAsync(string jobId, CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<JobHistDto>(HistorySp, new { P_JOB_ID = jobId, P_PAGE_NO = 1, P_PAGE_SIZE = HistoryPageSize }, cancellationToken);

    /// <inheritdoc />
    public async Task SaveAsync(JobSaveRequest request, CancellationToken cancellationToken = default)
    {
        // 공백을 하나로 맞춰 저장한다 (Worker와 같은 규칙으로 검사)
        var cron = string.IsNullOrWhiteSpace(request.CronExpr)
            ? null
            : string.Join(' ', request.CronExpr.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (cron is not null && !CronSchedule.TryParse(cron, out _))
        {
            throw new BizException(JobMessageKeys.CronInvalid);
        }

        await sp.ExecuteAsync(
            SaveSp,
            new
            {
                P_JOB_ID = request.JobId,
                P_EDIT_VER = request.EditVer,
                P_CRON_EXPR = cron,
                P_TIMEOUT_MIN = request.TimeoutMin,
                P_USE_YN = request.UseYn,
                P_DESCRIPTION = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RequestRunAsync(JobRunRequest request, CancellationToken cancellationToken = default) =>
        sp.ExecuteAsync(RunSp, new { P_JOB_ID = request.JobId }, cancellationToken);
}
