using System;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Controllers;

/// <summary>
/// Serves only previously validated public cover bytes. The opaque capability URL
/// lets Jellyfin's native image downloader read them without forwarding user tokens.
/// This endpoint accepts no source URL and never performs an outbound request.
/// </summary>
[ApiController]
[Route("Plugins/MusicHoarderzProvider/Images")]
public class ValidatedImageController : ControllerBase
{
    private readonly ValidatedCoverCache _cache;

    /// <summary>Initializes a new instance of the <see cref="ValidatedImageController"/> class.</summary>
    /// <param name="cache">Validated cover cache.</param>
    public ValidatedImageController(ValidatedCoverCache cache)
    {
        _cache = cache;
    }

    /// <summary>Retrieves one already validated cover until its capability expires.</summary>
    /// <param name="ticket">Random capability identifier, issued only by authenticated search.</param>
    /// <returns>Image bytes or not found; never an upstream redirect.</returns>
    [HttpGet("{ticket:guid}")]
    [AllowAnonymous]
    public IActionResult GetImage(Guid ticket)
    {
        var image = _cache.Get(ticket);
        if (image is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(image.Content.ToArray(), image.ContentType);
    }
}
