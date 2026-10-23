using Portal.Core.Errors;
using Portal.Web.Areas.Rpt.Models;
using Portal.Web.Areas.Rpt.Services;

namespace Portal.UnitTests.Rpt;

public sealed class ReportParamsTests
{
    private const string Json = """
        [{"name":"P_MONTH","type":"month","required":true,"labelKey":"LBL.MONTH","default":"PREV_MONTH"},
         {"name":"P_FROM","type":"DATE","labelKey":"LBL.START_DT","default":"today"},
         {"name":"P_KEYWORD","type":"TEXT","maxLength":3}]
        """;

    private static string Label(string key) => "[" + key + "]";

    [Fact]
    public void Parse_형식은대문자로_기본값_라벨없으면이름_길이기본100()
    {
        var p = ReportParams.Parse(Json);

        Assert.Equal(new ReportParam("P_MONTH", "MONTH", true, "LBL.MONTH", "PREV_MONTH", 100), p[0]);
        Assert.Equal(("DATE", "TODAY"), (p[1].Type, p[1].Default));
        Assert.Equal(("P_KEYWORD", 3), (p[2].LabelKey, p[2].MaxLength));
        Assert.Empty(ReportParams.Parse(null));
    }

    [Theory]
    [InlineData("""[{"name":"P_USER_ID","type":"TEXT"}]""")]
    [InlineData("""[{"name":"P_MAX_ROWS","type":"TEXT"}]""")]
    [InlineData("""[{"name":"month","type":"TEXT"}]""")]
    [InlineData("""[{"name":"P_A","type":"NUMBER"}]""")]
    [InlineData("""[{"name":"P_A","type":"DATE","default":"TOMORROW"}]""")]
    [InlineData("""[{"name":"P_A","type":"DATE"},{"name":"P_A","type":"DATE"}]""")]
    [InlineData("""{"name":"P_A"}""")]
    public void Parse_잘못된정의는_등록오류(string json)
    {
        Assert.Throws<InvalidOperationException>(() => ReportParams.Parse(json));
    }

    [Fact]
    public void Defaults_오늘기준_전월_이번달_오늘()
    {
        var p = ReportParams.Parse("""
            [{"name":"P_A","type":"MONTH","default":"PREV_MONTH"},{"name":"P_B","type":"MONTH","default":"THIS_MONTH"},
             {"name":"P_C","type":"DATE","default":"TODAY"},{"name":"P_D","type":"DATE","default":"PREV_MONTH"},{"name":"P_E","type":"TEXT"}]
            """);

        var d = ReportParams.Defaults(p, new DateOnly(2026, 1, 15));

        Assert.Equal(("2025-12", "2026-01", "2026-01-15", "2025-12-01"), (d["P_A"], d["P_B"], d["P_C"], d["P_D"]));
        Assert.Null(d["P_E"]);
    }

    [Fact]
    public void Bind_형식변환_공백제거_빈선택값은null_조건은입력한것만()
    {
        var (sp, conditions) = ReportParams.Bind(
            ReportParams.Parse(Json),
            new Dictionary<string, string?> { ["P_MONTH"] = " 2026-09 ", ["P_FROM"] = "2026-09-03", ["P_KEYWORD"] = "", ["P_OTHER"] = "x" },
            Label);

        Assert.Equal(
            [new("P_MONTH", "2026-09"), new("P_FROM", new DateOnly(2026, 9, 3)), new("P_KEYWORD", null)],
            sp);
        Assert.Equal([("[LBL.MONTH]", "2026-09"), ("[LBL.START_DT]", "2026-09-03")], conditions);
    }

    [Theory]
    [InlineData(null, "2026-09-01", "VAL.REQUIRED")]
    [InlineData("2026-9", null, "RPT.PARAM_INVALID")]
    [InlineData("2026-00", null, "RPT.PARAM_INVALID")]
    [InlineData("2026-09", "2026-02-30", "RPT.PARAM_INVALID")]
    [InlineData("2026-09", "09/01/2026", "RPT.PARAM_INVALID")]
    public void Bind_필수없음_형식오류는_조건이름과함께업무오류(string? month, string? from, string key)
    {
        var ex = Assert.Throws<BizException>(() => ReportParams.Bind(
            ReportParams.Parse(Json), new Dictionary<string, string?> { ["P_MONTH"] = month, ["P_FROM"] = from }, Label));

        Assert.Equal(key, ex.MessageKey);
        Assert.StartsWith("[LBL.", (string)ex.Args[0]!, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_글자는최대길이까지()
    {
        var p = ReportParams.Parse(Json);

        ReportParams.Bind(p, new Dictionary<string, string?> { ["P_MONTH"] = "2026-09", ["P_KEYWORD"] = "abc" }, Label);
        var ex = Assert.Throws<BizException>(() => ReportParams.Bind(p, new Dictionary<string, string?> { ["P_MONTH"] = "2026-09", ["P_KEYWORD"] = "abcd" }, Label));

        Assert.Equal("[P_KEYWORD]", ex.Args[0]);
    }
}
