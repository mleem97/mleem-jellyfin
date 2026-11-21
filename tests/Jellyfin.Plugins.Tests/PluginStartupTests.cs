using System.Reflection;
using Jellyfin.Plugin.MetadataDashboard.Controllers;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public sealed class PluginStartupTests
{
    public static TheoryData<Type, Type> StartupServices => new()
    {
        { typeof(Jellyfin.Plugin.HddDisplay.PluginServiceRegistrator), typeof(Jellyfin.Plugin.HddDisplay.Services.HddDisplayStartupService) },
        { typeof(Jellyfin.Plugin.MediaTools.PluginServiceRegistrator), typeof(Jellyfin.Plugin.MediaTools.Services.MediaToolsStartupService) },
        { typeof(Jellyfin.Plugin.MetadataDashboard.PluginServiceRegistrator), typeof(Jellyfin.Plugin.MetadataDashboard.Services.MetadataDashboardStartupService) }
    };

    [Theory]
    [MemberData(nameof(StartupServices))]
    public async Task HostedStartupInvokesOptionalBridgeWithoutAnApiRequest(Type registratorType, Type startupType)
    {
        var logger = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logger));
        var registrator = Assert.IsAssignableFrom<IPluginServiceRegistrator>(Activator.CreateInstance(registratorType));
        registrator.RegisterServices(services, null!);
        using var provider = services.BuildServiceProvider();

        var startup = Assert.Single(provider.GetServices<IHostedService>());
        Assert.Equal(startupType, startup.GetType());
        Assert.Empty(logger.Messages);

        await Assert.ThrowsAsync<OperationCanceledException>(() => startup.StartAsync(new CancellationToken(canceled: true)));
        Assert.Empty(logger.Messages);

        await startup.StartAsync(CancellationToken.None);
        Assert.Contains(logger.Messages, message => message.Contains("Paradox PluginPages not installed", StringComparison.Ordinal));
        var initialAttempts = logger.Messages.Count;

        // A missing optional plugin must not permanently latch a failed initialization.
        await startup.StartAsync(CancellationToken.None);
        Assert.Equal(initialAttempts * 2, logger.Messages.Count);
        await startup.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void MetadataUpdatesRequireAdministratorElevation()
    {
        var controller = typeof(MetadataDashboardController);
        var policies = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true);
        Assert.Contains(policies, attribute => attribute.Policy == Policies.RequiresElevation);
        Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Add(formatter(state, exception));
        }
    }
}
