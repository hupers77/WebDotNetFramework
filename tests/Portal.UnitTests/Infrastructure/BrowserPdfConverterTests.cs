using Microsoft.Extensions.Options;
using Portal.Infrastructure.Pdf;

namespace Portal.UnitTests.Infrastructure;

public sealed class BrowserPdfConverterTests
{
    [Fact]
    public async Task 없는브라우저경로면_알기쉬운오류()
    {
        using var converter = new BrowserPdfConverter(Options.Create(new PdfOptions { BrowserPath = "/nonexistent/msedge" }));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => converter.ConvertAsync("<p>x</p>", new PdfPageOptions(), TestContext.Current.CancellationToken));

        Assert.StartsWith("PDF_BROWSER_NOT_FOUND", error.Message, StringComparison.Ordinal);
    }
}
