using System.Net;
using System.Text.RegularExpressions;
using Portal.Core.Context;
using Portal.Core.Errors;
using Portal.Core.Results;
using Portal.Data;
using Portal.Security.Network;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>회사 관리 (<c>SYS_COMPANY_MGT</c>). 본사·협력사 목록, 협력사 등록, 회사 정보 수정, 잘못 등록한 협력사 삭제.</summary>
public interface ICompanyAdminService
{
    /// <summary>회사 목록 (본사 먼저, 정렬 순서·회사명 순, 한 쪽 <see cref="CompanyAdminService.PageSize"/>건).</summary>
    Task<PageResult<CompanyDto>> GetListAsync(CompanySearch search, CancellationToken cancellationToken = default);

    /// <summary>회사를 등록하거나 고친다. 사업자등록번호·허용 IP 형식이 틀리면 <see cref="BizException"/>.</summary>
    Task<CompanySaveResult> SaveAsync(CompanySaveRequest request, CancellationToken cancellationToken = default);

    /// <summary>쓰인 적 없는 협력사를 지운다. 계정 등이 있으면 <see cref="CompanyMessageKeys.InUse"/>.</summary>
    Task DeleteAsync(CompanyDeleteRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed partial class CompanyAdminService(ISpExecutor sp, ICurrentUser currentUser) : ICompanyAdminService
{
    /// <summary>한 쪽에 보이는 회사 수.</summary>
    public const int PageSize = 50;

    internal const string ListSp = "FW.USP_COMPANY_GET_LIST";
    internal const string SaveSp = "FW.USP_COMPANY_SAVE";
    internal const string DeleteSp = "FW.USP_COMPANY_DEL";

    /// <inheritdoc />
    public Task<PageResult<CompanyDto>> GetListAsync(CompanySearch search, CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<CompanyDto>(
            ListSp,
            new
            {
                P_KEYWORD = string.IsNullOrWhiteSpace(search.Keyword) ? null : search.Keyword.Trim(),
                P_COMPANY_TYPE_CD = string.IsNullOrEmpty(search.CompanyTypeCd) ? null : search.CompanyTypeCd,
                P_USE_YN = string.IsNullOrEmpty(search.UseYn) ? null : search.UseYn,
                P_PAGE_NO = search.PageNo,
                P_PAGE_SIZE = PageSize,
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<CompanySaveResult> SaveAsync(CompanySaveRequest request, CancellationToken cancellationToken = default)
    {
        var bizRegNo = string.IsNullOrWhiteSpace(request.BizRegNo) ? null : request.BizRegNo.Trim();
        if (bizRegNo is not null && !BizRegNoPattern().IsMatch(bizRegNo))
        {
            throw new BizException("VAL.BIZ_REG_NO_FORMAT");
        }

        var ipList = NormalizeIpList(request.AllowIpList);
        if (ipList is { Length: > 1000 })
        {
            throw new BizException(Core.Keys.MessageKeys.InvalidInput);
        }

        // 자기 회사의 허용 IP에서 지금 접속한 주소가 빠지면 다음 로그인부터 저장한 사람도 들어올 수 없다
        if (request.RowVer is not null && string.Equals(request.CompanyCd, currentUser.CompanyCd, StringComparison.Ordinal)
            && !IpAllowList.Parse(ipList).Allows(IPAddress.TryParse(currentUser.ClientIp, out var ip) ? ip : null))
        {
            throw new BizException(CompanyMessageKeys.IpSelfLockout, currentUser.ClientIp ?? "-");
        }

        var result = await sp.ExecuteAsync(
            SaveSp,
            new
            {
                P_COMPANY_CD = request.RowVer is null ? null : request.CompanyCd,
                P_ROW_VER = request.RowVer,
                P_COMPANY_NM = request.CompanyNm.Trim(),
                P_COMPANY_TYPE_CD = request.CompanyTypeCd,
                P_BIZ_REG_NO = bizRegNo,
                P_ALLOW_IP_LIST = ipList,
                P_CONTRACT_END_DT = request.ContractEndDt,
                P_SORT_NO = request.SortNo,
                P_USE_YN = request.UseYn,
            },
            cancellationToken);
        return new CompanySaveResult(result.NewKey ?? request.CompanyCd ?? "", Math.Max(0, result.AffectedCnt - 1));
    }

    /// <inheritdoc />
    public Task DeleteAsync(CompanyDeleteRequest request, CancellationToken cancellationToken = default) =>
        sp.ExecuteAsync(DeleteSp, new { P_COMPANY_CD = request.CompanyCd, P_ROW_VER = request.RowVer }, cancellationToken);

    /// <summary>
    /// 허용 IP 목록을 검사하고 <c>10.0.0.0/8, 192.168.1.10</c> 형태로 맞춘다. 비었으면 null(제한 없음).
    /// 잘못된 항목이 있으면 그 항목을 담아 <see cref="CompanyMessageKeys.IpInvalid"/>를 던진다(틀린 값 때문에 전부 막히거나 열리는 일을 막는다).
    /// </summary>
    internal static string? NormalizeIpList(string? list)
    {
        if (string.IsNullOrWhiteSpace(list))
        {
            return null;
        }

        var entries = list.Split([',', ';', '\n', '\r', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var entry in entries)
        {
            try
            {
                IpAllowList.Parse(entry);
            }
            catch (FormatException)
            {
                throw new BizException(CompanyMessageKeys.IpInvalid, entry);
            }
        }

        return entries.Count == 0 ? null : string.Join(", ", entries);
    }

    // 가입 신청과 같은 규칙: 숫자로 시작·끝, 사이에 숫자·공백·하이픈 (10~20자)
    [GeneratedRegex(@"^[0-9][0-9 \-]{8,18}[0-9]$")]
    private static partial Regex BizRegNoPattern();
}
