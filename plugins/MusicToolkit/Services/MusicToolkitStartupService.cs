using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicToolkit.Integrations;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.MusicToolkit.Services;

/// <summary>
/// Registers optional web integrations after Jellyfin has loaded its plugins and services.
/// </summary>
public sealed class MusicToolkitStartupService : IHostedService
{
    private readonly ParadoxBridge _bridge;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicToolkitStartupService"/> class.
    /// </summary>
    /// <param name="bridge">Optional Paradox integration bridge.</param>
    public MusicToolkitStartupService(ParadoxBridge bridge)
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
