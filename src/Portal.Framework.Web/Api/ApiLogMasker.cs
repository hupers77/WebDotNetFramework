using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace Portal.Framework.Web.Api;

/// <summary>
/// API 기록에 남기는 요청·응답 본문의 민감 값을 가린다 (설계서 11장: 요청/응답 로그, 민감 필드 마스킹).
/// 이름에 비밀번호·토큰·비밀값·주민번호·카드 등이 들어간 필드는 값을 <c>***</c>로 바꾸고, JSON·폼 외의 본문은 남기지 않는다.
/// </summary>
internal static partial class ApiLogMasker
{
    internal const int MaxBodyLength = 4000;
    internal const string Mask = "***";
    internal const string Omitted = "[omitted]";

    /// <summary>본문을 가려 기록용 문자열로 만든다. 남기지 않을 본문은 null.</summary>
    public static string? MaskBody(string? body, string? contentType)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        string masked;
        if (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            masked = MaskJson(body);
        }
        else if (contentType?.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) == true)
        {
            masked = MaskQuery(body);
        }
        else
        {
            return Omitted;
        }

        return masked.Length > MaxBodyLength ? masked[..MaxBodyLength] + "…" : masked;
    }

    /// <summary>주소의 쿼리 문자열 값을 가린다.</summary>
    public static string MaskUrl(string path, string? query)
    {
        var url = string.IsNullOrEmpty(query) || query == "?" ? path : path + "?" + MaskQuery(query.TrimStart('?'));
        return url.Length > 1000 ? url[..1000] : url;
    }

    /// <summary>필드 이름이 민감한지.</summary>
    internal static bool IsSensitive(string name) => SensitiveName().IsMatch(name);

    private static string MaskQuery(string query)
    {
        var parts = new List<string>();
        foreach (var pair in new QueryStringEnumerable(query))
        {
            var name = pair.DecodeName().ToString();
            parts.Add(Uri.EscapeDataString(name) + "=" + (IsSensitive(name) ? Mask : Uri.EscapeDataString(pair.DecodeValue().ToString())));
        }

        return string.Join('&', parts);
    }

    private static string MaskJson(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            MaskNode(node);
            return node?.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? Omitted;
        }
        catch (JsonException)
        {
            return Omitted;
        }
    }

    private static void MaskNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (IsSensitive(key))
                    {
                        obj[key] = Mask;
                    }
                    else
                    {
                        MaskNode(obj[key]);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    MaskNode(item);
                }

                break;
        }
    }

    [GeneratedRegex("pass|pwd|secret|token|authorization|credential|otp|ssn|jumin|resident|rrn|card|account_?no|bank", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveName();
}
