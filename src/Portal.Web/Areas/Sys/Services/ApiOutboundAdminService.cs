using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Core.Security;
using Portal.Data;
using Portal.Framework.Web.Api.Outbound;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>송신 대상 관리 (<c>SYS_API_OUTBOUND_MGT</c>). 인증 비밀값은 암호화해 저장하고 화면으로 다시 내보내지 않는다.</summary>
public interface IApiOutboundAdminService
{
    /// <summary>송신 대상 목록.</summary>
    Task<PageResult<ApiOutboundDto>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>바뀐 행을 한 번에 저장한다. 설정은 최대 30초 뒤 송신에 반영된다.</summary>
    Task<ApiOutboundSaveResult> SaveAsync(ApiOutboundSaveRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed partial class ApiOutboundAdminService(ISpExecutor sp, ISecretProtector secrets) : IApiOutboundAdminService
{
    internal const string ListSp = "FW.USP_API_OUTBOUND_ADMIN_GET_LIST";
    internal const string SaveSp = "FW.USP_API_OUTBOUND_SAVE";
    internal const string MsgInvalidUrl = "VAL.OUTBOUND_URL";
    internal const string MsgInvalidHeader = "VAL.OUTBOUND_HEADER";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] AuthTypes = ["NONE", "BASIC", "BEARER", "API_KEY"];

    /// <inheritdoc />
    public Task<PageResult<ApiOutboundDto>> GetListAsync(CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<ApiOutboundDto>(ListSp, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<ApiOutboundSaveResult> SaveAsync(ApiOutboundSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Rows.Count == 0)
        {
            throw new BizException(MessageKeys.NoChanges);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<object>(request.Rows.Count);
        foreach (var row in request.Rows)
        {
            if (!KeyPattern().IsMatch(row.TargetKey))
            {
                throw new BizException(MessageKeys.InvalidInput);
            }

            if (!seen.Add(row.TargetKey))
            {
                throw new BizException("VAL.DUP_ROW", row.TargetKey);
            }

            var editing = row.RowStat is RowStates.Inserted or RowStates.Updated;
            if (row.RowStat is not (RowStates.Inserted or RowStates.Updated or RowStates.Deleted)
                || (row.RowStat is RowStates.Updated or RowStates.Deleted && row.RowVer is not { Length: 8 }))
            {
                throw new BizException(MessageKeys.InvalidInput);
            }

            if (!editing)
            {
                rows.Add(new { row.RowStat, row.TargetKey, RowVer = Convert.ToHexString(row.RowVer!) });
                continue;
            }

            rows.Add(BuildEditRow(row));
        }

        var saved = await sp.ExecuteAsync(SaveSp, new { P_ROWS_JSON = JsonSerializer.Serialize(rows, Json) }, cancellationToken);
        return new ApiOutboundSaveResult(saved.AffectedCnt);
    }

    private object BuildEditRow(ApiOutboundSaveRow row)
    {
        var authType = row.AuthTypeCd ?? "";
        if (string.IsNullOrWhiteSpace(row.TargetNm) || !AuthTypes.Contains(authType, StringComparer.Ordinal)
            || row.AllowPrivateYn is not ("Y" or "N") || row.UseYn is not ("Y" or "N"))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var allowPrivate = row.AllowPrivateYn == "Y";
        var baseUrl = NormalizeUrl(row.BaseUrl, allowPrivate);
        var header = authType == "API_KEY" ? row.AuthHeader?.Trim() : null;
        if (authType == "API_KEY" && (header is null || !HeaderPattern().IsMatch(header)))
        {
            throw new BizException(MsgInvalidHeader, header ?? "");
        }

        var user = authType == "BASIC" ? row.AuthUser?.Trim() : null;
        if (authType == "BASIC" && string.IsNullOrEmpty(user))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var setSecret = authType != "NONE" && !string.IsNullOrEmpty(row.NewSecret);
        return new
        {
            row.RowStat,
            row.TargetKey,
            TargetNm = row.TargetNm.Trim(),
            BaseUrl = baseUrl,
            AuthTypeCd = authType,
            AuthHeader = header,
            AuthUser = user,
            SecretMode = setSecret ? "SET" : "KEEP",
            SecretEnc = setSecret ? secrets.Protect(row.NewSecret!) : null,
            row.TimeoutSec,
            row.RetryCount,
            row.AllowPrivateYn,
            row.UseYn,
            RowVer = row.RowVer is null ? null : Convert.ToHexString(row.RowVer),
        };
    }

    /// <summary>기본 주소를 검사하고 끝의 <c>/</c>를 뺀 형태로 맞춘다. https만 허용하고(사내망 허용이면 http도), 계정 정보·쿼리·조각은 막는다.</summary>
    internal static string NormalizeUrl(string? value, bool allowPrivate)
    {
        var text = value?.Trim() ?? "";
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Host.Length == 0 || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && allowPrivate)
            || text.Length > 500)
        {
            throw new BizException(MsgInvalidUrl, text.Length > 80 ? text[..80] : text);
        }

        // 주소 자체가 IP면 저장할 때 바로 거르고, 이름이면 호출할 때 풀린 주소로 한 번 더 검사한다
        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && !OutboundAddressPolicy.IsAllowed(ip, allowPrivate))
        {
            throw new BizException(MsgInvalidUrl, text);
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,29}$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]{0,49}$")]
    private static partial Regex HeaderPattern();
}
