using System;
using System.Linq;
using System.Security.Claims;

namespace Jellyfin.Plugin.MusicSuite.Services;

/// <summary>
/// Evaluates Jellyfin request claims for per-user settings access.
/// </summary>
public static class UserSettingsAccessEvaluator
{
    private const string UserIdClaimType = "Jellyfin-UserId";
    private const string AdminRole = "Administrator";

    /// <summary>
    /// Determines whether a principal may access the requested user's settings.
    /// </summary>
    /// <param name="principal">Request principal.</param>
    /// <param name="requestedUserId">Requested Jellyfin user id.</param>
    /// <returns>Whether access to the requested user's settings is allowed.</returns>
    public static bool CanAccess(ClaimsPrincipal principal, Guid requestedUserId)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.Identity?.IsAuthenticated != true || requestedUserId == Guid.Empty)
        {
            return false;
        }

        if (principal.IsInRole(AdminRole))
        {
            return true;
        }

        return TryGetUserId(principal, out var currentUserId)
            && currentUserId == requestedUserId;
    }

    /// <summary>
    /// Reads the server-issued Jellyfin user id claim.
    /// </summary>
    /// <param name="principal">Request principal.</param>
    /// <param name="userId">The parsed Jellyfin user id, when the claim is present.</param>
    /// <returns>Whether a non-empty Jellyfin user id claim was found.</returns>
    public static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var value = principal.Claims
            .FirstOrDefault(claim => string.Equals(
                claim.Type,
                UserIdClaimType,
                StringComparison.OrdinalIgnoreCase))?
            .Value;
        return Guid.TryParse(value, out userId) && userId != Guid.Empty;
    }
}
