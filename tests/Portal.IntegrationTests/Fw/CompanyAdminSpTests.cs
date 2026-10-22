using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.IntegrationTests.Fw;

/// <summary>
/// 회사 관리 SP: 목록 검색, 협력사 등록(코드 자동 부여·사업자등록번호 중복), 본사 규칙, 유형 고정, 동시성,
/// 계약 종료일을 바꾸면 그 날짜를 따르던 협력사 계정 기한도 바뀜, 쓰인 회사는 삭제 거부, 감사 로그.
/// </summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class CompanyAdminSpTests(DbFixture fixture)
{

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<Portal.Data.SaveResult> SaveAsync(
        TestDb db, string? code, byte[]? rowVer, string type = "PARTNER", string? biz = "990-11-22333",
        DateOnly? end = null, string useYn = "Y", string name = "IT 협력사", string? ip = null) =>
        db.Sp.ExecuteAsync(
            CompanyAdminService.SaveSp,
            new
            {
                P_COMPANY_CD = code, P_ROW_VER = rowVer, P_COMPANY_NM = name, P_COMPANY_TYPE_CD = type, P_BIZ_REG_NO = biz,
                P_ALLOW_IP_LIST = ip, P_CONTRACT_END_DT = end, P_SORT_NO = 5, P_USE_YN = useYn,
            },
            Ct);

    /// <summary>협력사를 등록하고 부여받은 코드를 돌려준다.</summary>
    private static async Task<string> CreateAsync(TestDb db, string? biz = "990-11-22333", DateOnly? end = null, string name = "IT 협력사", string? ip = null) =>
        (await SaveAsync(db, null, null, biz: biz, end: end, name: name, ip: ip)).NewKey!;

    private static async Task<CompanyDto> GetAsync(TestDb db, string code)
    {
        var page = await db.Sp.QueryPageAsync<CompanyDto>(CompanyAdminService.ListSp, new { P_KEYWORD = code, P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct);
        return Assert.Single(page.Items, c => c.CompanyCd == code);
    }

    [Fact]
    public async Task 등록_목록에보이고_사업자등록번호로도찾고_감사로그를남긴다()
    {
        await using var db = await fixture.BeginAsync();

        var result = await SaveAsync(db, "IGNORED", null, ip: "203.0.113.0/24", end: new DateOnly(2030, 12, 31));
        var ptn = result.NewKey!;
        await db.SeedUserAsync("it.co.user", ptn, "PARTNER");

        Assert.Equal(1, result.AffectedCnt);
        Assert.Matches("^P[0-9]{4,}$", ptn);
        var row = await GetAsync(db, ptn);
        Assert.Equal(("협력사", "203.0.113.0/24", new DateOnly(2030, 12, 31), 5, 1, 8),
            (row.CompanyTypeNm, row.AllowIpList, row.ContractEndDt, row.SortNo, row.UserCnt, row.RowVer.Length));
        var byBiz = await db.Sp.QueryPageAsync<CompanyDto>(CompanyAdminService.ListSp, new { P_KEYWORD = "9901122", P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct);
        Assert.Contains(byBiz.Items, c => c.CompanyCd == ptn);
        var unused = await db.Sp.QueryPageAsync<CompanyDto>(CompanyAdminService.ListSp, new { P_KEYWORD = ptn, P_USE_YN = "N", P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct);
        Assert.Empty(unused.Items);
        Assert.Equal("I", await db.ScalarAsync<string>("SELECT TOP 1 ACTION_CD FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_COMPANY' AND ROW_KEY = @C ORDER BY LOG_ID DESC", ("@C", ptn)));
    }

    [Fact]
    public async Task 목록_본사가먼저()
    {
        await using var db = await fixture.BeginAsync();
        await CreateAsync(db, name: "가나다");

        var page = await db.Sp.QueryPageAsync<CompanyDto>(CompanyAdminService.ListSp, new { P_PAGE_NO = 1, P_PAGE_SIZE = 500 }, Ct);

        Assert.Equal("HQ", page.Items[0].CompanyTypeCd);
    }

    [Fact]
    public async Task 수정_계약종료일을바꾸면_그날짜를따르던협력사계정만기한이바뀐다()
    {
        await using var db = await fixture.BeginAsync();
        var oldEnd = new DateOnly(2027, 6, 30);
        var newEnd = new DateOnly(2028, 6, 30);
        var ptn = await CreateAsync(db, end: oldEnd);
        await db.SeedUserAsync("it.co.follow", ptn, "PARTNER");
        await db.SeedUserAsync("it.co.own", ptn, "PARTNER");
        await db.ExecAsync("UPDATE FW.TB_USER SET ACCOUNT_END_DT = @E WHERE USER_ID = 'it.co.follow'", ("@E", oldEnd.ToDateTime(TimeOnly.MinValue)));
        await db.ExecAsync("UPDATE FW.TB_USER SET ACCOUNT_END_DT = '2027-01-31' WHERE USER_ID = 'it.co.own'");

        var result = await SaveAsync(db, ptn, (await GetAsync(db, ptn)).RowVer, end: newEnd);

        Assert.Equal(2, result.AffectedCnt);
        Assert.Equal(
            ["it.co.follow|2028-06-30", "it.co.own|2027-01-31"],
            await db.ColumnAsync<string>("SELECT CONCAT(USER_ID, '|', FORMAT(ACCOUNT_END_DT, 'yyyy-MM-dd')) FROM FW.TB_USER WHERE USER_ID IN ('it.co.follow', 'it.co.own') ORDER BY USER_ID"));
        var audit = await db.ScalarAsync<string>("SELECT TOP 1 AFTER_JSON FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_USER' AND ROW_KEY = @C ORDER BY LOG_ID DESC", ("@C", ptn));
        Assert.Contains("it.co.follow", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("it.co.own", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 수정_기한없던회사에종료일을두면_기한없던계정도따라간다()
    {
        await using var db = await fixture.BeginAsync();
        var ptn = await CreateAsync(db);
        await db.SeedUserAsync("it.co.nolimit", ptn, "PARTNER");

        var result = await SaveAsync(db, ptn, (await GetAsync(db, ptn)).RowVer, end: new DateOnly(2029, 1, 31));

        Assert.Equal(2, result.AffectedCnt);
        Assert.Equal("2029-01-31", await db.ScalarAsync<string>("SELECT FORMAT(ACCOUNT_END_DT, 'yyyy-MM-dd') FROM FW.TB_USER WHERE USER_ID = 'it.co.nolimit'"));
    }

    [Fact]
    public async Task 등록_코드는P와4자리일련번호로_차례로부여()
    {
        await using var db = await fixture.BeginAsync();
        await db.ExecAsync("INSERT INTO FW.TB_COMPANY (COMPANY_CD, COMPANY_NM, COMPANY_TYPE_CD) VALUES ('P9000', N'번호 기준', 'PARTNER'), ('PX123', N'규칙 밖', 'PARTNER')");

        var first = await CreateAsync(db, biz: "990-11-00001");
        var second = await CreateAsync(db, biz: "990-11-00002");

        Assert.Equal(("P9001", "P9002"), (first, second));
    }

    [Fact]
    public async Task 등록_사업자등록번호가같으면_하이픈이달라도거부()
    {
        await using var db = await fixture.BeginAsync();
        await CreateAsync(db);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, null, null, biz: "9901122333"));
        Assert.Equal(CompanyMessageKeys.BizDuplicate, ex.MessageKey);
    }

    [Fact]
    public async Task 등록_협력사는사업자등록번호필수()
    {
        await using var db = await fixture.BeginAsync();

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, null, null, biz: " "));
        Assert.Equal(CompanyMessageKeys.BizRequired, ex.MessageKey);
    }

    [Fact]
    public async Task 등록_본사는새로만들수없다()
    {
        await using var db = await fixture.BeginAsync();

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, null, null, type: "HQ", biz: null));
        Assert.Equal(CompanyMessageKeys.HqRule, ex.MessageKey);
    }

    [Fact]
    public async Task 수정_본사를사용안함으로바꿀수없다()
    {
        await using var db = await fixture.BeginAsync();
        var hq = await GetAsync(db, "HQ");

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, "HQ", hq.RowVer, type: "HQ", biz: null, useYn: "N"));
        Assert.Equal(CompanyMessageKeys.HqRule, ex.MessageKey);
    }

    [Fact]
    public async Task 수정_본사는이름과허용IP를바꿀수있다()
    {
        await using var db = await fixture.BeginAsync();
        var hq = await GetAsync(db, "HQ");

        await SaveAsync(db, "HQ", hq.RowVer, type: "HQ", biz: null, name: "본사 IT", ip: "10.0.0.0/8");

        var after = await GetAsync(db, "HQ");
        Assert.Equal(("본사 IT", "10.0.0.0/8"), (after.CompanyNm, after.AllowIpList));
    }

    [Fact]
    public async Task 수정_유형을바꾸면거부()
    {
        await using var db = await fixture.BeginAsync();
        var ptn = await CreateAsync(db);

        var ex = await Assert.ThrowsAsync<BizException>(async () => await SaveAsync(db, ptn, (await GetAsync(db, ptn)).RowVer, type: "HQ", biz: null));
        Assert.Equal(CompanyMessageKeys.TypeFixed, ex.MessageKey);
    }

    [Fact]
    public async Task 수정_다른사람이먼저고쳤으면거부()
    {
        await using var db = await fixture.BeginAsync();
        var ptn = await CreateAsync(db);
        var stale = (await GetAsync(db, ptn)).RowVer;
        await SaveAsync(db, ptn, stale, name: "먼저 고침");

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, ptn, stale, name: "나중"));
        Assert.Equal(MessageKeys.Concurrency, ex.MessageKey);
    }

    [Fact]
    public async Task 삭제_쓰이지않은협력사는지우고_감사로그를남긴다()
    {
        await using var db = await fixture.BeginAsync();
        var ptn = await CreateAsync(db);

        await db.Sp.ExecuteAsync(CompanyAdminService.DeleteSp, new { P_COMPANY_CD = ptn, P_ROW_VER = (await GetAsync(db, ptn)).RowVer }, Ct);

        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_COMPANY WHERE COMPANY_CD = @C", ("@C", ptn)));
        Assert.Equal("D", await db.ScalarAsync<string>("SELECT TOP 1 ACTION_CD FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_COMPANY' AND ROW_KEY = @C ORDER BY LOG_ID DESC", ("@C", ptn)));
    }

    [Fact]
    public async Task 삭제_부서가있으면거부()
    {
        await using var db = await fixture.BeginAsync();
        var ptn = await CreateAsync(db);
        await db.SeedDeptAsync("IT_CO1_D1", ptn);
        var rowVer = (await GetAsync(db, ptn)).RowVer;

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(CompanyAdminService.DeleteSp, new { P_COMPANY_CD = ptn, P_ROW_VER = rowVer }, Ct));
        Assert.Equal(CompanyMessageKeys.InUse, ex.MessageKey);
    }

    [Fact]
    public async Task 삭제_본사는거부()
    {
        await using var db = await fixture.BeginAsync();
        var hq = await GetAsync(db, "HQ");

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(CompanyAdminService.DeleteSp, new { P_COMPANY_CD = "HQ", P_ROW_VER = hq.RowVer }, Ct));
        Assert.Equal(CompanyMessageKeys.HqRule, ex.MessageKey);
    }
}
