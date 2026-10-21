using System.Data;
using Portal.Data.Internal;

namespace Portal.UnitTests.Data;

public sealed class RowMapperTests
{
    public enum UserStat
    {
        Active,
        Locked,
    }

    public sealed class CodeDto
    {
        public string Code { get; set; } = "";

        public string? CodeNm { get; set; }

        public int SortNo { get; set; }

        public int? ParentSort { get; set; }

        public byte[]? RowVer { get; set; }
    }

    public sealed record UserRow(string UserId, UserStat UserStatCd, DateOnly? AccountEndDt, long LoginFailCnt);

    [Fact]
    public void For_설정가능속성_대문자스네이크컬럼매핑()
    {
        using var reader = Reader(
            ["CODE", "CODE_NM", "SORT_NO", "PARENT_SORT", "ROW_VER", "EXTRA_COL"],
            ["A01", "사용", 3, DBNull.Value, new byte[] { 1, 2 }, "무시"]);

        reader.Read();
        var row = RowMapper.For<CodeDto>(reader)(reader);

        Assert.Equal("A01", row.Code);
        Assert.Equal("사용", row.CodeNm);
        Assert.Equal(3, row.SortNo);
        Assert.Null(row.ParentSort);
        Assert.Equal([1, 2], row.RowVer);
    }

    [Fact]
    public void For_위치기반record_생성자매개변수로매핑_형식변환()
    {
        using var reader = Reader(
            ["USER_ID", "USER_STAT_CD", "ACCOUNT_END_DT", "LOGIN_FAIL_CNT"],
            ["u1", "LOCKED", new DateTime(2026, 12, 31), 2]);

        reader.Read();
        var row = RowMapper.For<UserRow>(reader)(reader);

        Assert.Equal(new UserRow("u1", UserStat.Locked, new DateOnly(2026, 12, 31), 2L), row);
    }

    [Fact]
    public void For_결과에없는컬럼_기본값()
    {
        using var reader = Reader(["CODE"], ["A"]);

        reader.Read();
        var row = RowMapper.For<CodeDto>(reader)(reader);

        Assert.Equal("A", row.Code);
        Assert.Equal(0, row.SortNo);
    }

    [Fact]
    public void For_스칼라형식_첫컬럼()
    {
        using var reader = Reader(["TOTAL_CNT"], [42L]);

        reader.Read();

        Assert.Equal(42, RowMapper.For<int>(reader)(reader));
    }

    [Fact]
    public void For_스칼라nullable_DBNull이면null()
    {
        using var reader = Reader(["X"], [DBNull.Value]);

        reader.Read();

        Assert.Null(RowMapper.For<int?>(reader)(reader));
    }

    private static DataTableReader Reader(string[] columns, object[] values)
    {
        var table = new DataTable { Locale = System.Globalization.CultureInfo.InvariantCulture };
        for (var i = 0; i < columns.Length; i++)
        {
            table.Columns.Add(columns[i], values[i] is DBNull ? typeof(object) : values[i].GetType());
        }

        table.Rows.Add(values);
        return table.CreateDataReader();
    }
}
