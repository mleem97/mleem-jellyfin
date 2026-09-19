using System.Net;
using System.Text;
using Jellyfin.Plugin.MusicHoarderzProvider.Models;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public class MusicHoarderzHttpClientTests
{
    private const string BaseUrl = "https://covers.example.com";

    [Fact]
    public async Task SearchAsync_Success_ParsesResultsAndSendsCountry()
    {
        var json = "{\"results\":[{\"url\":\"https://covers.example.com/a.jpg\",\"width\":1200,\"height\":1200,\"source\":\"cov\",\"mimeType\":\"image/jpeg\",\"title\":\"Test Album\",\"artist\":\"Test Artist\",\"year\":1999}]}";
        HttpRequestMessage? captured = null;
        var handler = new MusicHoarderzQueueHandler((request, _) =>
        {
            captured = request;
            return JsonResponse(json);
        });
        var client = CreateClient(handler);

        var results = await client.SearchAsync(new CoverSearchQuery("Test Album", "Test Artist", 1999, "DE"), CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("https://covers.example.com/a.jpg", results[0].Url);
        Assert.Equal("Test Album", results[0].Title);
        Assert.Equal("Test Artist", results[0].Artist);
        Assert.Equal(1999, results[0].Year);
        Assert.Empty(client.RecentErrors);
        Assert.NotNull(captured);
        Assert.Contains("country=DE", captured!.RequestUri!.Query);
        Assert.Contains("title=Test%20Album", captured.RequestUri.Query);
    }

    [Fact]
    public async Task SearchAsync_RootArrayShape_ParsesResults()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) =>
            JsonResponse("[{\"url\":\"https://covers.example.com/a.jpg\"}]"));
        var client = CreateClient(handler);

        var results = await client.SearchAsync(new CoverSearchQuery("A", "B", null, "DE"), CancellationToken.None);

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchAsync_EmptyResults_ReturnsEmpty()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) => JsonResponse("{\"results\":[]}"));
        var client = CreateClient(handler);

        var results = await client.SearchAsync(new CoverSearchQuery("A", "B", null, "DE"), CancellationToken.None);

        Assert.Empty(results);
        Assert.Empty(client.RecentErrors);
    }

    [Fact]
    public async Task SearchAsync_RateLimited_ReturnsEmptyWithStructuredError()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var client = CreateClient(handler);

        var results = await client.SearchAsync(new CoverSearchQuery("A", "B", null, "DE"), CancellationToken.None);

        Assert.Empty(results);
        Assert.Contains(client.RecentErrors, e => e.Code == "RateLimited");
    }

    [Fact]
    public async Task SearchAsync_ServerError_ReturnsEmptyWithStructuredError()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        var results = await client.SearchAsync(new CoverSearchQuery("A", "B", null, "DE"), CancellationToken.None);

        Assert.Empty(results);
        Assert.Contains(client.RecentErrors, e => e.Code == "ServerError");
    }

    [Fact]
    public async Task SearchAsync_Timeout_ReturnsEmptyWithStructuredError()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) => throw new TaskCanceledException("simulated timeout"));
        var client = CreateClient(handler);

        var results = await client.SearchAsync(new CoverSearchQuery("A", "B", null, "DE"), CancellationToken.None);

        Assert.Empty(results);
        Assert.Contains(client.RecentErrors, e => e.Code == "Timeout");
    }

    [Fact]
    public async Task SearchAsync_InvalidJson_ReturnsEmptyWithStructuredError()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) => JsonResponse("this is not json{{{", "text/plain"));
        var client = CreateClient(handler);

        var results = await client.SearchAsync(new CoverSearchQuery("A", "B", null, "DE"), CancellationToken.None);

        Assert.Empty(results);
        Assert.Contains(client.RecentErrors, e => e.Code == "InvalidPayload");
    }

    [Fact]
    public async Task SearchAsync_OversizeResponse_ReturnsEmptyWithStructuredError()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(new byte[(2 * 1024 * 1024) + 1]);
            return response;
        });
        var client = CreateClient(handler);

        var results = await client.SearchAsync(new CoverSearchQuery("A", "B", null, "DE"), CancellationToken.None);

        Assert.Empty(results);
        Assert.Contains(client.RecentErrors, e => e.Code == "ResponseTooLarge");
    }

    [Theory]
    [InlineData("http://covers.example.com")]
    [InlineData("https://localhost/search")]
    [InlineData("https://127.0.0.1/search")]
    [InlineData("https://192.168.0.5/search")]
    [InlineData("https://10.0.0.5/search")]
    [InlineData("not-a-url")]
    public async Task SearchAsync_DisallowedBaseUrl_MakesNoRequest(string baseUrl)
    {
        var handler = new MusicHoarderzQueueHandler((_, _) => JsonResponse("{\"results\":[]}"));
        var client = CreateClient(handler, baseUrl);

        var results = await client.SearchAsync(new CoverSearchQuery("A", "B", null, "DE"), CancellationToken.None);

        Assert.Empty(results);
        Assert.Empty(handler.Requests);
        Assert.Contains(client.RecentErrors, e => e.Code == "InvalidBaseUrl");
    }

    [Fact]
    public async Task SearchAsync_CachesRepeatedQueries()
    {
        var calls = 0;
        var handler = new MusicHoarderzQueueHandler((_, _) =>
        {
            calls++;
            return JsonResponse("[{\"url\":\"https://covers.example.com/a.jpg\"}]");
        });
        var client = CreateClient(handler);
        var query = new CoverSearchQuery("A", "B", null, "DE");

        var first = await client.SearchAsync(query, CancellationToken.None);
        var second = await client.SearchAsync(query, CancellationToken.None);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SearchAsync_FollowsHttpsRedirect()
    {
        var calls = 0;
        var handler = new MusicHoarderzQueueHandler((request, _) =>
        {
            calls++;
            if (calls == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://covers.example.com/final", UriKind.Absolute);
                return redirect;
            }

            return JsonResponse("[{\"url\":\"https://covers.example.com/a.jpg\"}]");
        });
        var client = CreateClient(handler);

        var results = await client.SearchAsync(new CoverSearchQuery("A", "B", null, "DE"), CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task SearchAsync_PrivateRedirectTarget_IsBlocked()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://127.0.0.1/evil", UriKind.Absolute);
            return redirect;
        });
        var client = CreateClient(handler);

        var results = await client.SearchAsync(new CoverSearchQuery("A", "B", null, "DE"), CancellationToken.None);

        Assert.Empty(results);
        Assert.Single(handler.Requests);
        Assert.Contains(client.RecentErrors, e => e.Code == "RedirectBlocked");
    }

    [Fact]
    public async Task SearchAsync_QueryCountry_OverridesDefault()
    {
        HttpRequestMessage? captured = null;
        var handler = new MusicHoarderzQueueHandler((request, _) =>
        {
            captured = request;
            return JsonResponse("{\"results\":[]}");
        });
        var client = CreateClient(handler);

        _ = await client.SearchAsync(new CoverSearchQuery("A", "B", null, "FR"), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Contains("country=FR", captured!.RequestUri!.Query);
    }

    [Fact]
    public async Task SearchAsync_PropagatesCancellation()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) => JsonResponse("{\"results\":[]}"));
        var client = CreateClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            client.SearchAsync(new CoverSearchQuery("A", "B", null, "DE"), cts.Token));
    }

    private static MusicHoarderzHttpClient CreateClient(MusicHoarderzQueueHandler handler, string? baseUrl = null)
    {
        var factory = new MusicHoarderzTestHttpClientFactory(handler);
        return new MusicHoarderzHttpClient(
            factory,
            MusicHoarderzNoopLogger<MusicHoarderzHttpClient>.Instance,
            baseUrl ?? BaseUrl,
            "DE");
    }

    private static HttpResponseMessage JsonResponse(string json, string mediaType = "application/json")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent(json, Encoding.UTF8, mediaType);
        return response;
    }
}
