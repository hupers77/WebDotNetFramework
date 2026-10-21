using Portal.Data;

namespace Portal.UnitTests.Data;

public sealed class TvpTests
{
    public sealed class CodeRow
    {
        public string RowStat { get; set; } = "I";

        public string Code { get; set; } = "";

        public string? CodeNm { get; set; }

        public int? SortNo { get; set; }

        public byte[]? RowVer { get; set; }
    }

    [Fact]
    public void From_속성선언순서로컬럼생성_null은DBNull()
    {
        var tvp = Tvp.From("FW.TVP_CODE", [new CodeRow { RowStat = "U", Code = "A", SortNo = null }]);

        Assert.Equal("FW.TVP_CODE", tvp.TypeName);
        Assert.Equal(["ROW_STAT", "CODE", "CODE_NM", "SORT_NO", "ROW_VER"], tvp.Table.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName));
        Assert.Equal(typeof(int), tvp.Table.Columns["SORT_NO"]!.DataType);
        Assert.Equal("U", tvp.Table.Rows[0]["ROW_STAT"]);
        Assert.Equal(DBNull.Value, tvp.Table.Rows[0]["SORT_NO"]);
    }

    [Fact]
    public void From_컬럼직접지정_지정순서대로()
    {
        var tvp = Tvp.From("FW.TVP_CODE", [new CodeRow { Code = "A", CodeNm = "에이" }], "CODE", "ROW_STAT", "CODE_NM");

        Assert.Equal(["CODE", "ROW_STAT", "CODE_NM"], tvp.Table.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName));
        Assert.Equal("에이", tvp.Table.Rows[0]["CODE_NM"]);
    }

    [Fact]
    public void From_없는컬럼지정_예외() =>
        Assert.Throws<ArgumentException>(() => Tvp.From("FW.TVP_CODE", Array.Empty<CodeRow>(), "NO_SUCH"));

    [Fact]
    public void From_형식이름규칙위반_예외() =>
        Assert.Throws<ArgumentException>(() => Tvp.From("dbo.CodeTable", Array.Empty<CodeRow>()));
}
