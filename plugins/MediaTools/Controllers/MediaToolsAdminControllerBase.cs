using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MediaTools.Controllers;

/// <summary>
/// Base controller applying Jellyfin's administrator elevation policy.
/// </summary>
[Authorize(Policy = Policies.RequiresElevation)]
public abstract class MediaToolsAdminControllerBase : ControllerBase
{
}
