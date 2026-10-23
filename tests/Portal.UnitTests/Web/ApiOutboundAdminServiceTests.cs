using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Security;
using Portal.Data;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.UnitTests.Web;

/// <summary>송신 대상 관리 Service: 주소·인증 입력 규칙, 비밀값은 암호화해 넘기고 비우면 유지, 삭제 행은 키와 RowVer만.</summary>
public sealed class ApiOutboundAdminServiceTests
{
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 3];
    private readonly FakeSpExecutor _sp = new();
    private readonly ApiOutboundAdminService _service;

    public ApiOutboundAdminServiceTests()
    {
        _sp.Returns(ApiOutboundAdminService.SaveSp, new SaveResult(1, null));
        _service = new ApiOutboundAdminService(_sp, new ReverseProtector());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ApiOutboundSaveRow Row(string stat = "I") => new()
    {
        RowStat = stat, TargetKey = "ERP", TargetNm = " ERP ", BaseUrl = " https://erp.example.com/api/ ", AuthTypeCd = "BEARER", NewSecret = "tok",
        TimeoutSec = 20, RetryCount = 1, AllowPrivateYn = "N", UseYn = "Y", RowVer = stat == "I" ? null : RowVer,
    };

    private static ApiOutboundSaveRequest Req(params ApiOutboundSaveRow[] rows) => new() { Rows = [.. rows] };

    private JsonElement Sent() => JsonDocument.Parse((string)_sp.LastCall(ApiOutboundAdminService.SaveSp).Parameters["P_ROWS_JSON"]!).RootElement;

    [Fact]
    public async Task 저장은_주소_끝슬래시를_빼고_비밀값은_암호화해서_넘긴다()
    {
        await _service.SaveAsync(Req(Row()), Ct);

        var row = Sent()[0];
        Assert.Equal("https://erp.example.com/api", row.GetProperty("baseUrl").GetString());
        Assert.Equal("ERP", row.GetProperty("targetNm").GetString());
        Assert.Equal("SET", row.GetProperty("secretMode").GetString());
        Assert.Equal("enc:tok", row.GetProperty("secretEnc").GetString());
        Assert.DoesNotContain("\"tok\"", (string)_sp.LastCall(ApiOutboundAdminService.SaveSp).Parameters["P_ROWS_JSON"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 비밀값을_비우면_유지_인증없음이면_비밀값_사용자_헤더를_보내지_않는다()
    {
        var keep = Row("U");
        keep.NewSecret = "";
        await _service.SaveAsync(Req(keep), Ct);
        Assert.Equal("KEEP", Sent()[0].GetProperty("secretMode").GetString());
        Assert.Equal(JsonValueKind.Null, Sent()[0].GetProperty("secretEnc").ValueKind);

        var none = Row("U");
        none.AuthTypeCd = "NONE";
        none.AuthUser = "svc";
        none.AuthHeader = "X-Key";
        await _service.SaveAsync(Req(none), Ct);
        Assert.Equal("KEEP", Sent()[0].GetProperty("secretMode").GetString());
        Assert.Equal(JsonValueKind.Null, Sent()[0].GetProperty("authUser").ValueKind);
        Assert.Equal(JsonValueKind.Null, Sent()[0].GetProperty("authHeader").ValueKind);
    }

    [Fact]
    public async Task 삭제행은_키와_RowVer만_보낸다()
    {
        await _service.SaveAsync(Req(new ApiOutboundSaveRow { RowStat = "D", TargetKey = "ERP", RowVer = RowVer }), Ct);

        Assert.Equal("0000000000000003", Sent()[0].GetProperty("rowVer").GetString());
    }

    [Theory]
    [InlineData("http://erp.example.com")]
    [InlineData("ftp://erp.example.com")]
    [InlineData("https://user:pw@erp.example.com")]
    [InlineData("https://erp.example.com/api?x=1")]
    [InlineData("https://erp.example.com/api#top")]
    [InlineData("erp.example.com")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://10.0.0.5")]
    [InlineData("https://169.254.169.254/latest")]
    [InlineData("")]
    public async Task 기본주소가_https가_아니거나_위험하면_거부(string url)
    {
        var row = Row();
        row.BaseUrl = url;

        var ex = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(row), Ct));

        Assert.Equal(ApiOutboundAdminService.MsgInvalidUrl, ex.MessageKey);
    }

    [Fact]
    public async Task 사내망_허용이면_http와_사설_IP도_가능_루프백은_여전히_거부()
    {
        var ok = Row();
        ok.BaseUrl = "http://10.1.2.3:8080/sap";
        ok.AllowPrivateYn = "Y";
        await _service.SaveAsync(Req(ok), Ct);
        Assert.Equal("http://10.1.2.3:8080/sap", Sent()[0].GetProperty("baseUrl").GetString());

        var loop = Row();
        loop.BaseUrl = "http://127.0.0.1:8080";
        loop.AllowPrivateYn = "Y";
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(loop), Ct));
    }

    [Theory]
    [InlineData("erp")]
    [InlineData("1ERP")]
    [InlineData("ERP-1")]
    [InlineData("E")]
    public async Task 대상키_형식이_틀리면_거부(string key)
    {
        var row = Row();
        row.TargetKey = key;

        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(row), Ct));
    }

    [Fact]
    public async Task 인증_방식별_필수값()
    {
        var apiKey = Row();
        apiKey.AuthTypeCd = "API_KEY";
        apiKey.AuthHeader = "bad header";
        var basic = Row();
        basic.AuthTypeCd = "BASIC";
        basic.AuthUser = " ";
        var unknown = Row();
        unknown.AuthTypeCd = "OAUTH";

        Assert.Equal(ApiOutboundAdminService.MsgInvalidHeader, (await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(apiKey), Ct))).MessageKey);
        Assert.Equal(MessageKeys.InvalidInput, (await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(basic), Ct))).MessageKey);
        Assert.Equal(MessageKeys.InvalidInput, (await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(unknown), Ct))).MessageKey);

        apiKey.AuthHeader = "X-Api-Key";
        await _service.SaveAsync(Req(apiKey), Ct);
        Assert.Equal("X-Api-Key", Sent()[0].GetProperty("authHeader").GetString());
    }

    [Fact]
    public async Task 행상태_RowVer_중복키_빈요청_검사()
    {
        var noVer = Row("U");
        noVer.RowVer = null;

        Assert.Equal(MessageKeys.InvalidInput, (await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(noVer), Ct))).MessageKey);
        Assert.Equal("VAL.DUP_ROW", (await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(Row(), Row()), Ct))).MessageKey);
        Assert.Equal(MessageKeys.NoChanges, (await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(), Ct))).MessageKey);
    }

    private sealed class ReverseProtector : ISecretProtector
    {
        public string Protect(string plainText) => "enc:" + plainText;

        public string Unprotect(string protectedText) => protectedText[4..];
    }
}
