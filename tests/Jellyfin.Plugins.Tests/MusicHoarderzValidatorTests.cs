using System.Net;
using System.Text;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public class MusicHoarderzValidatorTests
{
    private const string PublicHostUrl = "https://covers.example.com/cover.png";

    [Fact]
    public async Task ValidateAsync_ValidPngAboveMinimum_ReturnsBytes()
    {
        var png = BuildPng(1200, 1200);
        var handler = new MusicHoarderzQueueHandler((_, _) => ImageResponse(png, "image/png"));
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync(PublicHostUrl, 1000, 1000, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1200, result!.Width);
        Assert.Equal(1200, result.Height);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(png, result.Content.ToArray());
    }

    [Fact]
    public async Task ValidateAsync_ValidJpegAboveMinimum_ReturnsBytes()
    {
        var jpeg = BuildJpeg(1200, 1200);
        var handler = new MusicHoarderzQueueHandler((_, _) => ImageResponse(jpeg, "image/jpeg"));
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync("https://covers.example.com/cover.jpg", 1000, 1000, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1200, result!.Width);
        Assert.Equal(1200, result.Height);
    }

    [Fact]
    public async Task ValidateAsync_ValidGifAboveMinimum_ReturnsBytes()
    {
        var gif = BuildGif(1200, 1200);
        var handler = new MusicHoarderzQueueHandler((_, _) => ImageResponse(gif, "image/gif"));
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync("https://covers.example.com/cover.gif", 1000, 1000, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1200, result!.Width);
        Assert.Equal(1200, result.Height);
    }

    [Fact]
    public async Task ValidateAsync_ValidWebPAboveMinimum_ReturnsBytes()
    {
        var webp = BuildWebP(1200, 1200);
        var handler = new MusicHoarderzQueueHandler((_, _) => ImageResponse(webp, "image/webp"));
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync("https://covers.example.com/cover.webp", 1000, 1000, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1200, result!.Width);
        Assert.Equal(1200, result.Height);
    }

    [Fact]
    public async Task ValidateAsync_BelowMinimum_ReturnsNull()
    {
        var png = BuildPng(1, 1);
        var handler = new MusicHoarderzQueueHandler((_, _) => ImageResponse(png, "image/png"));
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync(PublicHostUrl, 1000, 1000, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_Oversize_ReturnsNull()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) =>
            ImageResponse(new byte[CoverImageValidator.MaxImageBytes + 1], "image/jpeg"));
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync(PublicHostUrl, 1, 1, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_WrongMime_ReturnsNull()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) =>
            ImageResponse(Encoding.ASCII.GetBytes("<html><body>nope</body></html>"), "text/html"));
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync(PublicHostUrl, 1, 1, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_HtmlDisguisedAsImage_ReturnsNull()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) =>
            ImageResponse(Encoding.ASCII.GetBytes("<html><body>nope</body></html>"), "image/jpeg"));
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync(PublicHostUrl, 1, 1, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_JsonDisguisedAsImage_ReturnsNull()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) =>
            ImageResponse(Encoding.ASCII.GetBytes("{\"error\":\"nope\"}"), "image/png"));
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync(PublicHostUrl, 1, 1, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_PrivateIp_MakesNoRequest()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) =>
            ImageResponse(BuildPng(1200, 1200), "image/png"));
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync("https://192.168.1.10/cover.png", 1, 1, CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ValidateAsync_LocalhostRedirect_IsBlocked()
    {
        var calls = 0;
        var handler = new MusicHoarderzQueueHandler((_, _) =>
        {
            calls++;
            if (calls == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = new Uri("https://127.0.0.1/evil.png", UriKind.Absolute);
                return redirect;
            }

            return ImageResponse(BuildPng(1200, 1200), "image/png");
        });
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync(PublicHostUrl, 1, 1, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ValidateAsync_PrivateDns_MakesNoRequest()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) =>
            ImageResponse(BuildPng(1200, 1200), "image/png"));
        var validator = new CoverImageValidator(
            new MusicHoarderzTestHttpClientFactory(handler),
            MusicHoarderzNoopLogger<CoverImageValidator>.Instance, new MusicHoarderzTestImageDecoder(),
            (_, _) => Task.FromResult(new[] { System.Net.IPAddress.Parse("10.0.0.9") }));

        var result = await validator.ValidateAsync(PublicHostUrl, 1, 1, CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ValidateAsync_NonHttps_ReturnsNull()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) =>
            ImageResponse(BuildPng(1200, 1200), "image/png"));
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync("http://covers.example.com/cover.png", 1, 1, CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    private static CoverImageValidator CreateValidator(MusicHoarderzQueueHandler handler)
    {
        return new CoverImageValidator(
            new MusicHoarderzTestHttpClientFactory(handler),
            MusicHoarderzNoopLogger<CoverImageValidator>.Instance, new MusicHoarderzTestImageDecoder(),
            (_, _) => Task.FromResult(new[] { System.Net.IPAddress.Parse("93.184.216.34") }));
    }

    [Fact]
    public async Task MismatchedImageMime_IsRejected()
    {
        var validator = CreateValidator(new MusicHoarderzQueueHandler((_, _) => ImageResponse(BuildPng(1200, 1200), "image/jpeg")));

        Assert.Null(await validator.ValidateAsync(PublicHostUrl, 1, 1, CancellationToken.None));
    }

    [Fact]
    public async Task CorruptPngSignature_IsRejected()
    {
        var bytes = BuildPng(1200, 1200);
        bytes[7] = 0;
        var validator = CreateValidator(new MusicHoarderzQueueHandler((_, _) => ImageResponse(bytes, "image/png")));

        Assert.Null(await validator.ValidateAsync(PublicHostUrl, 1, 1, CancellationToken.None));
    }

    [Fact]
    public async Task ExcessiveDecodedDimensions_AreRejected()
    {
        var validator = CreateValidator(new MusicHoarderzQueueHandler((_, _) => ImageResponse(BuildPng(50000, 50000), "image/png")));

        Assert.Null(await validator.ValidateAsync(PublicHostUrl, 1, 1, CancellationToken.None));
    }

    [Fact]
    public async Task DnsCancellation_Propagates()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) => throw new InvalidOperationException("Unexpected HTTP"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var validator = new CoverImageValidator(new MusicHoarderzTestHttpClientFactory(handler),
            MusicHoarderzNoopLogger<CoverImageValidator>.Instance, new MusicHoarderzTestImageDecoder(),
            (_, token) => Task.FromCanceled<IPAddress[]>(token));

        await Assert.ThrowsAsync<OperationCanceledException>(() => validator.ValidateAsync(PublicHostUrl, 1, 1, cancelled.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TruncatedJpeg_ReturnsNullWithoutThrowing()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xC0, 0, 7, 8, 0xFF, 0xD9 };
        var validator = CreateValidator(new MusicHoarderzQueueHandler((_, _) => ImageResponse(bytes, "image/jpeg")));

        Assert.Null(await validator.ValidateAsync(PublicHostUrl, 1, 1, CancellationToken.None));
    }

    private static HttpResponseMessage ImageResponse(byte[] bytes, string mediaType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        response.Content = content;
        return response;
    }

    private static byte[] BuildPng(int width, int height)
    {
        if (width == 1 && height == 1)
        {
            return MusicHoarderzImageFixtures.SmallPng();
        }

        var raw = MusicHoarderzImageFixtures.Png();
        if (width != 1200 || height != 1200)
        {
            WriteBigEndian32(raw, 16, width);
            WriteBigEndian32(raw, 20, height);
        }

        return raw;
    }

    private static byte[] BuildJpeg(int width, int height) => MusicHoarderzImageFixtures.Jpeg();

    private static byte[] BuildGif(int width, int height) => MusicHoarderzImageFixtures.Gif();

    private static byte[] BuildWebP(int width, int height) => MusicHoarderzImageFixtures.WebP();

    private static void WriteBigEndian32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)((value >> 24) & 0xFF);
        buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }
}
