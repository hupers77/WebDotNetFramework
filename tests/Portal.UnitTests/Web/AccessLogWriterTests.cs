using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Portal.Data;
using Portal.Framework.Web.Logging;
using Portal.Testing;

namespace Portal.UnitTests.Web;

public sealed class AccessLogWriterTests : IDisposable
{
    private readonly FakeSpExecutor _sp = new();
    private readonly AccessLogBuffer _buffer = new();
    private readonly AccessLogWriter _writer;

    public AccessLogWriterTests()
    {
        var services = new ServiceCollection().AddSingleton<ISpExecutor>(_sp).BuildServiceProvider();
        _writer = new AccessLogWriter(_buffer, services.GetRequiredService<IServiceScopeFactory>(), new FakeTimeProvider(), NullLogger<AccessLogWriter>.Instance);
    }

    public void Dispose() => _writer.Dispose();

    private static AccessLogEntry Entry(string path) => new() { HttpMethod = "GET", RequestPath = path, StatusCode = 200 };

    [Fact]
    public async Task Flush_배치크기만큼TVP한번으로_순번부여()
    {
        for (var i = 0; i < AccessLogWriter.BatchSize + 5; i++)
        {
            _buffer.Enqueue(Entry("/p" + i));
        }

        var first = await _writer.FlushOnceAsync(TestContext.Current.CancellationToken);
        var second = await _writer.FlushOnceAsync(TestContext.Current.CancellationToken);
        var third = await _writer.FlushOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal((AccessLogWriter.BatchSize, 5, 0), (first, second, third));
        Assert.Equal(2, _sp.Calls.Count(c => c.SpName == AccessLogWriter.WriteSp));
        var tvp = Assert.IsType<Tvp>(_sp.Calls[1].Parameters["P_ROWS"]);
        Assert.Equal(AccessLogWriter.TvpType, tvp.TypeName);
    }

    [Fact]
    public void Buffer_가득차면오래된것부터버리고건수기록()
    {
        for (var i = 0; i < AccessLogBuffer.Capacity + 3; i++)
        {
            _buffer.Enqueue(Entry("/p" + i));
        }

        Assert.Equal(3, _buffer.TakeDroppedCount());
        Assert.Equal(0, _buffer.TakeDroppedCount());
        Assert.True(_buffer.Reader.TryRead(out var oldest));
        Assert.Equal("/p3", oldest.RequestPath);
    }
}
