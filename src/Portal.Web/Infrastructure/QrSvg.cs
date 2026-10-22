using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Html;
using QRCoder;

namespace Portal.Web.Infrastructure;

/// <summary>
/// QR 코드를 SVG로 그린다 (2단계 인증 등록). 그림 라이브러리 없이 모듈 행렬을 경로 하나로 쓴다.
/// 화면에 그대로 넣는 마크업이지만 담는 글자는 SVG에 들어가지 않고(모듈 좌표만 쓴다) 설명은 인코딩하므로 주입 위험이 없다.
/// </summary>
public static class QrSvg
{
    /// <summary>글자를 QR SVG 마크업으로. 오류 정정 수준 M, 가장자리 여백(4칸) 포함.</summary>
    /// <param name="text">담을 글자.</param>
    /// <param name="label">화면 낭독기에 읽어 줄 설명.</param>
    public static IHtmlContent Render(string text, string label)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        var size = matrix.Count;
        var path = new StringBuilder();
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (matrix[y][x])
                {
                    path.Append(CultureInfo.InvariantCulture, $"M{x} {y}h1v1h-1z");
                }
            }
        }

        var encodedLabel = System.Net.WebUtility.HtmlEncode(label);
        return new HtmlString($"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {size} {size}" class="qr" role="img" aria-label="{encodedLabel}" shape-rendering="crispEdges"><rect width="{size}" height="{size}" fill="#fff"/><path d="{path}" fill="#000"/></svg>""");
    }
}
