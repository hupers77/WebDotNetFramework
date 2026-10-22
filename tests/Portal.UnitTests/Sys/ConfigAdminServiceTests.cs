using Microsoft.AspNetCore.DataProtection;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Data;
using Portal.Security.Secrets;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.UnitTests.Sys;

/// <summary>설정 관리 Service: 형식별 검사, 비밀값 암호화, 재시작 안내 (형식은 화면이 아니라 DB 정의를 따른다).</summary>
public sealed class ConfigAdminServiceTests
{
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 1];
    private readonly FakeSpExecutor _sp = new();
    private readonly DataProtectionSecretProtector _secrets = new(new EphemeralDataProtectionProvider());
    private readonly ConfigAdminService _service;

    private readonly Dictionary<string, ConfigDto> _current = new[]
    {
        Config("SITE.NAME", ConfigValueTypes.Text),
        Config("PWD.LOCK_COUNT", ConfigValueTypes.Number),
        Config("AUTH.AD_YN", ConfigValueTypes.Bool, restart: "Y"),
        Config("UI.THEME", ConfigValueTypes.Json),
        Config("MAIL.SMTP_PASSWORD", ConfigValueTypes.Secret),
    }.ToDictionary(c => c.ConfigKey, StringComparer.Ordinal);

    public ConfigAdminServiceTests()
    {
        _service = new ConfigAdminService(_sp, _secrets);
        _sp.Returns(ConfigAdminService.ListSp, new PageResult<ConfigDto> { Items = [.. _current.Values], TotalCount = _current.Count, PageNo = 1, PageSize = ConfigAdminService.MaxRows });
        _sp.Returns(ConfigAdminService.SaveSp, p => new SaveResult(((Tvp)p["P_ROWS"]!).Table.Rows.Count, null));
    }

    private static ConfigDto Config(string key, string type, string restart = "N") =>
        new() { ConfigKey = key, ValTypeCd = type, RestartYn = restart, CategoryCd = key.Split('.')[0], RowVer = RowVer };

    private static ConfigSaveRow Row(string key, string? value) => new() { RowStat = "U", ConfigKey = key, ConfigVal = value, RowVer = RowVer };

    private static ConfigSaveRequest Request(params ConfigSaveRow[] rows) => new() { Rows = [.. rows] };

    private BizException ErrorOf(params ConfigSaveRow[] rows) => Assert.Throws<BizException>(() => _service.Validate(Request(rows), _current));

    private string? Normalized(string key, string? value) => Assert.Single(_service.Validate(Request(Row(key, value)), _current)).ConfigVal;

    [Theory]
    [InlineData("SITE.NAME", " 포털 ", "포털")]
    [InlineData("SITE.NAME", "  ", null)]
    [InlineData("PWD.LOCK_COUNT", " 007 ", "7")]
    [InlineData("AUTH.AD_YN", "Y", "Y")]
    [InlineData("UI.THEME", "{\"a\":1}", "{\"a\":1}")]
    [InlineData("UI.THEME", "", null)]
    public void 형식별_정리(string key, string? input, string? expected) => Assert.Equal(expected, Normalized(key, input));

    [Theory]
    [InlineData("PWD.LOCK_COUNT", "", "VAL.REQUIRED")]
    [InlineData("PWD.LOCK_COUNT", "-1", "VAL.CONFIG_INT")]
    [InlineData("PWD.LOCK_COUNT", "1.5", "VAL.CONFIG_INT")]
    [InlineData("PWD.LOCK_COUNT", "99999999999", "VAL.CONFIG_INT")]
    [InlineData("AUTH.AD_YN", "y", "VAL.CONFIG_BOOL")]
    [InlineData("AUTH.AD_YN", null, "VAL.CONFIG_BOOL")]
    [InlineData("UI.THEME", "{a:1}", "VAL.CONFIG_JSON")]
    [InlineData("MAIL.SMTP_PASSWORD", "", "VAL.REQUIRED")]
    public void 형식오류_설정키를인자로(string key, string? input, string messageKey)
    {
        var error = ErrorOf(Row(key, input));

        Assert.Equal(messageKey, error.MessageKey);
        Assert.Equal(key, error.Args[0]);
    }

    [Fact]
    public void 길이초과()
    {
        Assert.Equal("VAL.MAX_LENGTH", ErrorOf(Row("SITE.NAME", new string('a', 2001))).MessageKey);
        Assert.Equal("VAL.MAX_LENGTH", ErrorOf(Row("MAIL.SMTP_PASSWORD", new string('a', 501))).MessageKey);
    }

    [Fact]
    public void 비밀값_공백포함그대로_암호화해저장()
    {
        var stored = Normalized("MAIL.SMTP_PASSWORD", " p@ss ");

        Assert.NotNull(stored);
        Assert.DoesNotContain("p@ss", stored, StringComparison.Ordinal);
        Assert.Equal(" p@ss ", _secrets.Unprotect(stored));
    }

    [Theory]
    [InlineData("I")]
    [InlineData("D")]
    [InlineData("")]
    public void 수정행만_받는다(string rowStat) =>
        Assert.Equal(MessageKeys.InvalidInput, ErrorOf(new ConfigSaveRow { RowStat = rowStat, ConfigKey = "SITE.NAME", ConfigVal = "a", RowVer = RowVer }).MessageKey);

    [Fact]
    public void 같은키두번_ROW_VER없음_입력오류()
    {
        Assert.Equal(MessageKeys.InvalidInput, ErrorOf(Row("SITE.NAME", "a"), Row("SITE.NAME", "b")).MessageKey);
        Assert.Equal(MessageKeys.InvalidInput, ErrorOf(new ConfigSaveRow { RowStat = "U", ConfigKey = "SITE.NAME", ConfigVal = "a" }).MessageKey);
    }

    [Fact]
    public void 없는키_NOT_FOUND() => Assert.Equal(MessageKeys.NotFound, ErrorOf(Row("NO.SUCH", "a")).MessageKey);

    [Fact]
    public async Task SaveAsync_변경행없음_SP호출안함()
    {
        var error = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(MessageKeys.NoChanges, error.MessageKey);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == ConfigAdminService.SaveSp);
    }

    [Fact]
    public async Task SaveAsync_TVP로보내고_재시작설정포함여부를알려준다()
    {
        var ct = TestContext.Current.CancellationToken;

        var plain = await _service.SaveAsync(Request(Row("SITE.NAME", "포털")), ct);
        var restart = await _service.SaveAsync(Request(Row("SITE.NAME", "포털"), Row("AUTH.AD_YN", "N")), ct);

        Assert.Equal(new ConfigSaveResult(1, false), plain);
        Assert.Equal(new ConfigSaveResult(2, true), restart);
        var table = ((Tvp)_sp.LastCall(ConfigAdminService.SaveSp).Parameters["P_ROWS"]!).Table;
        Assert.Equal("FW.TVP_CONFIG", ((Tvp)_sp.LastCall(ConfigAdminService.SaveSp).Parameters["P_ROWS"]!).TypeName);
        Assert.Equal(["CONFIG_KEY", "CONFIG_VAL", "ROW_VER"], table.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName));
    }
}
