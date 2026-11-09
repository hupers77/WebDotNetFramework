using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Portal.Infrastructure.Ui;

namespace Portal.Framework.Web.Layout;

/// <summary>
/// 디자인 프로필 폴더를 <c>/profile</c> 아래 정적 파일로 내보낸다. 화면에 쓰는 형식(CSS, 그림, 글꼴)만 내보내고
/// <c>profile.json</c>·<c>mail-layout.html</c> 등 다른 파일은 404다. 주소에 <c>?v=해시</c>가 있으면 1년 캐시, 없으면 매번 확인한다.
/// </summary>
internal static class ProfileFiles
{
    /// <summary>내보내는 확장자와 형식.</summary>
    internal static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".css"] = "text/css",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".jpg"] = "image/jpeg",
        [".ico"] = "image/x-icon",
        [".woff2"] = "font/woff2",
    };

    /// <summary>프로필 폴더용 정적 파일 설정.</summary>
    public static StaticFileOptions Options(string rootPath) => new()
    {
#pragma warning disable CA2000 // 앱이 끝날 때까지 쓰는 파일 공급자다
        FileProvider = new PhysicalFileProvider(rootPath),
#pragma warning restore CA2000
        RequestPath = UiProfile.RequestPath,
        ContentTypeProvider = new FileExtensionContentTypeProvider(new Dictionary<string, string>(ContentTypes, StringComparer.OrdinalIgnoreCase)),
        ServeUnknownFileTypes = false,
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl =
            ctx.Context.Request.Query.ContainsKey("v") ? "public, max-age=31536000, immutable" : "no-cache",
    };
}
