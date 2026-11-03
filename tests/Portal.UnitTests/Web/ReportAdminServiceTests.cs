using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Testing;
using Portal.Web.Areas.Rpt.Models;
using Portal.Web.Areas.Rpt.Services;

namespace Portal.UnitTests.Web;

/// <summary>레포트 관리 Service: 데이터 SP 이름 형식(스키마.USP_RPT_…_GET_LIST만), 양식 뷰 존재, 조건 정의 형식, 값 범위를 SP 호출 전에 거절한다.</summary>
public sealed class ReportAdminServiceTests
{
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 2];
    private readonly FakeSpExecutor _sp = new();
    private readonly ReportAdminService _service;

    public ReportAdminServiceTests()
    {
        _sp.Returns(ReportAdminService.SaveSp, new SaveResult(1, null));
        _service = new ReportAdminService(_sp, new Templates());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ReportSaveRow Row(string rowStat = "I", string id = "RPT_SALES", string sp = "LOG.USP_RPT_SALES_GET_LIST") =>
        new() { RowStat = rowStat, ReportId = id, DataSp = sp, RowVer = rowStat == "I" ? null : RowVer };

    private static ReportSaveRequest Request(params ReportSaveRow[] rows) => new() { Rows = [.. rows] };

    [Fact]
    public async Task SaveAsync_정상_행은_SP에_JSON으로_넘긴다()
    {
        var row = Row("U");
        row.TemplateView = " Screen ";
        row.ParamsJson = """[{"name":"P_MONTH","type":"MONTH","required":true,"labelKey":"LBL.MONTH","default":"PREV_MONTH"}]""";

        await _service.SaveAsync(Request(row), Ct);

        var json = JsonDocument.Parse((string)_sp.LastCall(ReportAdminService.SaveSp).Parameters["P_ROWS_JSON"]!).RootElement;
        Assert.Equal("Screen", json[0].GetProperty("templateView").GetString());
        Assert.Equal("0000000000000002", json[0].GetProperty("rowVer").GetString());
    }

    [Theory]
    [InlineData("LOG.USP_RPT_X_GET_LIST;DROP TABLE A")]
    [InlineData("USP_RPT_X_GET_LIST")]
    [InlineData("LOG.USP_USER_GET_LIST")]
    [InlineData("LOG.[USP_RPT_X_GET_LIST]")]
    [InlineData("A.B.USP_RPT_X_GET_LIST")]
    [InlineData("LOG.USP_RPT__GET_LIST ")]
    public async Task SaveAsync_데이터SP_형식이_아니면_거절한다(string sp)
    {
        if (sp.EndsWith(' '))
        {
            // 앞뒤 공백은 지우지만 대상 이름이 비면 형식이 아니다
            sp = "LOG.USP_RPT__GET_LIST ";
        }

        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row(sp: sp)), Ct));
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == ReportAdminService.SaveSp);
    }

    [Fact]
    public async Task SaveAsync_없는_양식_잘못된_조건_범위밖_값을_거절한다()
    {
        var noTemplate = Row();
        noTemplate.TemplateView = "Missing";
        var badTemplate = Row();
        badTemplate.TemplateView = "../Secret";
        var badParams = Row();
        badParams.ParamsJson = """[{"name":"P_USER_ID","type":"TEXT"}]""";
        var notJson = Row();
        notJson.ParamsJson = "not json";
        var badOrient = Row();
        badOrient.OrientCd = "X";
        var badMax = Row();
        badMax.MaxRows = 0;
        var badId = Row(id: "bad id");

        foreach (var row in new[] { noTemplate, badTemplate, badParams, notJson, badOrient, badMax, badId })
        {
            var ex = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(row), Ct));
            Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
        }
    }

    [Fact]
    public async Task SaveAsync_삭제는_RowVer만_있으면_되고_수정_삭제의_RowVer_누락과_중복은_거절한다()
    {
        var del = new ReportSaveRow { RowStat = "D", ReportId = "RPT_SALES", RowVer = RowVer };
        await _service.SaveAsync(Request(del), Ct);

        var noVer = Row("U");
        noVer.RowVer = null;
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(noVer), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row(), Row()), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(), Ct));
    }

    [Fact]
    public async Task GetListAsync_목록과_등록할_프로그램을_돌려준다()
    {
        _sp.Returns(ReportAdminService.ListSp, new object[]
        {
            new List<ReportAdminDto> { new() { ReportId = "RPT_A" } },
            new List<ReportProgramOption> { new() { ProgramId = "RPT_B" } },
            null!,
        });

        var result = await _service.GetListAsync(new ReportAdminSearch { Keyword = " a " }, Ct);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("RPT_B", result.Programs[0].ProgramId);
        Assert.Equal("a", _sp.LastCall(ReportAdminService.ListSp).Parameters["P_KEYWORD"]);
    }

    private sealed class Templates : IReportTemplateCatalog
    {
        public bool Exists(string templateView) => templateView is "Default" or "Screen" or "AccessMonthly";
    }
}
