using System.Text.Json;
using Portal.Core.Results;

namespace Portal.UnitTests.Core;

public sealed class ApiResultTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Ok_데이터_성공형식()
    {
        var result = ApiResult.Ok(new { vendorCd = "V0001" });
        result.TraceId = "t-1";

        var json = JsonSerializer.Serialize(result, Web);

        Assert.Equal("""{"data":{"vendorCd":"V0001"},"success":true,"traceId":"t-1"}""", json);
    }

    [Fact]
    public void Ok_값형식데이터_0도내보낸다()
    {
        var json = JsonSerializer.Serialize(ApiResult.Ok(0), Web);

        Assert.Equal("""{"data":0,"success":true,"traceId":null}""", json);
    }

    [Fact]
    public void Fail_오류형식_data없음()
    {
        var result = ApiResult.Fail("MSG.DUP_CODE", "이미 사용 중인 코드입니다.");

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("MSG.DUP_CODE", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(doc.RootElement.TryGetProperty("data", out _));
    }

    [Fact]
    public void PageResult_전체페이지수()
    {
        var page = new PageResult<int> { Items = [], TotalCount = 101, PageNo = 1, PageSize = 50 };

        Assert.Equal(3, page.TotalPages);
    }
}
