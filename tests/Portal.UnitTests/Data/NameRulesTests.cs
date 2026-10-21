using Portal.Data.Internal;

namespace Portal.UnitTests.Data;

public sealed class NameRulesTests
{
    [Theory]
    [InlineData("CODE_NM", "CODENM")]
    [InlineData("CodeNm", "CODENM")]
    [InlineData("ROW_VER", "ROWVER")]
    [InlineData("attr1", "ATTR1")]
    public void Normalize_컬럼과속성이름_같은키(string name, string expected) =>
        Assert.Equal(expected, NameRules.Normalize(name));

    [Theory]
    [InlineData("CodeNm", "CODE_NM")]
    [InlineData("RowVer", "ROW_VER")]
    [InlineData("RowStat", "ROW_STAT")]
    [InlineData("Attr1", "ATTR1")]
    [InlineData("UserId", "USER_ID")]
    public void ToUpperSnake_PascalCase_대문자스네이크(string pascal, string expected) =>
        Assert.Equal(expected, NameRules.ToUpperSnake(pascal));

    [Theory]
    [InlineData("FW.USP_CODE_GET_LIST")]
    [InlineData("BRD.USP_POST_SAVE")]
    [InlineData("LOG.USP_AUDIT_WRITE")]
    public void EnsureSpName_규칙에맞는이름_통과(string spName) => NameRules.EnsureSpName(spName);

    [Theory]
    [InlineData("USP_CODE_GET_LIST")]
    [InlineData("fw.usp_code_get_list")]
    [InlineData("FW.SP_CODE")]
    [InlineData("FW.USP_CODE; DROP TABLE FW.TB_USER")]
    [InlineData("FW.USP_CODE--")]
    [InlineData("[FW].[USP_CODE]")]
    public void EnsureSpName_규칙위반_예외(string spName) =>
        Assert.Throws<ArgumentException>(() => NameRules.EnsureSpName(spName));

    [Theory]
    [InlineData("P_GROUP_CD")]
    [InlineData("P_ROWS")]
    public void EnsureParameterName_P접두어대문자_통과(string name) => NameRules.EnsureParameterName(name);

    [Theory]
    [InlineData("GroupCd")]
    [InlineData("@P_GROUP_CD")]
    [InlineData("p_group_cd")]
    public void EnsureParameterName_규칙위반_예외(string name) =>
        Assert.Throws<ArgumentException>(() => NameRules.EnsureParameterName(name));
}
