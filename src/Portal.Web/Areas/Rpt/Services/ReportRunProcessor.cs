using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Context;
using Portal.Infrastructure.Files;
using Portal.Security.Authentication;
using Portal.Web.Areas.Rpt.Models;

namespace Portal.Web.Areas.Rpt.Services;

/// <summary>
/// 대기열(<c>TB_REPORT_RUN</c>)에서 요청 하나를 꺼내 만든다. 만드는 동안은 요청자로 동작한다: 요청자의 회사·언어·권한·데이터 범위로
/// 데이터 SP를 실행하므로 화면에서 만들 때와 같은 자료만 나온다. 요청한 뒤에 권한이 없어졌으면 만들지 않고 실패로 끝낸다.
/// 결과 파일은 저장소의 <c>rpt/yyyy/MM/&lt;guid&gt;</c>에 두고, 끝나면 요청자에게 포털 알림이 간다(<c>USP_REPORT_RUN_END_EXEC</c>).
/// </summary>
public sealed partial class ReportRunProcessor(IServiceScopeFactory scopes, IFileStorage storage, TimeProvider time, ILogger<ReportRunProcessor> logger)
{
    internal const string ClaimSp = "FW.USP_REPORT_RUN_CLAIM_EXEC";
    internal const string EndSp = "FW.USP_REPORT_RUN_END_EXEC";
    internal const string CleanupListSp = "FW.USP_REPORT_RUN_CLEANUP_GET_LIST";
    internal const string CleanupDeleteSp = "FW.USP_REPORT_RUN_CLEANUP_DEL";

    /// <summary>한 요청을 만드는 데 줄 수 있는 최대 시간. 넘으면 실패로 끝낸다(서비스가 멈춘 것으로 보는 30분보다 짧다).</summary>
    internal static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(20);

    private const int CleanupBatch = 100;

    /// <summary>대기 중인 요청을 하나 꺼내 만든다. 만든(또는 실패로 끝낸) 요청이 있으면 true.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken stoppingToken)
    {
        ClaimedReportRun? run;
        await using (var claimScope = scopes.CreateAsyncScope())
        {
            run = await claimScope.ServiceProvider.GetRequiredService<ISpExecutor>().QuerySingleAsync<ClaimedReportRun>(ClaimSp, null, stoppingToken);
        }

        if (run is null)
        {
            return false;
        }

        // 요청자 컨텍스트로 만든다. 서비스가 멈추면(stoppingToken) 끝내지 않고 둔다 — 멈춘 요청으로 다음에 정리된다
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Features.Set(new PortalUserFeature(
            new SessionUser
            {
                UserId = run.ReqUserId,
                CompanyCd = run.ReqCompanyCd,
                UserTypeCd = run.ReqUserTypeCd,
                UserNm = run.ReqUserId,
                LangCd = run.ReqLangCd,
                ExpireDt = time.GetUtcNow().UtcDateTime.Add(RunTimeout),
            },
            false));
        accessor.HttpContext = context;

        var sp = services.GetRequiredService<ISpExecutor>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(RunTimeout);

        string? storedPath = null;
        try
        {
            var output = await BuildAsync(services, context, run, timeout.Token);
            var now = time.GetUtcNow();
            storedPath = $"rpt/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}";
            await storage.WriteAsync(storedPath, new MemoryStream(output.Content, writable: false), timeout.Token);

            var ended = await sp.ExecuteAsync(
                EndSp,
                new { P_RUN_ID = run.RunId, P_STATUS_CD = "DONE", P_ROW_CNT = output.RowCount, P_FILE_NM = output.FileName, P_FILE_SIZE = (long)output.Content.Length, P_STORED_PATH = storedPath },
                stoppingToken);
            if (ended.AffectedCnt == 0)
            {
                DeleteQuietly(storedPath);
            }

            LogDone(logger, run.RunId, run.ReportId, output.RowCount);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            DeleteQuietly(storedPath);
            throw;
        }
        catch (Exception ex)
        {
            DeleteQuietly(storedPath);
            var (key, arg) = ex switch
            {
                BizException biz => (biz.MessageKey, biz.Args.Count > 0 ? Convert.ToString(biz.Args[0], CultureInfo.InvariantCulture) : null),
                PermissionDeniedException => (MessageKeys.Forbidden, null),
                OperationCanceledException => (ReportMessageKeys.RunTimeout, null),
                _ => (MessageKeys.SystemError, null),
            };
            if (ex is not (BizException or PermissionDeniedException))
            {
                LogFailed(logger, run.RunId, run.ReportId, ex);
            }

            await sp.ExecuteAsync(EndSp, new { P_RUN_ID = run.RunId, P_STATUS_CD = "FAILED", P_ERR_KEY = key, P_ERR_ARG = arg }, stoppingToken);
        }
        finally
        {
            accessor.HttpContext = null;
        }

        return true;
    }

