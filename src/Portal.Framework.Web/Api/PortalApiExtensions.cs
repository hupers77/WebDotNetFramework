using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Portal.Security.Passwords;

namespace Portal.Framework.Web.Api;

/// <summary>외부 API(수신) 구성. 호스트는 <c>AddPortalWeb</c>/<c>UsePortalWeb</c>를 통해 자동으로 적용한다.</summary>
public static class PortalApiExtensions
{
    internal const int MinSigningKeyBytes = 32;

    /// <summary>외부 API 서비스를 등록한다.</summary>
    public static IServiceCollection AddPortalApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiOptions>().Bind(configuration.GetSection(ApiOptions.SectionName));
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiOptions>>().Value;
            return new ApiJwt(ResolveSigningKey(options, sp.GetRequiredService<IHostEnvironment>()), options.Issuer, options.Audience, sp.GetRequiredService<TimeProvider>());
        });
        services.AddScoped<IApiClientStore, ApiClientStore>();
        services.AddScoped<ApiTokenService>();
        services.AddSingleton<ApiRateLimiter>();
        services.AddSingleton<ApiLogBuffer>();
        services.AddHostedService<ApiLogWriter>();
        return services;
    }

    /// <summary>관문을 파이프라인에 넣는다 (라우팅 뒤여야 엔드포인트를 알 수 있다).</summary>
    public static IApplicationBuilder UsePortalApiGateway(this IApplicationBuilder app) =>
        app.UseWhen(
            c => c.Request.Path.StartsWithSegments(ApiEndpointExtensions.BasePath),
            branch => branch.UseMiddleware<ApiGatewayMiddleware>());

    /// <summary>기본 외부 API(토큰 발급, OpenAPI 문서, 연결 확인)를 등록한다. 업무 API는 <see cref="ApiEndpointExtensions.MapApi"/>로 더한다.</summary>
    public static IEndpointRouteBuilder MapPortalApi(this IEndpointRouteBuilder app)
    {
        app.MapApi("POST", "/token", ApiEndpointExtensions.TokenCode, "Issue an access token (OAuth2 client credentials).", IssueTokenAsync, anonymous: true);
        app.MapApi("GET", "/openapi.json", "OPENAPI", "OpenAPI document.", (HttpContext http, EndpointDataSource source) =>
            Results.Text(OpenApiDocument.Build(source, http.Request), "application/json"), anonymous: true);
        app.MapApi("GET", "/ping", "PING", "Check the connection and the token.", (HttpContext http, TimeProvider time) =>
            Results.Json(new { clientId = http.GetApiCaller()?.ClientId, serverTime = time.GetUtcNow() }));
        return app;
    }

    internal static byte[] ResolveSigningKey(ApiOptions options, IHostEnvironment env)
    {
        if (!string.IsNullOrWhiteSpace(options.SigningKey))
        {
            byte[] key;
            try
            {
                key = Convert.FromBase64String(options.SigningKey);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("Api:SigningKey는 Base64여야 합니다.", ex);
            }

            return key.Length >= MinSigningKeyBytes
                ? key
                : throw new InvalidOperationException($"Api:SigningKey는 {MinSigningKeyBytes}바이트 이상이어야 합니다.");
        }

        // 서명 키가 없으면 개발·테스트에서만 임의 키로 동작한다 (운영에서 키 없이 토큰이 발급되는 일을 막는다)
        return env.EnvironmentName is "Development" or "Testing"
            ? RandomNumberGenerator.GetBytes(MinSigningKeyBytes)
            : throw new InvalidOperationException("Api:SigningKey가 설정되지 않았습니다 (환경 변수 Api__SigningKey 또는 비밀 저장소로 주입).");
    }

    private static async Task<IResult> IssueTokenAsync(HttpContext http, ApiTokenService tokens)
    {
        if (!http.Request.HasFormContentType)
        {
            return OAuthError(400, "invalid_request", "Content-Type must be application/x-www-form-urlencoded.");
        }

        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        if (form["grant_type"] != "client_credentials")
        {
            return OAuthError(400, "unsupported_grant_type", "Only client_credentials is supported.");
        }

        string? clientId = form["client_id"], secret = form["client_secret"];
        if (TryBasic(http.Request, out var basicId, out var basicSecret))
        {
            (clientId, secret) = (basicId, basicSecret);
        }

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(secret) || clientId.Length > 100 || secret.Length > 500)
        {
            return OAuthError(400, "invalid_request", "client_id and client_secret are required.");
        }

        try
        {
            var token = await tokens.IssueAsync(clientId, secret, http.Connection.RemoteIpAddress, http.RequestAborted);
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.Pragma = "no-cache";
            return Results.Json(new { access_token = token.AccessToken, token_type = "Bearer", expires_in = token.ExpiresInSeconds });
        }
        catch (ApiAuthException ex)
        {
            if (ex.StatusCode == 401)
            {
                http.Response.Headers.WWWAuthenticate = "Basic realm=\"api\"";
            }

            return OAuthError(ex.StatusCode, ex.Error, ex.Message);
        }
    }

    private static IResult OAuthError(int status, string error, string description) =>
        Results.Json(new Dictionary<string, string> { ["error"] = error, ["error_description"] = description }, statusCode: status);

    private static bool TryBasic(HttpRequest request, out string id, out string secret)
    {
        (id, secret) = ("", "");
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
            var colon = decoded.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return false;
            }

            (id, secret) = (decoded[..colon], decoded[(colon + 1)..]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>등록된 외부 API 정의로 OpenAPI 3 문서를 만든다 (외부 패키지 없이).</summary>
internal static class OpenApiDocument
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Build(EndpointDataSource source, HttpRequest request)
    {
        var version = typeof(OpenApiDocument).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        var paths = new SortedDictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<ApiOperation>() is not { } op || endpoint.RoutePattern.RawText is not { } path)
            {
                continue;
            }

            var responses = new Dictionary<string, object> { ["200"] = new { description = "OK" } };
            if (!op.Anonymous)
            {
                responses["401"] = new { description = "Missing, malformed or expired token." };
                responses["403"] = new { description = "Client disabled, address or API not allowed." };
                responses["429"] = new { description = "Rate limit exceeded." };
            }

            var operation = new Dictionary<string, object>
            {
                ["operationId"] = op.Code,
                ["summary"] = op.Summary,
                ["responses"] = responses,
            };
            if (!op.Anonymous)
            {
                operation["security"] = new[] { new Dictionary<string, string[]> { ["bearer"] = [] } };
                operation["x-api-code"] = op.Code;
            }

            if (op.Code == ApiEndpointExtensions.TokenCode)
            {
                operation["requestBody"] = new
                {
                    required = true,
                    content = new Dictionary<string, object>
                    {
                        ["application/x-www-form-urlencoded"] = new
                        {
                            schema = new
                            {
                                type = "object",
                                required = new[] { "grant_type", "client_id", "client_secret" },
                                properties = new Dictionary<string, object>
                                {
                                    ["grant_type"] = new { type = "string", @enum = new[] { "client_credentials" } },
                                    ["client_id"] = new { type = "string" },
                                    ["client_secret"] = new { type = "string", format = "password" },
                                },
                            },
                        },
                    },
                };
            }

            if (!paths.TryGetValue(path, out var methods))
            {
                paths[path] = methods = [];
            }

            methods[op.Method.ToLowerInvariant()] = operation;
        }

        var doc = new Dictionary<string, object>
        {
            ["openapi"] = "3.0.3",
            ["info"] = new { title = "Portal API", version },
            ["servers"] = new[] { new { url = $"{request.Scheme}://{request.Host}" } },
            ["paths"] = paths,
            ["components"] = new { securitySchemes = new { bearer = new { type = "http", scheme = "bearer", bearerFormat = "JWT" } } },
        };
        return JsonSerializer.Serialize(doc, Indented);
    }
}
