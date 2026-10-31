using System.Collections.Frozen;

namespace Portal.Infrastructure.Files;

/// <summary>
/// 확장자 차단 목록과 내용 확인(매직 넘버). 허용 확장자는 공통코드 <c>FILE_EXT</c>가 정하지만,
/// 실행·스크립트 형식은 관리자가 코드에 추가해도 여기서 막는다.
/// </summary>
internal static class FileSignatures
{
    /// <summary>앞부분 몇 바이트를 검사하는지.</summary>
    internal const int HeadLength = 8192;

    private static readonly FrozenSet<string> Blocked = FrozenSet.ToFrozenSet(
    [
        "exe", "dll", "com", "scr", "msi", "msp", "cpl", "sys", "bat", "cmd", "ps1", "psm1", "vbs", "vbe", "js", "jse", "mjs",
        "wsf", "wsh", "hta", "jar", "sh", "lnk", "reg", "html", "htm", "xhtml", "shtml", "svg", "xml", "aspx", "asp", "ashx",
        "asmx", "cshtml", "config", "php", "jsp",
        // 점검(2026-10) 추가: 브라우저가 문서로 여는 형식, 바로 실행되는 Windows 형식, 디스크 이미지(내려받기 표시 우회), 서버 스크립트
        "mht", "mhtml", "xht", "xsl", "xslt", "svgz", "chm", "pif", "msc", "application", "gadget", "inf", "scf", "url", "ws", "vb",
        "psd1", "ps1xml", "appx", "msix", "iso", "img", "vhd", "vhdx", "phtml", "php5", "jspx", "asa", "war", "swf", "jnlp",
    ], StringComparer.OrdinalIgnoreCase);

    private static readonly byte[] Pdf = "%PDF"u8.ToArray();
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Gif = "GIF8"u8.ToArray();
    private static readonly byte[] Elf = [0x7F, 0x45, 0x4C, 0x46];

    /// <summary>항상 막는 확장자인지.</summary>
    internal static bool IsBlocked(string ext) => Blocked.Contains(ext);

    /// <summary>
    /// 내용이 확장자와 맞는지. 규칙이 없는 확장자는 실행 파일(MZ, ELF, <c>#!</c>)만 아니면 통과한다.
    /// </summary>
    internal static bool Matches(string ext, ReadOnlySpan<byte> head) => ext.ToLowerInvariant() switch
    {
        "pdf" => head.StartsWith(Pdf),
        "xlsx" or "docx" or "pptx" or "hwpx" or "zip" => head.StartsWith(Zip),
        "xls" or "doc" or "ppt" or "hwp" => head.StartsWith(Ole),
        "jpg" or "jpeg" => head.StartsWith(Jpeg),
        "png" => head.StartsWith(Png),
        "gif" => head.StartsWith(Gif),
        "txt" or "csv" => !head.Contains((byte)0) && !IsExecutable(head),
        _ => !IsExecutable(head),
    };

    private static bool IsExecutable(ReadOnlySpan<byte> head) =>
        head.StartsWith("MZ"u8) || head.StartsWith(Elf) || head.StartsWith("#!"u8);
}
