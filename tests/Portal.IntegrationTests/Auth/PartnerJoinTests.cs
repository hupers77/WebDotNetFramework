using Microsoft.AspNetCore.DataProtection;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Security.Authentication;
using Portal.Security.Passwords;
using Portal.Security.Secrets;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using static Portal.IntegrationTests.Auth.AuthTestContext;

namespace Portal.IntegrationTests.Auth;

/// <summary>
/// 협력사 가입 신청 → 본사 승인 → 로그인 (설계서 5장). 회사 확인, ID 중복, 승인·반려, 동시 처리, 협력사 사용자의 처리 차단.
/// </summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class PartnerJoinTests(DbFixture fixture)
{
    private const string Pwd = "Partner#2026";
    private const string BizRegNo = "999-88-77777";
    private static readonly DataProtectionPersonalDataProtector Protector = new(new EphemeralDataProtectionProvider());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PartnerJoinService NewJoin(TestDb db) => new(db.Sp, Hasher, new PasswordPolicy(new FakePortalConfig()), Protector);

    private static PartnerApproveService NewApprove(TestDb db) => new(db.Sp, Protector);

    /// <summary>사업자등록번호가 있는 협력사를 만든다 (하이픈 없이 저장된 번호도 찾아야 한다).</summary>
    private static async Task SeedPartnerAsync(TestDb db, string companyCd = "IT9001", string bizRegNo = "9998877777", string? contractEnd = "2099-12-31")
    {
        await db.SeedCompanyAsync(companyCd);
        await db.ExecAsync("UPDATE FW.TB_COMPANY SET BIZ_REG_NO = @B, CONTRACT_END_DT = @E WHERE COMPANY_CD = @C",
            ("@B", bizRegNo), ("@E", contractEnd), ("@C", companyCd));
    }

    private static Task<long> RequestAsync(TestDb db, string userId = "it.ptn.kim", string bizRegNo = BizRegNo)
    {
        AsAnonymous(db);
        return NewJoin(db).RequestAsync(new PartnerJoinRequest(bizRegNo, userId, "김협력", "kim@partner.example.com", Pwd), Ct);
    }

    /// <summary>본사 관리자로 돌아와 신청을 찾는다.</summary>
    private static async Task<PartnerJoinDto> FindAsync(TestDb db, string userId, string? stat = "REQUESTED")
    {
        db.AsUser("it.admin", "HQ", "EMP");
        var list = await NewApprove(db).GetListAsync(new PartnerJoinSearch { ReqStatCd = stat, Keyword = userId }, Ct);
        return Assert.Single(list.Items);
    }

    private static PartnerJoinProcessRequest Process(PartnerJoinDto row, string? reason = null) =>
        new() { ReqId = row.ReqId, RowVer = row.RowVer, RejectReason = reason };

    [Fact]
    public async Task 신청_승인_같은ID비밀번호로로그인_협력사사용자역할()
    {
        await using var db = await fixture.BeginAsync();
        await SeedPartnerAsync(db);

        var reqId = await RequestAsync(db);
        var row = await FindAsync(db, "it.ptn.kim");
        Assert.Equal((reqId, "IT9001", "ki***@partner.example.com", "처리 대기"), (row.ReqId, row.CompanyCd, row.EmailMasked, row.ReqStatNm));
        Assert.Null(row.EmailEnc);

        Assert.Equal("it.ptn.kim", await NewApprove(db).ApproveAsync(Process(row), Ct));

        Assert.Equal("PARTNER|ACTIVE|IT9001|2099-12-31|N", await db.ScalarAsync<string>(
            "SELECT CONCAT(USER_TYPE_CD, '|', USER_STAT_CD, '|', COMPANY_CD, '|', CONVERT(CHAR(10), ACCOUNT_END_DT, 23), '|', PWD_CHG_REQ_YN) FROM FW.TB_USER WHERE USER_ID = 'it.ptn.kim'"));
        Assert.Equal(["PARTNER_USER"], await db.ColumnAsync<string>("SELECT ROLE_ID FROM FW.TB_USER_ROLE WHERE USER_ID = 'it.ptn.kim'"));
        Assert.Equal("kim@partner.example.com", Protector.Unprotect(await db.ScalarAsync<byte[]>("SELECT EMAIL_ENC FROM FW.TB_USER WHERE USER_ID = 'it.ptn.kim'")));
        Assert.Equal(1, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM FW.TB_PARTNER_JOIN_REQ WHERE REQ_ID = @R AND REQ_STAT_CD = 'APPROVED' AND PWD_HASH IS NULL AND PWD_SALT IS NULL AND PROC_ID = 'it.admin'",
            ("@R", reqId)));
        Assert.Equal(2, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE (TABLE_NM = 'FW.TB_USER' AND ROW_KEY = 'it.ptn.kim') OR (TABLE_NM = 'FW.TB_PARTNER_JOIN_REQ' AND ROW_KEY = @R AND ACTION_CD = 'U')",
            ("@R", reqId.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        AsAnonymous(db);
        var login = await NewAuth(db, PasswordOnly()).LoginAsync(new LoginRequest("it.ptn.kim", Pwd, "IT-Agent"), Ct);
        Assert.True(login.Succeeded, login.MessageKey);
        Assert.False(login.PasswordChangeRequired);
    }

    [Fact]
    public async Task 승인전에는_로그인할수없다()
    {
        await using var db = await fixture.BeginAsync();
        await SeedPartnerAsync(db);
        await RequestAsync(db);

        AsAnonymous(db);
        var login = await NewAuth(db).LoginAsync(new LoginRequest("it.ptn.kim", Pwd, "IT-Agent"), Ct);

        Assert.Equal(MessageKeys.LoginFailed, login.MessageKey);
    }

    [Fact]
    public async Task 반려_사유와비밀번호삭제_같은ID로다시신청할수있다()
    {
        await using var db = await fixture.BeginAsync();
        await SeedPartnerAsync(db);
        await RequestAsync(db);
        var row = await FindAsync(db, "it.ptn.kim");

        await NewApprove(db).RejectAsync(Process(row, " 소속 확인 불가 "), Ct);

        var rejected = await FindAsync(db, "it.ptn.kim", "REJECTED");
        Assert.Equal(("소속 확인 불가", "it.admin"), (rejected.RejectReason, rejected.ProcId));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_PARTNER_JOIN_REQ WHERE PWD_HASH IS NOT NULL AND USER_ID = 'it.ptn.kim'"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_USER WHERE USER_ID = 'it.ptn.kim'"));

        await RequestAsync(db);
        Assert.Equal(2, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_PARTNER_JOIN_REQ WHERE USER_ID = 'it.ptn.kim'"));
    }

    [Fact]
    public async Task 승인반려_결과안내메일을_같은트랜잭션으로_대기열에()
    {
        await using var db = await fixture.BeginAsync();
        await SeedPartnerAsync(db);
        await RequestAsync(db, "it.ptn.ok");
        await RequestAsync(db, "it.ptn.no");
        await NewApprove(db).ApproveAsync(Process(await FindAsync(db, "it.ptn.ok")), Ct);
        await NewApprove(db).RejectAsync(Process(await FindAsync(db, "it.ptn.no"), "소속 확인 불가"), Ct);

        var rows = await db.ColumnAsync<string>(
            "SELECT CONCAT(TEMPLATE_ID, '|', ISNULL(TO_USER_ID, '-'), '|', LANG_CD, '|', STAT_CD, '|', JSON_VALUE(PARAMS_JSON, '$.USER_ID'), '|', ISNULL(JSON_VALUE(PARAMS_JSON, '$.REJECT_REASON'), '-'))"
            + " FROM FW.TB_MSG_QUEUE WHERE JSON_VALUE(PARAMS_JSON, '$.USER_ID') LIKE 'it.ptn.%' ORDER BY MSG_ID");
        Assert.Equal(["TPL_PARTNER_APPROVED|it.ptn.ok|ko|READY|it.ptn.ok|-", "TPL_PARTNER_REJECTED|-|ko|READY|it.ptn.no|소속 확인 불가"], rows);
        var emails = await db.ColumnAsync<byte[]>("SELECT TO_EMAIL_ENC FROM FW.TB_MSG_QUEUE WHERE JSON_VALUE(PARAMS_JSON, '$.USER_ID') LIKE 'it.ptn.%'");
        Assert.All(emails, e => Assert.Equal("kim@partner.example.com", Protector.Unprotect(e)));
    }

    [Theory]
    [InlineData("111-22-33333", "PARTNER", "2099-12-31")] // 등록되지 않은 번호
    [InlineData(BizRegNo, "HQ", "2099-12-31")]             // 본사 회사
    [InlineData(BizRegNo, "PARTNER", "2000-01-01")]        // 계약 종료
    public async Task 등록된협력사가아니면_신청거부(string bizRegNo, string type, string contractEnd)
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync("IT9001", type);
        await db.ExecAsync("UPDATE FW.TB_COMPANY SET BIZ_REG_NO = '9998877777', CONTRACT_END_DT = @E WHERE COMPANY_CD = 'IT9001'", ("@E", contractEnd));

        var ex = await Assert.ThrowsAsync<BizException>(() => RequestAsync(db, bizRegNo: bizRegNo));

        Assert.Equal(PartnerJoinMessageKeys.CompanyNotFound, ex.MessageKey);
    }

    [Fact]
    public async Task 기존계정ID_대소문자달라도_신청거부()
    {
        await using var db = await fixture.BeginAsync();
        await SeedPartnerAsync(db);

        var ex = await Assert.ThrowsAsync<BizException>(() => RequestAsync(db, userId: "IT.ADMIN"));

        Assert.Equal(PartnerJoinMessageKeys.IdTaken, ex.MessageKey);
    }

    [Fact]
    public async Task 처리대기중인ID_신청거부()
    {
        await using var db = await fixture.BeginAsync();
        await SeedPartnerAsync(db);
        await RequestAsync(db);

        var ex = await Assert.ThrowsAsync<BizException>(() => RequestAsync(db));

        Assert.Equal(PartnerJoinMessageKeys.IdTaken, ex.MessageKey);
    }

    [Fact]
    public async Task 반려사유없음_거부()
    {
        await using var db = await fixture.BeginAsync();
        await SeedPartnerAsync(db);
        await RequestAsync(db);
        var row = await FindAsync(db, "it.ptn.kim");

        var ex = await Assert.ThrowsAsync<BizException>(() => NewApprove(db).RejectAsync(Process(row, "  "), Ct));

        Assert.Equal(PartnerJoinMessageKeys.RejectReasonRequired, ex.MessageKey);
    }

    [Fact]
    public async Task 이미처리한신청_다시처리하면동시성오류()
    {
        await using var db = await fixture.BeginAsync();
        await SeedPartnerAsync(db);
        await RequestAsync(db);
        var row = await FindAsync(db, "it.ptn.kim");
        await NewApprove(db).ApproveAsync(Process(row), Ct);

        var ex = await Assert.ThrowsAsync<BizException>(() => NewApprove(db).RejectAsync(Process(row, "중복"), Ct));

        Assert.Equal(MessageKeys.Concurrency, ex.MessageKey);
    }

    [Fact]
    public async Task 승인전에같은ID계정이생기면_승인거부()
    {
        await using var db = await fixture.BeginAsync();
        await SeedPartnerAsync(db);
        await RequestAsync(db);
        var row = await FindAsync(db, "it.ptn.kim");
        await db.SeedUserAsync("it.ptn.kim", "HQ", "EMP");

        var ex = await Assert.ThrowsAsync<BizException>(() => NewApprove(db).ApproveAsync(Process(row), Ct));

        Assert.Equal(PartnerJoinMessageKeys.IdTaken, ex.MessageKey);
    }

    [Fact]
    public async Task 협력사사용자_자기회사신청만보이고_처리할수없다()
    {
        await using var db = await fixture.BeginAsync();
        await SeedPartnerAsync(db);
        await SeedPartnerAsync(db, "IT9002", "5554433333");
        await RequestAsync(db);
        await RequestAsync(db, "it.ptn.lee", "555-44-33333");
        var row = await FindAsync(db, "it.ptn.kim");
        await db.SeedUserAsync("it.ptn.admin", "IT9002", "PARTNER", roles: ["PARTNER_ADMIN"]);
        db.AsUser("it.ptn.admin", "IT9002", "PARTNER");

        var mine = await NewApprove(db).GetListAsync(new PartnerJoinSearch(), Ct);
        Assert.Equal(["it.ptn.lee"], mine.Items.Select(r => r.UserId));

        var ex = await Assert.ThrowsAsync<BizException>(() => NewApprove(db).ApproveAsync(Process(row), Ct));
        Assert.Equal(MessageKeys.NotFound, ex.MessageKey);
    }
}
