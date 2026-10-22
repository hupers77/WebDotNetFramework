using System.Text.Json;
using System.Text.Json.Serialization;
using Portal.Core.Errors;
using Portal.Framework.Web.Logging;
using Portal.Infrastructure.Excel;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>사용자 목록 엑셀 내보내기. 이름이 개인정보라서 파일을 만들기 전에 개인정보 접속기록(다운로드, 사유)을 남긴다.</summary>
public sealed partial class UserAdminService
{
    /// <summary>한 번에 내보낼 수 있는 최대 사용자 수. 넘으면 조건을 좁혀 달라고 한다.</summary>
    public const int MaxExportRows = 5000;

    // 목록 SP의 페이지 크기 상한
    private const int ExportPageSize = 500;

    private static readonly JsonSerializerOptions TargetJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <inheritdoc />
    public async Task<Stream> ExportAsync(string programId, UserSearch search, string reason, CancellationToken cancellationToken = default)
    {
        var rows = new List<UserAdminDto>();
        for (var pageNo = 1; ; pageNo++)
        {
            var page = await sp.QueryPageAsync<UserAdminDto>(ListSp, ListParameters(programId, search, pageNo, ExportPageSize), cancellationToken);
            if (page.TotalCount > MaxExportRows)
            {
                throw new BizException(UserAdminMessageKeys.ExportTooMany, MaxExportRows);
            }

            rows.AddRange(page.Items);
            if (page.Items.Count < ExportPageSize || rows.Count >= page.TotalCount)
            {
                break;
            }
        }

        await pii.WriteAsync(programId, PiiActions.Download, TargetDesc(search, rows.Count), reason, cancellationToken);
        return ExcelWriter.Write(new ExcelSheet<UserAdminDto>(localizer["LBL.USER_LIST"], Columns(), rows));
    }

    /// <summary>접속기록의 대상: 건수와 검색 조건. 검색어는 이름일 수 있어 넣었는지만 남긴다.</summary>
    internal static string TargetDesc(UserSearch search, int count) =>
        JsonSerializer.Serialize(
            new
            {
                cnt = count,
                companyCd = Blank(search.CompanyCd),
                userTypeCd = Blank(search.UserTypeCd),
                userStatCd = Blank(search.UserStatCd),
                lockYn = Blank(search.LockYn),
                keyword = Blank(search.Keyword) is null ? null : "Y",
            },
            TargetJson);

    private List<ExcelColumn<UserAdminDto>> Columns()
    {
        static DateTime? Local(DateTime? utc) => utc is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc).ToLocalTime() : null;

        return
        [
            new(localizer["LBL.USER_ID"], u => u.UserId, 16),
            new(localizer["LBL.USER_NM"], u => u.UserNm, 14),
            new(localizer["LBL.COMPANY"], u => u.CompanyNm, 20),
            new(localizer["LBL.DEPT"], u => u.DeptNm, 20),
            new(localizer["LBL.USER_TYPE"], u => u.UserTypeNm),
            new(localizer["LBL.USER_STAT"], u => u.UserStatNm),
            new(localizer["SYS.USER.PWD_LOCKED"], u => u.LockYn),
            new(localizer["LBL.ACCOUNT_END_DT"], u => u.AccountEndDt),
            new(localizer["LBL.MFA"], u => u.MfaYn),
            new(localizer["LBL.LAST_LOGIN"], u => Local(u.LastLoginDt), 18),
            new(localizer["LBL.ROLES"], u => u.RoleIds, 30),
        ];
    }
}
