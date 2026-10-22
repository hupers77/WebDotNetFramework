using System.Security.Cryptography;
using Portal.Core.Results;
using Portal.Data;
using Portal.Security.Secrets;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>협력사 계정 승인 (<c>SYS_PARTNER_APPROVE</c>). 가입 신청 목록, 승인(계정 생성), 반려.</summary>
public interface IPartnerApproveService
{
    /// <summary>신청 목록 (오래된 신청부터, 최대 <see cref="PartnerApproveService.MaxRows"/>건). 이메일은 가려서 돌려준다.</summary>
    Task<PageResult<PartnerJoinDto>> GetListAsync(PartnerJoinSearch search, CancellationToken cancellationToken = default);

    /// <summary>승인: 신청한 ID·비밀번호로 협력사 사용자 계정을 만든다. 만든 사용자 ID를 돌려준다.</summary>
    Task<string> ApproveAsync(PartnerJoinProcessRequest request, CancellationToken cancellationToken = default);

    /// <summary>반려: 사유를 남기고 신청의 비밀번호를 지운다.</summary>
    Task RejectAsync(PartnerJoinProcessRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PartnerApproveService(ISpExecutor sp, IPersonalDataProtector protector) : IPartnerApproveService
{
    /// <summary>한 번에 읽는 최대 행 수.</summary>
    public const int MaxRows = 500;

    internal const string ListSp = "FW.USP_PARTNER_JOIN_GET_LIST";
    internal const string ApproveSp = "FW.USP_PARTNER_JOIN_APPROVE_EXEC";

    /// <inheritdoc />
    public async Task<PageResult<PartnerJoinDto>> GetListAsync(PartnerJoinSearch search, CancellationToken cancellationToken = default)
    {
        var page = await sp.QueryPageAsync<PartnerJoinDto>(
            ListSp,
            new
            {
                P_REQ_STAT_CD = string.IsNullOrWhiteSpace(search.ReqStatCd) ? null : search.ReqStatCd.Trim(),
                P_KEYWORD = string.IsNullOrWhiteSpace(search.Keyword) ? null : search.Keyword.Trim(),
                P_PAGE_NO = 1,
                P_PAGE_SIZE = MaxRows,
            },
            cancellationToken);
        foreach (var row in page.Items)
        {
            row.EmailMasked = PersonalDataMask.Email(Decrypt(row.EmailEnc));
            row.EmailEnc = null;
        }

        return page;
    }

    /// <inheritdoc />
    public async Task<string> ApproveAsync(PartnerJoinProcessRequest request, CancellationToken cancellationToken = default) =>
        (await ProcessAsync(request, "Y", null, cancellationToken)).NewKey!;

    /// <inheritdoc />
    public Task RejectAsync(PartnerJoinProcessRequest request, CancellationToken cancellationToken = default) =>
        ProcessAsync(request, "N", request.RejectReason?.Trim(), cancellationToken);

    private Task<SaveResult> ProcessAsync(PartnerJoinProcessRequest request, string approveYn, string? reason, CancellationToken cancellationToken) =>
        sp.ExecuteAsync(
            ApproveSp,
            new { P_REQ_ID = request.ReqId, P_ROW_VER = request.RowVer, P_APPROVE_YN = approveYn, P_REJECT_REASON = reason },
            cancellationToken);

    // 키를 잃었거나 값이 손상되었으면 가린 값으로만 보인다 (목록 조회가 멈추지 않게)
    private string? Decrypt(byte[]? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
