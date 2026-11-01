using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaTools.Integrations;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.MediaTools.Services;

/// <summary>
/// Registers optional web integrations after Jellyfin has loaded its plugins and services.
/// </summary>
public sealed class MediaToolsStartupService : IHostedService
{
    private readonly PluginPagesBridge _bridge;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaToolsStartupService"/> class.
    /// </summary>
    /// <param name="bridge">Optional Paradox integration bridge.</param>
    public MediaToolsStartupService(PluginPagesBridge bridge)
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
