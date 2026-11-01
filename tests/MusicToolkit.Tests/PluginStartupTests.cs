using Jellyfin.Plugin.MusicToolkit.Integrations;
using Jellyfin.Plugin.MusicToolkit.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.MusicToolkit.Tests;

public sealed class PluginStartupTests
{
    [Fact]
    public async Task WebBridgeRunsAtHostedStartupAndCanRetryMissingDependencies()
    {
        var logger = new RecordingLogger();
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<ParadoxBridge>>(logger);
        new PluginServiceRegistrator().RegisterServices(services, null!);
        using var provider = services.BuildServiceProvider();
        var startup = Assert.Single(provider.GetServices<IHostedService>());
        Assert.IsType<MusicToolkitStartupService>(startup);
        Assert.Empty(logger.Messages);

        await Assert.ThrowsAsync<OperationCanceledException>(() => startup.StartAsync(new CancellationToken(canceled: true)));
        Assert.Empty(logger.Messages);
        await startup.StartAsync(CancellationToken.None);
        Assert.Contains(logger.Messages, message => message.Contains("Paradox PluginPages not installed", StringComparison.Ordinal));
        var initialAttempts = logger.Messages.Count;
        await startup.StartAsync(CancellationToken.None);
        Assert.Equal(initialAttempts * 2, logger.Messages.Count);
        await startup.StopAsync(CancellationToken.None);
    }

    private sealed class RecordingLogger : ILogger<ParadoxBridge>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
