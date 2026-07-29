using System.Reflection;
using Jellyfin.Plugin.BetterMusicDisplay.Controllers;
using Jellyfin.Plugin.BetterMusicDisplay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public sealed class AlbumQueryModelTests
{
    [Fact]
    public void QueryBoundsAndNormalizesClientInput()
    {
        var normalized = new AlbumQueryRequest
        {
            StartIndex = -10,
            Limit = 10000,
            ParentId = Guid.Empty,
            SearchTerm = "  album  ",
            SortBy = "unsupported",
            SortOrder = "sideways",
            Genre = "  Jazz ",
            Year = 12,
            Fields = "Genres,Unsupported,DateCreated,Genres"
        }.Normalize();

        Assert.Equal(0, normalized.StartIndex);
        Assert.Equal(200, normalized.Limit);
        Assert.Null(normalized.ParentId);
        Assert.Equal("album", normalized.SearchTerm);
        Assert.Equal("SortName", normalized.SortBy);
        Assert.Equal("Ascending", normalized.SortOrder);
        Assert.Equal("Jazz", normalized.Genre);
        Assert.Null(normalized.Year);
        Assert.True(normalized.IncludesField("Genres"));
        Assert.True(normalized.IncludesField("DateCreated"));
        Assert.False(normalized.IncludesField("Unsupported"));
    }

    [Theory]
    [InlineData("AlbumArtist")]
    [InlineData("ProductionYear")]
    [InlineData("DateCreated")]
    [InlineData("SortName")]
    public void SupportedSortKeysArePreserved(string sortBy)
    {
        var normalized = new AlbumQueryRequest
        {
            SortBy = sortBy,
            SortOrder = "Descending"
        }.Normalize();

        Assert.Equal(sortBy, normalized.SortBy);
        Assert.Equal("Descending", normalized.SortOrder);
    }

    [Fact]
    public void AlbumsControllerRequiresAuthenticationAndBoundedGetRoute()
    {
        var authorize = typeof(AlbumsController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .ToArray();
        var method = typeof(AlbumsController).GetMethod(nameof(AlbumsController.GetAlbums));

        Assert.NotEmpty(authorize);
        Assert.NotNull(method);
        Assert.NotEmpty(method!.GetCustomAttributes<HttpGetAttribute>(inherit: true));
    }
}
