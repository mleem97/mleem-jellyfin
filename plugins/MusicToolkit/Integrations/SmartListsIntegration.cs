using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicToolkit.Integrations;

/// <summary>
/// Smart list rule from configs/smartlists-rules.json.
/// </summary>
/// <param name="Name">Rule name.</param>
/// <param name="Expression">Rule expression for SmartLists plugin.</param>
/// <param name="Limit">Max items.</param>
public sealed record SmartListRule(string Name, string Expression, int Limit);

/// <summary>
/// Reads SmartLists rule files and exposes them to the Spotify dashboard.
/// </summary>
public sealed class SmartListsIntegration
{
    private readonly ILogger<SmartListsIntegration> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SmartListsIntegration"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public SmartListsIntegration(ILogger<SmartListsIntegration> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Loads rules from a JSON file (configs/smartlists-rules.json shape).
    /// </summary>
    /// <param name="path">Absolute path to rules json.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rules (empty when file is missing).</returns>
    public async Task<IReadOnlyList<SmartListRule>> LoadRulesAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return Array.Empty<SmartListRule>();
            }

            using var stream = File.OpenRead(path);
            var rules = await JsonSerializer.DeserializeAsync<List<SmartListRule>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return rules ?? new List<SmartListRule>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load SmartLists rules from {Path}", path);
            return Array.Empty<SmartListRule>();
        }
    }
}
