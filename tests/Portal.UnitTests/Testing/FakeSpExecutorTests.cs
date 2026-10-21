using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Data;
using Portal.Testing;

namespace Portal.UnitTests.Testing;

/// <summary>Service 단위 테스트에서 FakeSpExecutor를 쓰는 방법의 예이기도 하다.</summary>
public sealed class FakeSpExecutorTests
{
    private sealed record CodeDto(string Code, string CodeNm);

    [Fact]
    public async Task QueryPageAsync_등록한결과반환_호출파라미터기록()
    {
        var page = new PageResult<CodeDto> { Items = [new("A", "에이")], TotalCount = 1, PageNo = 1, PageSize = 50 };
        var sp = new FakeSpExecutor().Returns("FW.USP_CODE_GET_LIST", page);

        var result = await sp.QueryPageAsync<CodeDto>("FW.USP_CODE_GET_LIST", new { P_GROUP_CD = "USER_STAT", P_PAGE_NO = 1, P_PAGE_SIZE = 50 });

        Assert.Same(page, result);
        Assert.Equal("USER_STAT", sp.LastCall("FW.USP_CODE_GET_LIST").Parameters["P_GROUP_CD"]);
    }

    [Fact]
    public async Task ExecuteAsync_예외등록_그대로던짐()
    {
        var sp = new FakeSpExecutor().Throws("FW.USP_CODE_SAVE", new BizException(MessageKeys.DupCode));

        var ex = await Assert.ThrowsAsync<BizException>(() => sp.ExecuteAsync("FW.USP_CODE_SAVE"));

        Assert.Equal(MessageKeys.DupCode, ex.MessageKey);
    }

    [Fact]
    public async Task QueryMultipleAsync_결과셋순서대로()
    {
        var sp = new FakeSpExecutor().Returns("FW.USP_CODE_GROUP_GET", new object[] { new CodeDto("G", "그룹"), new[] { new CodeDto("A", "에이") } });

        var (group, codes) = await sp.QueryMultipleAsync("FW.USP_CODE_GROUP_GET", null, async r =>
            (await r.ReadSingleAsync<CodeDto>(), await r.ReadAsync<CodeDto>()));

        Assert.Equal("G", group!.Code);
        Assert.Single(codes);
    }

    [Fact]
    public async Task BeginTransactionAsync_커밋횟수기록()
    {
        var sp = new FakeSpExecutor();

        await using (var tx = await sp.BeginTransactionAsync())
        {
            await tx.CommitAsync();
        }

        Assert.Equal(1, sp.CommitCount);
    }
}
