using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicSuite.Integrations;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.MusicSuite.Services;

/// <summary>
/// Activates the optional UI bridges when Jellyfin starts, even before any plugin API is called.
/// </summary>
public sealed class MusicSuiteStartupService : IHostedService
{
    private readonly ParadoxBridge _bridge;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicSuiteStartupService"/> class.
    /// </summary>
    /// <param name="bridge">Optional Jellyfin web integration bridge.</param>
    public MusicSuiteStartupService(ParadoxBridge bridge)
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
