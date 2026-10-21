using System.Data;
using Portal.Data;
using Portal.Data.Internal;

namespace Portal.UnitTests.Data;

public sealed class SqlParameterFactoryTests
{
    [Fact]
    public void Create_null_DBNull()
    {
        var p = SqlParameterFactory.Create("P_X", null);

        Assert.Equal("@P_X", p.ParameterName);
        Assert.Equal(DBNull.Value, p.Value);
    }

    [Fact]
    public void Create_짧은문자열_NVarChar4000으로고정()
    {
        var p = SqlParameterFactory.Create("P_X", "abc");

        Assert.Equal(SqlDbType.NVarChar, p.SqlDbType);
        Assert.Equal(4000, p.Size);
    }

    [Fact]
    public void Create_긴문자열_NVarCharMax()
    {
        var p = SqlParameterFactory.Create("P_X", new string('a', 4001));

        Assert.Equal(-1, p.Size);
    }

    [Fact]
    public void Create_Tvp_Structured와형식이름()
    {
        var tvp = Tvp.From("FW.TVP_CODE", new[] { new { Code = "A" } });

        var p = SqlParameterFactory.Create("P_ROWS", tvp);

        Assert.Equal(SqlDbType.Structured, p.SqlDbType);
        Assert.Equal("FW.TVP_CODE", p.TypeName);
        Assert.Same(tvp.Table, p.Value);
    }

    [Fact]
    public void Create_DateOnly_Date형식()
    {
        var p = SqlParameterFactory.Create("P_DT", new DateOnly(2026, 10, 21));

        Assert.Equal(SqlDbType.Date, p.SqlDbType);
        Assert.Equal(new DateTime(2026, 10, 21), p.Value);
    }
}
