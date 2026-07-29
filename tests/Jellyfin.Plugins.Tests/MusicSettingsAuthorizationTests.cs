using System.Reflection;
using System.Security.Claims;
using Jellyfin.Plugin.BetterMusicDisplay.Controllers;
using Jellyfin.Plugin.BetterMusicDisplay.Services;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public sealed class MusicSettingsAuthorizationTests
{
    [Fact]
    public void AuthenticatedUserCanAccessOwnSettings()
    {
        var userId = Guid.NewGuid();
        var principal = CreatePrincipal(userId, isAdministrator: false);

        Assert.True(UserSettingsAccessEvaluator.CanAccess(principal, userId));
    }

    [Fact]
    public void AuthenticatedUserCannotAccessAnotherUsersSettings()
    {
        var principal = CreatePrincipal(Guid.NewGuid(), isAdministrator: false);

        Assert.False(UserSettingsAccessEvaluator.CanAccess(principal, Guid.NewGuid()));
    }

    [Fact]
    public void AdministratorCanAccessAnotherUsersSettings()
    {
        var principal = CreatePrincipal(Guid.NewGuid(), isAdministrator: true);

        Assert.True(UserSettingsAccessEvaluator.CanAccess(principal, Guid.NewGuid()));
    }

    [Fact]
    public void MissingOrUnauthenticatedClaimsDoNotGrantAccess()
    {
        var unauthenticated = new ClaimsPrincipal(new ClaimsIdentity());
        var authenticatedWithoutUser = new ClaimsPrincipal(new ClaimsIdentity(
            Array.Empty<Claim>(),
            authenticationType: "test"));

        Assert.False(UserSettingsAccessEvaluator.CanAccess(unauthenticated, Guid.NewGuid()));
        Assert.False(UserSettingsAccessEvaluator.CanAccess(authenticatedWithoutUser, Guid.NewGuid()));
    }

    [Fact]
    public void ControllerRequiresAuthentication()
    {
        var authorize = typeof(MusicDisplayController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .ToArray();

        Assert.NotEmpty(authorize);
    }

    private static ClaimsPrincipal CreatePrincipal(Guid userId, bool isAdministrator)
    {
        var claims = new List<Claim>
        {
            new("Jellyfin-UserId", userId.ToString("N"))
        };
        if (isAdministrator)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }
}
