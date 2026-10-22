using Portal.Framework.Web.Mvc;

namespace Portal.UnitTests.Web;

public sealed class GridRowsTests
{
    private sealed record Row(string RowStat) : IGridRow;

    [Fact]
    public void 행상태별필요권한_추가CREATE_수정UPDATE_삭제DELETE()
    {
        var funcs = RowStates.RequiredFuncs([new Row("I"), new Row("U"), new Row("U"), new Row("D")]);

        Assert.Equal(["CREATE", "DELETE", "UPDATE"], funcs!.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void 수정행만_UPDATE만() => Assert.Equal(["UPDATE"], RowStates.RequiredFuncs([new Row("U")])!);

    [Theory]
    [InlineData("")]
    [InlineData("X")]
    [InlineData("i")]
    public void 모르는행상태_null(string rowStat) => Assert.Null(RowStates.RequiredFuncs([new Row("U"), new Row(rowStat)]));
}
