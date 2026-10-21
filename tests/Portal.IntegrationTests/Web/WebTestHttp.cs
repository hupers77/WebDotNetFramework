using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Portal.IntegrationTests.Web;

/// <summary>웹 테스트 요청 도우미: AJAX 요청 표시, CSRF 토큰 얻기, JSON 응답 읽기.</summary>
internal static partial class WebTestHttp
{
    public static HttpRequestMessage Ajax(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    /// <summary>화면을 열어 폼의 CSRF 토큰과 CSRF 쿠키를 얻는다.</summary>
    public static async Task<(string Token, string Cookie)> GetCsrfAsync(HttpClient client, string pageUrl)
    {
        var response = await client.GetAsync(pageUrl, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var token = TokenPattern().Match(html).Groups[1].Value;
        var cookie = response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).Single(c => c.StartsWith("__Host-portal-csrf=", StringComparison.Ordinal));
        Assert.NotEmpty(token);
        return (token, cookie);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    public static string? SetCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal))
            : null;

    [GeneratedRegex("""(?:name="__RequestVerificationToken" type="hidden" value|name="csrf-token" content)="([^"]+)" """)]
    private static partial Regex TokenPattern();
}
