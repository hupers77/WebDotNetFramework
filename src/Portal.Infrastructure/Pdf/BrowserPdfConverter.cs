using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Portal.Infrastructure.Pdf;

/// <summary>
/// 헤드리스 Microsoft Edge(또는 Chromium)로 HTML을 PDF로 바꾼다(설계서 2장·11장, 외부 패키지 없음).
/// 변환마다 브라우저를 새 임시 프로필로 띄우고, 개발자 도구 프로토콜로 문서를 넣어 <c>Page.printToPDF</c>를 부른 뒤 끝낸다.
/// 동시에 띄우는 브라우저 수는 <see cref="PdfOptions.MaxConcurrency"/>로 제한하고, 제한 시간을 넘으면 브라우저를 끝낸다.
/// </summary>
internal sealed class BrowserPdfConverter(IOptions<PdfOptions> options) : IPdfConverter, IDisposable
{
    private static readonly TimeSpan StartupPoll = TimeSpan.FromMilliseconds(50);

    private readonly PdfOptions settings = options.Value;
    private readonly SemaphoreSlim slots = new(Math.Max(1, options.Value.MaxConcurrency));

    /// <inheritdoc />
    public async Task<byte[]> ConvertAsync(string html, PdfPageOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var document = PdfDocument.Prepare(html);

        await slots.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));
            return await RunAsync(document, options, timeout.Token);
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task<byte[]> RunAsync(string document, PdfPageOptions options, CancellationToken cancellationToken)
    {
        var browserPath = string.IsNullOrWhiteSpace(settings.BrowserPath) ? PdfOptions.DefaultEdgePath : settings.BrowserPath;
        if (!File.Exists(browserPath))
        {
            throw new InvalidOperationException($"PDF_BROWSER_NOT_FOUND: '{browserPath}'에 브라우저가 없습니다. Portal:Pdf:BrowserPath를 확인하세요.");
        }

        var profile = Path.Combine(Path.GetTempPath(), "portal-pdf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);

        var start = new ProcessStartInfo(browserPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in PdfDocument.BrowserArguments(profile, settings.NoSandbox))
        {
            start.ArgumentList.Add(arg);
        }

        using var browser = Process.Start(start) ?? throw new InvalidOperationException("PDF_BROWSER_START: 브라우저를 시작하지 못했습니다.");
        browser.OutputDataReceived += static (_, _) => { };
        browser.ErrorDataReceived += static (_, _) => { };
        browser.BeginOutputReadLine();
        browser.BeginErrorReadLine();
        var closed = false;
        try
        {
            var endpoint = await WaitForEndpointAsync(profile, browser, cancellationToken);
            await using var cdp = await CdpSession.ConnectAsync(endpoint, cancellationToken);

            var target = await cdp.SendAsync("Target.createTarget", new { url = "about:blank" }, null, cancellationToken);
            var attached = await cdp.SendAsync("Target.attachToTarget", new { targetId = (string?)target["targetId"], flatten = true }, null, cancellationToken);
            var session = (string?)attached["sessionId"];

            var tree = await cdp.SendAsync("Page.getFrameTree", null, session, cancellationToken);
            var frameId = (string?)tree["frameTree"]?["frame"]?["id"];
            await cdp.SendAsync("Page.setDocumentContent", new { frameId, html = document }, session, cancellationToken);
            await cdp.SendAsync("Runtime.evaluate", new { expression = "document.fonts.ready.then(() => true)", awaitPromise = true }, session, cancellationToken);

            var pdf = await cdp.SendAsync("Page.printToPDF", PdfDocument.PrintParameters(options), session, cancellationToken);
            var data = (string?)pdf["data"] ?? throw new InvalidOperationException("PDF_BROWSER_ERROR: 브라우저가 PDF를 돌려주지 않았습니다.");

            await cdp.SendAsync("Browser.close", null, null, cancellationToken);
            closed = true;
            return Convert.FromBase64String(data);
        }
        finally
        {
            await StopAsync(browser, closed);
            TryDelete(profile);
        }
    }

    /// <summary>브라우저가 프로필 폴더에 쓰는 <c>DevToolsActivePort</c>(1줄: 포트, 2줄: 경로)를 기다려 웹소켓 주소를 만든다.</summary>
    private static async Task<Uri> WaitForEndpointAsync(string profile, Process browser, CancellationToken cancellationToken)
    {
        var file = Path.Combine(profile, "DevToolsActivePort");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (browser.HasExited)
            {
                throw new InvalidOperationException($"PDF_BROWSER_START: 브라우저가 바로 끝났습니다(종료 코드 {browser.ExitCode}).");
            }

            if (File.Exists(file))
            {
                string[] lines;
                try
                {
                    lines = await File.ReadAllLinesAsync(file, cancellationToken);
                }
                catch (IOException)
                {
                    lines = [];
                }

                if (lines.Length >= 2 && int.TryParse(lines[0], out var port) && port > 0)
                {
                    return new Uri($"ws://127.0.0.1:{port}{lines[1].Trim()}");
                }
            }

            await Task.Delay(StartupPoll, cancellationToken);
        }
    }

    /// <summary>정상 종료를 요청했으면 잠깐 기다리고, 아니면(실패·제한 시간) 바로 프로세스 트리를 끝낸다.</summary>
    private static async Task StopAsync(Process browser, bool closed)
    {
        try
        {
            if (!browser.HasExited)
            {
                using var wait = new CancellationTokenSource(closed ? TimeSpan.FromSeconds(5) : TimeSpan.Zero);
                try
                {
                    await browser.WaitForExitAsync(wait.Token);
                }
                catch (OperationCanceledException)
                {
                    browser.Kill(entireProcessTree: true);
                    await browser.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // 이미 끝난 프로세스
        }
    }

    /// <summary>임시 프로필을 지운다. 브라우저 자식 프로세스가 잠깐 파일을 잡고 있을 수 있어 몇 번 다시 시도하고, 끝내 못 지우면 남겨 둔다(임시 폴더).</summary>
    private static void TryDelete(string profile)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(profile, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => slots.Dispose();
}
