using Portal.Data.Internal;
using Portal.Testing;

namespace Portal.UnitTests.Data;

public sealed class SpParameterBuilderTests
{
    [Fact]
    public void Build_익명객체_속성순서대로_공통파라미터는마지막()
    {
        var user = new FakeCurrentUser { UserId = "u1", LangCd = "en", ClientIp = "10.0.0.1" };

        var result = SpParameterBuilder.Build(new { P_GROUP_CD = "USER_STAT", P_PAGE_NO = 2 }, user);

        Assert.Equal(["P_GROUP_CD", "P_PAGE_NO", "P_USER_ID", "P_LANG_CD", "P_CLIENT_IP"], result.Select(p => p.Key));
        Assert.Equal(["USER_STAT", 2, "u1", "en", "10.0.0.1"], result.Select(p => p.Value));
    }

    [Fact]
    public void Build_호출자가사용자ID를보내도_현재사용자값으로덮어씀()
    {
        var user = new FakeCurrentUser { UserId = "real.user" };

        var result = SpParameterBuilder.Build(new { P_USER_ID = "sysadmin", P_LANG_CD = "zh-CN", P_CLIENT_IP = "1.1.1.1" }, user);

        Assert.Single(result, p => p.Key == "P_USER_ID");
        Assert.Equal("real.user", result.Single(p => p.Key == "P_USER_ID").Value);
        Assert.Equal("ko", result.Single(p => p.Key == "P_LANG_CD").Value);
        Assert.Equal("127.0.0.1", result.Single(p => p.Key == "P_CLIENT_IP").Value);
    }

    [Fact]
    public void Build_비로그인_사용자ID는null()
    {
        var result = SpParameterBuilder.Build(null, FakeCurrentUser.Anonymous());

        Assert.Null(result.Single(p => p.Key == "P_USER_ID").Value);
    }

    [Fact]
    public void Build_사전형식_지원()
    {
        var parameters = new Dictionary<string, object?> { ["P_CODE"] = "A" };

        var result = SpParameterBuilder.Build(parameters, new FakeCurrentUser());

        Assert.Equal("A", result.Single(p => p.Key == "P_CODE").Value);
    }

    [Fact]
    public void Build_규칙에맞지않는파라미터이름_예외() =>
        Assert.Throws<ArgumentException>(() => SpParameterBuilder.Build(new { GroupCd = "X" }, new FakeCurrentUser()));
}
