using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.UnitTests.Web;

/// <summary>메시지 템플릿 Service: 채널·기본 언어 필수, 제목 줄바꿈 금지, 본문 스크립트·이벤트 속성 금지, 목록에 언어별 문구 모으기.</summary>
public sealed class MsgTemplateAdminServiceTests : IAsyncLifetime
{
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 3];
    private readonly FakeSpExecutor _sp = new();
    private MsgTemplateAdminService _service = null!;

    public async ValueTask InitializeAsync()
    {
        _sp.Returns(MsgTemplateAdminService.SaveSp, new SaveResult(1, null));
        _service = new MsgTemplateAdminService(_sp, await NewScreensTestSupport.LangsAsync(_sp));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MsgTemplateSaveRequest Request(string subject = " 안내 ", string body = "<p>{{USER_NM}}님</p>", string mail = "Y", string portal = "N") => new()
    {
        TemplateId = "TPL_X", RowVer = RowVer, MailYn = mail, PortalYn = portal, UseYn = "Y",
        Langs = [new() { LangCd = "ko", Subject = subject, Body = body }],
    };

    [Fact]
    public async Task SaveAsync_제목본문_공백을_빼고_SP에_JSON으로_넘긴다()
    {
        await _service.SaveAsync(Request(), Ct);

        var args = _sp.LastCall(MsgTemplateAdminService.SaveSp).Parameters;
        var json = JsonDocument.Parse((string)args["P_LANGS_JSON"]!).RootElement;
        Assert.Equal("안내", json[0].GetProperty("subject").GetString());
        Assert.Equal(RowVer, args["P_ROW_VER"]);
    }

    [Theory]
    [InlineData("제목\r\n헤더: x", "<p>a</p>")]
    [InlineData("제목", "<script>alert(1)</script>")]
    [InlineData("제목", "<IFRAME src=x></IFRAME>")]
    [InlineData("제목", "<img src=x onerror=alert(1)>")]
    [InlineData("제목", "<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("", "<p>a</p>")]
    [InlineData("제목", "  ")]
    public async Task SaveAsync_위험한_제목본문은_SP를_부르지_않고_거절한다(string subject, string body)
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(subject, body), Ct));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == MsgTemplateAdminService.SaveSp);
    }

    [Fact]
    public async Task SaveAsync_채널_기본언어_RowVer_쓰지않는_언어를_확인한다()
    {
        var noChannel = Request(mail: "N", portal: "N");
        var noDefault = Request();
        noDefault.Langs[0].LangCd = "en";
        var noVer = Request();
        noVer.RowVer = null;
        var badLang = Request();
        badLang.Langs.Add(new() { LangCd = "zh-CN", Subject = "s", Body = "b" });

        foreach (var request in new[] { noChannel, noDefault, noVer, badLang })
        {
            await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(request, Ct));
        }
    }

    [Fact]
    public async Task GetListAsync_언어별_제목본문을_템플릿에_붙인다()
    {
        _sp.Returns(MsgTemplateAdminService.ListSp, new object[]
        {
            new List<MsgTemplateDto> { new() { TemplateId = "T1" }, new() { TemplateId = "T2" } },
            new List<MsgTemplateLangDto> { new() { TemplateId = "T1", LangCd = "ko", Subject = "s", Body = "b" } },
        });

        var items = await _service.GetListAsync(new MsgTemplateSearch(), Ct);

        Assert.Single(items[0].Langs);
        Assert.Empty(items[1].Langs);
    }
}
