using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Security.Passwords;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.UnitTests.Web;

/// <summary>API 클라이언트 관리 Service: 입력 정규화(코드·IP·기한), 행 상태별 필수값, 비밀값은 해시만 SP로 넘기고 원문은 호출자에게만.</summary>
public sealed class ApiClientAdminServiceTests
{
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 9];
    private readonly FakeSpExecutor _sp = new();
    private readonly Pbkdf2PasswordHasher _hasher = new();
    private readonly ApiClientAdminService _service;

    public ApiClientAdminServiceTests()
    {
        _sp.Returns(ApiClientAdminService.SaveSp, new SaveResult(1, null));
        _sp.Returns(ApiClientAdminService.SecretSp, new SaveResult(1, null));
        _service = new ApiClientAdminService(_sp, _hasher);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ApiClientSaveRow Row(string stat = "I", string id = "erp-sync") => new()
    {
        RowStat = stat, ClientId = id, ClientNm = " ERP ", ApiCodes = "ping, order_get;PING", AllowIpList = "10.0.0.0/8 , 10.0.0.0/8", ExpireDt = "2027-03-31", UseYn = "Y",
        RowVer = stat == "I" ? null : RowVer,
    };

    private static ApiClientSaveRequest Req(params ApiClientSaveRow[] rows) => new() { Rows = [.. rows] };

    [Fact]
    public async Task 저장은_코드를_대문자로_중복없이_IP는_정리해서_JSON으로_넘긴다()
    {
        await _service.SaveAsync(Req(Row("I"), Row("U", "erp-2")), Ct);

        var rows = JsonDocument.Parse((string)_sp.LastCall(ApiClientAdminService.SaveSp).Parameters["P_ROWS_JSON"]!).RootElement;
        Assert.Equal("PING,ORDER_GET", rows[0].GetProperty("apiCodes").GetString());
        Assert.Equal("10.0.0.0/8", rows[0].GetProperty("allowIpList").GetString());
        Assert.Equal("ERP", rows[0].GetProperty("clientNm").GetString());
        Assert.Equal("2027-03-31", rows[0].GetProperty("expireDt").GetString());
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("rowVer").ValueKind);
        Assert.Equal("0000000000000009", rows[1].GetProperty("rowVer").GetString());
    }

    [Fact]
    public async Task 삭제행은_이름이나_코드_검사없이_RowVer만_필요()
    {
        await _service.SaveAsync(Req(new ApiClientSaveRow { RowStat = "D", ClientId = "old", RowVer = RowVer }), Ct);

        Assert.Contains("\"rowStat\":\"D\"", (string)_sp.LastCall(ApiClientAdminService.SaveSp).Parameters["P_ROWS_JSON"]!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bad id!", "I")]
    [InlineData("ok", "X")]
    [InlineData("ok", "U")]
    public async Task 잘못된_ID_행상태_RowVer누락은_입력오류(string id, string stat)
    {
        var row = Row("I", id);
        row.RowStat = stat;
        row.RowVer = null;

        var ex = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(row), Ct));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }

    [Fact]
    public async Task 이름이_없거나_UseYn이_잘못되면_입력오류()
    {
        var noName = Row();
        noName.ClientNm = " ";
        var badYn = Row(id: "b");
        badYn.UseYn = "X";

        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(noName), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(badYn), Ct));
    }

    [Fact]
    public async Task 같은_ID가_두번이면_거부_빈_요청은_변경없음()
    {
        var dup = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(Row(), Row("I", "ERP-SYNC")), Ct));
        var none = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(), Ct));

        Assert.Equal("VAL.DUP_ROW", dup.MessageKey);
        Assert.Equal(MessageKeys.NoChanges, none.MessageKey);
    }

    [Theory]
    [InlineData("1BAD")]
    [InlineData("A")]
    [InlineData("ORDER-GET")]
    public async Task 허용코드_형식이_틀리면_코드를_담아_거부(string code)
    {
        var row = Row();
        row.ApiCodes = "PING, " + code;

        var ex = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(row), Ct));

        Assert.Equal(ApiClientAdminService.MsgInvalidCode, ex.MessageKey);
        Assert.Equal(code.ToUpperInvariant(), ex.Args[0]);
    }

    [Fact]
    public async Task 잘못된_IP나_기한은_거부_빈_값은_제한없음()
    {
        var badIp = Row();
        badIp.AllowIpList = "10.0.0";
        var badDate = Row(id: "d");
        badDate.ExpireDt = "2027-13-40";
        var empty = Row(id: "e");
        empty.AllowIpList = " ";
        empty.ExpireDt = "";
        empty.ApiCodes = null;

        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(badIp), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Req(badDate), Ct));
        await _service.SaveAsync(Req(empty), Ct);

        var json = JsonDocument.Parse((string)_sp.LastCall(ApiClientAdminService.SaveSp).Parameters["P_ROWS_JSON"]!).RootElement[0];
        Assert.Equal("", json.GetProperty("apiCodes").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("allowIpList").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("expireDt").ValueKind);
    }

    [Fact]
    public async Task 비밀값발급은_해시와_salt만_SP로_원문은_돌려준다()
    {
        var result = await _service.IssueSecretAsync(new ApiClientSecretRequest { ClientId = "erp-sync", RowVer = RowVer }, Ct);

        var p = _sp.LastCall(ApiClientAdminService.SecretSp).Parameters;
        Assert.Equal(43, result.ClientSecret.Length);
        Assert.DoesNotContain(p.Values, v => v is string text && text.Contains(result.ClientSecret, StringComparison.Ordinal));
        Assert.True(_hasher.Verify(result.ClientSecret, (byte[])p["P_SECRET_HASH"]!, (byte[])p["P_SECRET_SALT"]!));
        Assert.NotEqual(result.ClientSecret, (await _service.IssueSecretAsync(new ApiClientSecretRequest { ClientId = "erp-sync", RowVer = RowVer }, Ct)).ClientSecret);
    }

    [Fact]
    public async Task 비밀값발급은_RowVer가_없거나_ID가_이상하면_거부()
    {
        await Assert.ThrowsAsync<BizException>(() => _service.IssueSecretAsync(new ApiClientSecretRequest { ClientId = "x", RowVer = null }, Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.IssueSecretAsync(new ApiClientSecretRequest { ClientId = "a b", RowVer = RowVer }, Ct));
    }
}
