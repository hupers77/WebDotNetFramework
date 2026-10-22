using Portal.Core.Security;
using System.Globalization;
using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Data;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>설정 관리 (<c>SYS_CONFIG_MGT</c>). 값만 바꾸고(키는 초기 데이터가 관리), 형식 검사·비밀값 암호화는 여기서 한다.</summary>
public interface IConfigAdminService
{
    /// <summary>설정 목록 (SECRET 값은 빠져 있다).</summary>
    Task<PageResult<ConfigDto>> GetListAsync(ConfigSearch search, CancellationToken cancellationToken = default);

    /// <summary>설정 하나의 변경 이력 (최근 순).</summary>
    Task<PageResult<ConfigHistDto>> GetHistoryAsync(string configKey, CancellationToken cancellationToken = default);

    /// <summary>바뀐 설정을 한 번에 저장한다. 저장 후 몇 초 안에 모든 서버의 설정 캐시가 바뀐다.</summary>
    Task<ConfigSaveResult> SaveAsync(ConfigSaveRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ConfigAdminService(ISpExecutor sp, ISecretProtector secrets) : IConfigAdminService
{
    /// <summary>한 번에 읽고 저장하는 최대 행 수.</summary>
    public const int MaxRows = 500;

    internal const string ListSp = "FW.USP_CONFIG_ADMIN_GET_LIST";
    internal const string HistorySp = "FW.USP_CONFIG_HIST_GET_LIST";
    internal const string SaveSp = "FW.USP_CONFIG_SAVE";

    private const int HistoryPageSize = 100;
    private const int ValueMaxLength = 2000;

    /// <summary>비밀값 평문 최대 길이 (암호화하면 길이가 늘어나 <c>CONFIG_VAL</c> 2000자 안에 들어가야 한다).</summary>
    private const int SecretMaxLength = 500;

    /// <inheritdoc />
    public Task<PageResult<ConfigDto>> GetListAsync(ConfigSearch search, CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<ConfigDto>(
            ListSp,
            new { P_CATEGORY_CD = NullIfBlank(search.CategoryCd), P_KEYWORD = NullIfBlank(search.Keyword), P_PAGE_NO = 1, P_PAGE_SIZE = MaxRows },
            cancellationToken);

    /// <inheritdoc />
    public Task<PageResult<ConfigHistDto>> GetHistoryAsync(string configKey, CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<ConfigHistDto>(HistorySp, new { P_CONFIG_KEY = configKey, P_PAGE_NO = 1, P_PAGE_SIZE = HistoryPageSize }, cancellationToken);

    /// <inheritdoc />
    public async Task<ConfigSaveResult> SaveAsync(ConfigSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Rows.Count == 0)
        {
            throw new BizException(MessageKeys.NoChanges);
        }

        // 형식·재시작 여부는 화면이 보낸 값이 아니라 DB 정의를 따른다
        var current = (await GetListAsync(new ConfigSearch(), cancellationToken)).Items.ToDictionary(c => c.ConfigKey, StringComparer.Ordinal);
        var rows = Validate(request, current);
        var saved = await sp.ExecuteAsync(SaveSp, new { P_ROWS = Tvp.From("FW.TVP_CONFIG", rows) }, cancellationToken);
        return new ConfigSaveResult(saved.AffectedCnt, rows.Any(r => current[r.ConfigKey].RestartYn == "Y"));
    }

    internal List<ConfigTvpRow> Validate(ConfigSaveRequest request, IReadOnlyDictionary<string, ConfigDto> current)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<ConfigTvpRow>(request.Rows.Count);
        foreach (var row in request.Rows)
        {
            if (row.RowStat != RowStates.Updated || row.RowVer is not { Length: 8 } || !seen.Add(row.ConfigKey))
            {
                throw new BizException(MessageKeys.InvalidInput);
            }

            if (!current.TryGetValue(row.ConfigKey, out var config))
            {
                throw new BizException(MessageKeys.NotFound);
            }

            rows.Add(new ConfigTvpRow { ConfigKey = row.ConfigKey, ConfigVal = Normalize(config, row.ConfigVal), RowVer = row.RowVer });
        }

        return rows;
    }

    /// <summary>형식별 검사. 정수·Y/N은 비울 수 없고, 문자열·JSON은 비우면 값 없음(null)이다.</summary>
    private string? Normalize(ConfigDto config, string? input)
    {
        var key = config.ConfigKey;
        var value = input?.Trim();
        switch (config.ValTypeCd)
        {
            case ConfigValueTypes.Secret:
                if (string.IsNullOrEmpty(input))
                {
                    throw new BizException(ValRequired, key);
                }

                if (input.Length > SecretMaxLength)
                {
                    throw new BizException(ValMaxLength, key, SecretMaxLength);
                }

                // 비밀값은 앞뒤 공백도 값일 수 있으므로 그대로 암호화한다
                return secrets.Protect(input);

            case ConfigValueTypes.Number:
                if (string.IsNullOrEmpty(value))
                {
                    throw new BizException(ValRequired, key);
                }

                return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    ? number.ToString(CultureInfo.InvariantCulture)
                    : throw new BizException(ValConfigInt, key);

            case ConfigValueTypes.Bool:
                return value is "Y" or "N" ? value : throw new BizException(ValConfigBool, key);

            case ConfigValueTypes.Json:
                if (string.IsNullOrEmpty(value))
                {
                    return null;
                }

                try
                {
                    using var _ = JsonDocument.Parse(value);
                }
                catch (JsonException)
                {
                    throw new BizException(ValConfigJson, key);
                }

                return value.Length <= ValueMaxLength ? value : throw new BizException(ValMaxLength, key, ValueMaxLength);

            default:
                return string.IsNullOrEmpty(value) ? null
                    : value.Length <= ValueMaxLength ? value
                    : throw new BizException(ValMaxLength, key, ValueMaxLength);
        }
    }

    private const string ValRequired = "VAL.REQUIRED";
    private const string ValMaxLength = "VAL.MAX_LENGTH";
    private const string ValConfigInt = "VAL.CONFIG_INT";
    private const string ValConfigBool = "VAL.CONFIG_BOOL";
    private const string ValConfigJson = "VAL.CONFIG_JSON";

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary><c>FW.TVP_CONFIG</c> 컬럼 순서.</summary>
    internal sealed class ConfigTvpRow
    {
        public string ConfigKey { get; set; } = "";

        public string? ConfigVal { get; set; }

        public byte[] RowVer { get; set; } = [];
    }
}