    /// <summary>보관 기간이 지난(또는 지운) 요청의 파일과 행을 정리한다. 지운 요청 수를 돌려준다.</summary>
    public async Task<int> CleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider.GetRequiredService<ISpExecutor>();
        var deleted = 0;
        while (true)
        {
            var targets = await sp.QueryAsync<CleanupTarget>(CleanupListSp, new { P_BATCH_SIZE = CleanupBatch }, cancellationToken);
            var deletedInBatch = 0;
            foreach (var target in targets)
            {
                var result = await sp.ExecuteAsync(CleanupDeleteSp, new { P_RUN_ID = target.RunId }, cancellationToken);
                if (result.AffectedCnt != 1)
                {
                    continue;
                }

                deletedInBatch++;
                DeleteQuietly(target.StoredPath);
            }

            deleted += deletedInBatch;
            if (targets.Count < CleanupBatch || deletedInBatch == 0)
            {
                return deleted;
            }
        }
    }

    private static async Task<ReportOutput> BuildAsync(IServiceProvider services, HttpContext http, ClaimedReportRun run, CancellationToken cancellationToken)
    {
        var reports = services.GetRequiredService<IReportService>();
        var definition = await reports.GetAsync(run.ReportId, cancellationToken) ?? throw new BizException(MessageKeys.NotFound);

        // 요청한 뒤에 권한이 없어졌을 수 있다
        var granted = await services.GetRequiredService<CurrentPermissions>().GetAsync(cancellationToken);
        var func = ReportFormats.FuncOf(run.FormatCd) ?? throw new BizException(MessageKeys.InvalidInput);
        if (!granted.Can(definition.ReportId, func))
        {
            throw new PermissionDeniedException(definition.ReportId, func);
        }

        var request = new ReportRunRequest
        {
            Params = JsonSerializer.Deserialize<Dictionary<string, string?>>(run.ParamsJson) ?? [],
            Reason = run.Reason,
        };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return await reports.GenerateAsync(action, definition, request, run.FormatCd, cancellationToken);
    }

    private void DeleteQuietly(string? storedPath)
    {
        if (storedPath is null)
        {
            return;
        }

        try
        {
            storage.Delete(storedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogDeleteFailed(logger, storedPath, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "레포트 백그라운드 생성 완료 (요청 {RunId}, {ReportId}, {RowCount}행)")]
    private static partial void LogDone(ILogger logger, long runId, string reportId, int rowCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "레포트 백그라운드 생성 실패 (요청 {RunId}, {ReportId})")]
    private static partial void LogFailed(ILogger logger, long runId, string reportId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "레포트 결과 파일을 저장소에서 지우지 못했습니다 ({StoredPath})")]
    private static partial void LogDeleteFailed(ILogger logger, string storedPath, Exception ex);

    /// <summary><c>USP_REPORT_RUN_CLEANUP_GET_LIST</c> 결과 행.</summary>
    internal sealed class CleanupTarget
    {
        public long RunId { get; set; }

        public string? StoredPath { get; set; }
    }
}
