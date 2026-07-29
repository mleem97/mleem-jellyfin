using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.HddDisplay.Controllers;

/// <summary>
/// Base controller that applies Jellyfin's administrator elevation policy.
/// </summary>
[Authorize(Policy = Policies.RequiresElevation)]
public abstract class HddDisplayAdminControllerBase : ControllerBase
{
}
