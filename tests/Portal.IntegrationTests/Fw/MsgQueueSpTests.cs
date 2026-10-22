using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Fw;

/// <summary>메일 대기열 SP: 받는 사람 주소·언어 결정, 쓰지 않는 템플릿·주소 없음은 건너뜀, 치환값 형식.</summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class MsgQueueSpTests(DbFixture fixture)
{
    private const string SaveSp = "FW.USP_MSG_QUEUE_SAVE";
    private const string Template = "TPL_PARTNER_APPROVED";
    private static readonly byte[] Email = [1, 2, 3, 4];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<string> RowAsync(TestDb db, string? msgId) =>
        db.ScalarAsync<string>(
            "SELECT CONCAT(TEMPLATE_ID, '|', CHANNEL_CD, '|', ISNULL(TO_USER_ID, '-'), '|', LANG_CD, '|', STAT_CD, '|', TRY_CNT, '|', REG_ID, '|', CONVERT(VARCHAR(20), TO_EMAIL_ENC, 2))"
            + " FROM FW.TB_MSG_QUEUE WHERE MSG_ID = @M",
            ("@M", long.Parse(msgId!, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public async Task 사용자에게_사용자주소와언어로()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.mail", "HQ", "EMP");
        await db.ExecAsync("UPDATE FW.TB_USER SET EMAIL_ENC = 0x0A0B, LANG_CD = 'en' WHERE USER_ID = 'it.mail'");

        var result = await db.Sp.ExecuteAsync(SaveSp, new { P_TEMPLATE_ID = Template, P_TO_USER_ID = "it.mail", P_PARAMS_JSON = """{"USER_NM":"홍"}""" }, Ct);

        Assert.Equal(1, result.AffectedCnt);
        Assert.Equal($"{Template}|MAIL|it.mail|en|READY|0|{db.User.UserId}|0A0B", await RowAsync(db, result.NewKey));
    }

    [Fact]
    public async Task 계정없는사람_주소직접_쓰지않는언어는기본언어로()
    {
        await using var db = await fixture.BeginAsync();

        var result = await db.Sp.ExecuteAsync(SaveSp, new { P_TEMPLATE_ID = Template, P_TO_EMAIL_ENC = Email, P_TO_LANG_CD = "fr" }, Ct);

        Assert.Equal($"{Template}|MAIL|-|ko|READY|0|{db.User.UserId}|01020304", await RowAsync(db, result.NewKey));
    }

    [Fact]
    public async Task 주소가없거나_쓰지않는템플릿이면_쌓지않는다()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.nomail", "HQ", "EMP");
        var before = await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_MSG_QUEUE");

        var noEmail = await db.Sp.ExecuteAsync(SaveSp, new { P_TEMPLATE_ID = Template, P_TO_USER_ID = "it.nomail" }, Ct);
        var unknown = await db.Sp.ExecuteAsync(SaveSp, new { P_TEMPLATE_ID = "TPL_NONE", P_TO_EMAIL_ENC = Email }, Ct);
        await db.ExecAsync("UPDATE FW.TB_MSG_TEMPLATE SET USE_YN = 'N' WHERE TEMPLATE_ID = @T", ("@T", Template));
        var unused = await db.Sp.ExecuteAsync(SaveSp, new { P_TEMPLATE_ID = Template, P_TO_EMAIL_ENC = Email }, Ct);

        Assert.Equal((0, (string?)null), (noEmail.AffectedCnt, noEmail.NewKey));
        Assert.Equal(0, unknown.AffectedCnt);
        Assert.Equal(0, unused.AffectedCnt);
        Assert.Equal(before, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_MSG_QUEUE"));
    }

    [Fact]
    public async Task 초기템플릿_3개언어_치환변수형식()
    {
        await using var db = await fixture.BeginAsync();

        var rows = await db.ColumnAsync<string>(
            "SELECT CONCAT(T.TEMPLATE_ID, '|', L.LANG_CD) FROM FW.TB_MSG_TEMPLATE T JOIN FW.TB_MSG_TEMPLATE_LANG L ON L.TEMPLATE_ID = T.TEMPLATE_ID"
            + " WHERE T.TEMPLATE_ID IN ('TPL_PARTNER_APPROVED', 'TPL_PARTNER_REJECTED') ORDER BY 1");

        Assert.Equal(6, rows.Count);
        Assert.Equal(0, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM FW.TB_MSG_TEMPLATE_LANG WHERE SUBJECT LIKE '%' + CHAR(10) + '%' OR BODY NOT LIKE '%{{USER_NM}}%'"));
    }

    [Fact]
    public async Task 치환값이JSON이아니면_오류()
    {
        await using var db = await fixture.BeginAsync();

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(SaveSp, new { P_TEMPLATE_ID = Template, P_TO_EMAIL_ENC = Email, P_PARAMS_JSON = "{bad" }, Ct));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }
}
