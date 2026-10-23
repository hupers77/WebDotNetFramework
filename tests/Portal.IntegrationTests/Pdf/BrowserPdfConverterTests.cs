using Microsoft.Extensions.Options;
using Portal.Infrastructure.Pdf;

namespace Portal.IntegrationTests.Pdf;

/// <summary>
/// 실제 헤드리스 브라우저로 PDF를 만든다. 환경 변수 <c>PORTAL_PDF_BROWSER</c>(Edge 또는 Chromium 실행 파일)가 없으면 건너뛴다.
/// Linux 컨테이너에서는 root로 돌기 때문에 샌드박스를 끈다.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BrowserPdfConverterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BrowserPdfConverter Create(int timeoutSeconds = 60)
    {
        var path = Environment.GetEnvironmentVariable("PORTAL_PDF_BROWSER");
        Assert.SkipWhen(string.IsNullOrEmpty(path) || !File.Exists(path), "PORTAL_PDF_BROWSER가 없어 건너뜁니다.");
        return new BrowserPdfConverter(Options.Create(new PdfOptions
        {
            BrowserPath = path!,
            TimeoutSeconds = timeoutSeconds,
            NoSandbox = !OperatingSystem.IsWindows(),
        }));
    }

    [Fact]
    public async Task 한국어중국어문서를_PDF로바꾼다()
    {
        using var converter = Create();

        var pdf = await converter.ConvertAsync(
            "<!doctype html><html><head><meta charset=\"utf-8\"><style>h1{color:#123}</style></head><body><h1>월별 점검 보고서</h1><p>访问记录</p></body></html>",
            new PdfPageOptions("출력: admin 2026-10-23 11:00"), Ct);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF-"u8.ToArray(), pdf[..5]);
    }

    [Fact]
    public async Task 여러건을동시에요청해도_모두만든다()
    {
        using var converter = Create();

        var pdfs = await Task.WhenAll(Enumerable.Range(1, 6).Select(i =>
            converter.ConvertAsync($"<p>{i}</p>", new PdfPageOptions(), Ct)));

        Assert.All(pdfs, p => Assert.Equal("%PDF-"u8.ToArray(), p[..5]));
    }
}
