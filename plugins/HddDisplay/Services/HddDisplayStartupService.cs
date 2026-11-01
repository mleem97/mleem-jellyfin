using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HddDisplay.Integrations;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.HddDisplay.Services;

/// <summary>
/// Registers optional web integrations after Jellyfin has loaded its plugins and services.
/// </summary>
public sealed class HddDisplayStartupService : IHostedService
{
    private readonly PluginPagesBridge _bridge;

    /// <summary>
    /// Initializes a new instance of the <see cref="HddDisplayStartupService"/> class.
    /// </summary>
    /// <param name="bridge">Optional Paradox integration bridge.</param>
    public HddDisplayStartupService(PluginPagesBridge bridge)
    {
        _bridge = bridge;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _bridge.Initialize();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
