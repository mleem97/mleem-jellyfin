using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugins.Tests;

/// <summary>
/// Shared test doubles for MusicHoarderz tests. No real network calls.
/// </summary>
internal sealed class MusicHoarderzTestHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public MusicHoarderzTestHttpClientFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    public HttpClient CreateClient(string name)
    {
        return new HttpClient(_handler, disposeHandler: false);
    }
}

/// <summary>
/// No-op logger for tests.
/// </summary>
internal sealed class MusicHoarderzNoopLogger<T> : ILogger<T>
{
    public static readonly MusicHoarderzNoopLogger<T> Instance = new();

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return MusicHoarderzNullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return false;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }

    private sealed class MusicHoarderzNullScope : IDisposable
    {
        public static readonly MusicHoarderzNullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Queue-based stub handler with request capture.
/// </summary>
internal sealed class MusicHoarderzQueueHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;

    public MusicHoarderzQueueHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    public List<CancellationToken> Tokens { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Tokens.Add(cancellationToken);
        return Task.FromResult(_responder(request, cancellationToken));
    }
}
