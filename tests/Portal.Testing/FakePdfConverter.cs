using Portal.Infrastructure.Pdf;

namespace Portal.Testing;

/// <summary>브라우저 없이 PDF 변환을 대신한다. 받은 HTML·옵션을 남기고 PDF 머리(<c>%PDF-</c>)만 있는 내용을 돌려준다.</summary>
public sealed class FakePdfConverter : IPdfConverter
{
    /// <summary>마지막으로 받은 HTML.</summary>
    public string? LastHtml { get; private set; }

    /// <summary>마지막으로 받은 옵션.</summary>
    public PdfPageOptions? LastOptions { get; private set; }

    /// <inheritdoc />
    public Task<byte[]> ConvertAsync(string html, PdfPageOptions options, CancellationToken cancellationToken = default)
    {
        LastHtml = html;
        LastOptions = options;
        return Task.FromResult("%PDF-1.7 fake"u8.ToArray());
    }
}
