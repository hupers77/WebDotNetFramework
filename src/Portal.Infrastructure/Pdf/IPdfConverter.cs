namespace Portal.Infrastructure.Pdf;

/// <summary>PDF 한 건의 출력 옵션.</summary>
/// <param name="FooterText">모든 쪽 아래에 찍을 문구(출력자·출력 일시 등, 워터마크). 오른쪽에는 쪽 번호가 붙는다. 비우면 쪽 번호만.</param>
/// <param name="Landscape">가로 방향이면 true (기본 세로 A4).</param>
public sealed record PdfPageOptions(string? FooterText = null, bool Landscape = false);

/// <summary>
/// HTML 문서를 PDF로 바꾼다(설계서 11장 PDF 변환 ①). 화면·레포트는 인쇄용 CSS를 포함한 완성된 HTML을 넘긴다.
/// 문서는 외부 자원을 불러올 수 없다: CSS는 문서 안에 넣고, 그림은 data: URL로 넣는다.
/// </summary>
public interface IPdfConverter
{
    /// <summary>HTML을 PDF 바이트로 바꾼다.</summary>
    Task<byte[]> ConvertAsync(string html, PdfPageOptions options, CancellationToken cancellationToken = default);
}
