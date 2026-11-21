using System.Net;
using Jellyfin.Plugin.MusicHoarderzProvider.Models;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public class MusicHoarderzStreamingTests
{
    [Fact]
    public async Task ChunkedJson_StopsReadingAtHardLimit()
    {
        var stream = new GeneratedStream(20 * 1024 * 1024);
        var handler = new MusicHoarderzQueueHandler((_, _) => Response(stream, "application/json"));
        var client = Client(handler);

        Assert.Empty(await client.SearchAsync(new CoverSearchQuery("Album", "Artist", null, "DE"), CancellationToken.None));
        Assert.Equal("ResponseTooLarge", client.Health.Error?.Code);
        Assert.InRange(stream.BytesRead, 1, (2 * 1024 * 1024) + 1);
    }

    [Fact]
    public async Task ChunkedImage_StopsReadingAtHardLimit()
    {
        var stream = new GeneratedStream(40 * 1024 * 1024);
        var factory = new MusicHoarderzTestHttpClientFactory(new MusicHoarderzQueueHandler((_, _) => Response(stream, "image/png")));
        var validator = new CoverImageValidator(factory, MusicHoarderzNoopLogger<CoverImageValidator>.Instance, new MusicHoarderzTestImageDecoder(),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

        Assert.Null(await validator.ValidateAsync("https://covers.example.com/a.png", 1, 1, CancellationToken.None));
        Assert.InRange(stream.BytesRead, 1, CoverImageValidator.MaxImageBytes + 1);
    }

    [Fact]
    public async Task CancellationDuringBodyRead_IsNotSwallowed()
    {
        var stream = new GeneratedStream(100, block: true);
        var client = Client(new MusicHoarderzQueueHandler((_, _) => Response(stream, "application/json")));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SearchAsync(new CoverSearchQuery("Album", "Artist", null, "DE"), cancellation.Token));
        Assert.Equal(0, stream.BytesRead);
    }

    [Fact]
    public async Task UpstreamExceptionSecrets_AreNotLoggedOrReturned()
    {
        const string secret = "never-log-this-key";
        var logger = new CaptureLogger<MusicHoarderzHttpClient>();
        var handler = new MusicHoarderzQueueHandler((_, _) => throw new HttpRequestException("GET https://upstream/?key=" + secret));
        var client = new MusicHoarderzHttpClient(new MusicHoarderzTestHttpClientFactory(handler), logger,
            "https://covers.example.com", "DE", (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

        await client.SearchAsync(new CoverSearchQuery("Album", "Artist", null, "DE"), CancellationToken.None);

        Assert.Null(logger.LastException);
        Assert.DoesNotContain(logger.Messages, value => value.Contains(secret, StringComparison.Ordinal));
        Assert.DoesNotContain(client.RecentErrors, error => error.Message.Contains(secret, StringComparison.Ordinal));
    }

    private static MusicHoarderzHttpClient Client(MusicHoarderzQueueHandler handler)
    {
        return new MusicHoarderzHttpClient(new MusicHoarderzTestHttpClientFactory(handler), MusicHoarderzNoopLogger<MusicHoarderzHttpClient>.Instance,
            "https://covers.example.com", "DE", (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));
    }

    private static HttpResponseMessage Response(Stream stream, string mime)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
        Assert.Null(response.Content.Headers.ContentLength);
        return response;
    }

    private sealed class GeneratedStream : Stream
    {
        private readonly int _totalBytes;
        private readonly bool _block;

        public GeneratedStream(int totalBytes, bool block = false)
        {
            _totalBytes = totalBytes;
            _block = block;
        }

        public int BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_block)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, _totalBytes - BytesRead);
            buffer.Span[..count].Clear();
            BytesRead += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public Exception? LastException { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            LastException = exception;
        }
    }
}
