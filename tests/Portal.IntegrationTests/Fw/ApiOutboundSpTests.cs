using Portal.Core.Errors;
using Portal.Core.Security;
using Portal.Framework.Web.Api.Outbound;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.IntegrationTests.Fw;

/// <summary>송신 대상 SP: 저장·수정·삭제, 비밀값 유지/교체/삭제, 제약(https, 인증), 감사 로그에 비밀값 없음.</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class ApiOutboundSpTests(DbFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ApiOutboundSaveRow Row(string key = "T_ERP", string stat = "I") => new()
    {
        RowStat = stat, TargetKey = key, TargetNm = "ERP", BaseUrl = "https://erp.example.com/api", AuthTypeCd = "BEARER", NewSecret = "tok-1",
        TimeoutSec = 20, RetryCount = 1, AllowPrivateYn = "N", UseYn = "Y",
    };

    [Fact]
    public async Task 저장_비밀값유지_교체_인증없음전환_삭제()
    {
        await using var db = await fixture.BeginAsync();
        var service = new ApiOutboundAdminService(db.Sp, new Plain());
        await service.SaveAsync(new ApiOutboundSaveRequest { Rows = [Row()] }, Ct);

        var created = (await service.GetListAsync(Ct)).Items.Single(t => t.TargetKey == "T_ERP");
        Assert.Equal("Y", created.SecretYn);
        Assert.Equal("enc:tok-1", await db.ScalarAsync<string>("SELECT AUTH_SECRET_ENC FROM FW.TB_API_OUTBOUND WHERE TARGET_KEY = 'T_ERP'"));

        // 비우고 저장하면 유지
        var keep = Row(stat: "U");
        keep.NewSecret = "";
        keep.TargetNm = "ERP2";
        keep.RowVer = created.RowVer;
        await service.SaveAsync(new ApiOutboundSaveRequest { Rows = [keep] }, Ct);
        var after = (await service.GetListAsync(Ct)).Items.Single(t => t.TargetKey == "T_ERP");
        Assert.Equal("enc:tok-1", await db.ScalarAsync<string>("SELECT AUTH_SECRET_ENC FROM FW.TB_API_OUTBOUND WHERE TARGET_KEY = 'T_ERP'"));

        // 인증 없음으로 바꾸면 비밀값이 지워진다
        var none = Row(stat: "U");
        none.AuthTypeCd = "NONE";
        none.RowVer = after.RowVer;
        await service.SaveAsync(new ApiOutboundSaveRequest { Rows = [none] }, Ct);
        Assert.Equal("N", (await service.GetListAsync(Ct)).Items.Single(t => t.TargetKey == "T_ERP").SecretYn);

        // 감사 로그에 비밀값 원문 없음
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE AFTER_JSON LIKE '%tok-1%' OR BEFORE_JSON LIKE '%tok-1%'"));

        var current = (await service.GetListAsync(Ct)).Items.Single(t => t.TargetKey == "T_ERP");
        await service.SaveAsync(new ApiOutboundSaveRequest { Rows = [new ApiOutboundSaveRow { RowStat = "D", TargetKey = "T_ERP", RowVer = current.RowVer }] }, Ct);
        Assert.DoesNotContain((await service.GetListAsync(Ct)).Items, t => t.TargetKey == "T_ERP");
    }

    // SP가 오류에서 ROLLBACK하면 테스트 트랜잭션까지 끝나므로, 오류를 기대하는 호출은 테스트마다 마지막에 하나만 둔다
    [Fact]
    public async Task 이전_RowVer로는_수정할_수_없다()
    {
        await using var db = await fixture.BeginAsync();
        var service = new ApiOutboundAdminService(db.Sp, new Plain());
        await service.SaveAsync(new ApiOutboundSaveRequest { Rows = [Row()] }, Ct);
        var keep = Row(stat: "U");
        keep.NewSecret = "";
        keep.RowVer = (await service.GetListAsync(Ct)).Items.Single(t => t.TargetKey == "T_ERP").RowVer;
        await service.SaveAsync(new ApiOutboundSaveRequest { Rows = [keep] }, Ct);

        await Assert.ThrowsAsync<BizException>(() => service.SaveAsync(new ApiOutboundSaveRequest { Rows = [keep] }, Ct));
    }

    [Fact]
    public async Task 중복키는_오류()
    {
        await using var db = await fixture.BeginAsync();
        var service = new ApiOutboundAdminService(db.Sp, new Plain());
        var none = Row();
        none.AuthTypeCd = "NONE";
        none.NewSecret = null;
        await service.SaveAsync(new ApiOutboundSaveRequest { Rows = [none] }, Ct);

        await Assert.ThrowsAsync<BizException>(() => service.SaveAsync(new ApiOutboundSaveRequest { Rows = [none] }, Ct));
    }

    [Fact]
    public async Task 비밀값_없는_인증전환은_거부()
    {
        await using var db = await fixture.BeginAsync();
        var service = new ApiOutboundAdminService(db.Sp, new Plain());
        var none = Row();
        none.AuthTypeCd = "NONE";
        none.NewSecret = null;
        await service.SaveAsync(new ApiOutboundSaveRequest { Rows = [none] }, Ct);

        var target = (await service.GetListAsync(Ct)).Items.Single(t => t.TargetKey == "T_ERP");
        var bearer = Row(stat: "U");
        bearer.NewSecret = "";
        bearer.RowVer = target.RowVer;
        await Assert.ThrowsAsync<BizException>(() => service.SaveAsync(new ApiOutboundSaveRequest { Rows = [bearer] }, Ct));
    }

    [Fact]
    public async Task 송신_클라이언트용_조회는_암호화된_비밀값을_포함하고_없으면_0행()
    {
        await using var db = await fixture.BeginAsync();
        await new ApiOutboundAdminService(db.Sp, new Plain()).SaveAsync(new ApiOutboundSaveRequest { Rows = [Row()] }, Ct);

        var row = await db.Sp.QuerySingleAsync<OutboundTargetStore.Row>(OutboundTargetStore.GetSp, new { P_TARGET_KEY = "T_ERP" }, Ct);

        Assert.Equal("enc:tok-1", row!.AuthSecretEnc);
        Assert.Null(await db.Sp.QuerySingleAsync<OutboundTargetStore.Row>(OutboundTargetStore.GetSp, new { P_TARGET_KEY = "NOPE" }, Ct));
    }

    [Fact]
    public async Task 테이블_제약은_http와_잘못된_인증방식을_막는다()
    {
        await using var db = await fixture.BeginAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => db.ExecAsync(
            "INSERT INTO FW.TB_API_OUTBOUND (TARGET_KEY, TARGET_NM, BASE_URL) VALUES ('T_HTTP', N'x', 'http://erp.example.com')"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.ExecAsync(
            "INSERT INTO FW.TB_API_OUTBOUND (TARGET_KEY, TARGET_NM, BASE_URL, AUTH_TYPE_CD) VALUES ('T_AUTH', N'x', 'https://a.example.com', 'OAUTH')"));
    }

    private sealed class Plain : ISecretProtector
    {
        public string Protect(string plainText) => "enc:" + plainText;

        public string Unprotect(string protectedText) => protectedText[4..];
    }
}
