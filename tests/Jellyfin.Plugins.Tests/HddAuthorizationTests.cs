using System.Reflection;
using Jellyfin.Plugin.HddDisplay.Controllers;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public sealed class HddAuthorizationTests
{
    public static TheoryData<Type> AdminControllerTypes => new()
    {
        typeof(StorageController),
        typeof(SystemUsageController),
        typeof(AssetController)
    };

    [Theory]
    [MemberData(nameof(AdminControllerTypes))]
    public void HddControllersRequireJellyfinAdministratorElevation(Type controllerType)
    {
        var policies = controllerType
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(attribute => attribute.Policy)
            .ToArray();

        Assert.Contains(Policies.RequiresElevation, policies);
        Assert.True(typeof(HddDisplayAdminControllerBase).IsAssignableFrom(controllerType));
    }
}
