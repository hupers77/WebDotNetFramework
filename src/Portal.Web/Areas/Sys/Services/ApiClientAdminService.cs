using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Data;
using Portal.Framework.Web.Mvc;
using Portal.Security.Passwords;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>API 클라이언트 관리 (<c>SYS_API_CLIENT_MGT</c>). 클라이언트 설정을 한 번에 저장하고, 비밀값은 발급 때 한 번만 보여 준다.</summary>
public interface IApiClientAdminService
{
    /// <summary>클라이언트 목록.</summary>
    Task<PageResult<ApiClientDto>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>바뀐 행을 한 번에 저장한다.</summary>
    Task<ApiClientSaveResult> SaveAsync(ApiClientSaveRequest request, CancellationToken cancellationToken = default);

    /// <summary>비밀값을 새로 만들어 해시만 저장하고 원문을 돌려준다 (이전 비밀값은 바로 무효).</summary>
    Task<ApiClientSecretResult> IssueSecretAsync(ApiClientSecretRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed partial class ApiClientAdminService(ISpExecutor sp, IPasswordHasher hasher) : IApiClientAdminService
{
    internal const string ListSp = "FW.USP_API_CLIENT_ADMIN_GET_LIST";
    internal const string SaveSp = "FW.USP_API_CLIENT_SAVE";
    internal const string SecretSp = "FW.USP_API_CLIENT_SECRET_SET";
    internal const string MsgInvalidCode = "VAL.API_CODE";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public Task<PageResult<ApiClientDto>> GetListAsync(CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<ApiClientDto>(ListSp, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<ApiClientSaveResult> SaveAsync(ApiClientSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Rows.Count == 0)
        {
            throw new BizException(MessageKeys.NoChanges);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<object>(request.Rows.Count);
        foreach (var row in request.Rows)
        {
            if (!ClientIdPattern().IsMatch(row.ClientId))
            {
                throw new BizException(MessageKeys.InvalidInput);
            }

            if (!seen.Add(row.ClientId))
            {
                throw new BizException("VAL.DUP_ROW", row.ClientId);
            }

            var needsVer = row.RowStat is RowStates.Updated or RowStates.Deleted;
            var editing = row.RowStat is RowStates.Inserted or RowStates.Updated;
            if (row.RowStat is not (RowStates.Inserted or RowStates.Updated or RowStates.Deleted)
                || (needsVer && row.RowVer is not { Length: 8 })
                || (editing && (string.IsNullOrWhiteSpace(row.ClientNm) || row.UseYn is not ("Y" or "N"))))
            {
                throw new BizException(MessageKeys.InvalidInput);
            }

            rows.Add(new
            {
                row.RowStat,
                row.ClientId,
                ClientNm = row.ClientNm?.Trim(),
                ApiCodes = editing ? NormalizeCodes(row.ApiCodes) : null,
                AllowIpList = editing ? CompanyAdminService.NormalizeIpList(row.AllowIpList) : null,
                row.RatePerMin,
                ExpireDt = editing ? NormalizeDate(row.ExpireDt) : null,
                row.UseYn,
                RowVer = row.RowVer is null ? null : Convert.ToHexString(row.RowVer),
            });
        }

        var saved = await sp.ExecuteAsync(SaveSp, new { P_ROWS_JSON = JsonSerializer.Serialize(rows, Json) }, cancellationToken);
        return new ApiClientSaveResult(saved.AffectedCnt);
    }

    /// <inheritdoc />
    public async Task<ApiClientSecretResult> IssueSecretAsync(ApiClientSecretRequest request, CancellationToken cancellationToken = default)
    {
        if (!ClientIdPattern().IsMatch(request.ClientId) || request.RowVer is not { Length: 8 })
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var secret = GenerateSecret();
        var hash = hasher.Hash(secret);
        await sp.ExecuteAsync(SecretSp, new
        {
            P_CLIENT_ID = request.ClientId,
            P_ROW_VER = request.RowVer,
            P_SECRET_HASH = hash.Hash,
            P_SECRET_SALT = hash.Salt,
        }, cancellationToken);
        return new ApiClientSecretResult(request.ClientId, secret);
    }

    /// <summary>비밀값: 무작위 32바이트를 URL에 안전한 Base64로 (43자).</summary>
    internal static string GenerateSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>허용 코드 목록을 검사하고 <c>A,B</c> 형태로 맞춘다 (대문자·숫자·밑줄만, 중복 제거). 비면 빈 문자열(아무 API도 허용하지 않음).</summary>
    internal static string NormalizeCodes(string? codes)
    {
        var list = (codes ?? "").Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var code in list.Where(c => !CodePattern().IsMatch(c)))
        {
            throw new BizException(MsgInvalidCode, code);
        }

        var joined = string.Join(',', list);
        return joined.Length <= 1000 ? joined : throw new BizException(MessageKeys.InvalidInput);
    }

    private static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : throw new BizException(MessageKeys.InvalidInput);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,50}$")]
    private static partial Regex ClientIdPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,39}$")]
    private static partial Regex CodePattern();
}
