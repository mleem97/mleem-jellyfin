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
            MusicHoarderzNoopLogger<CoverImageValidator>.Instance,
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
            MusicHoarderzNoopLogger<CoverImageValidator>.Instance,
            (_, _) => Task.FromResult(new[] { System.Net.IPAddress.Parse("93.184.216.34") }));
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
        var raw = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        WriteBigEndian32(raw, 16, width);
        WriteBigEndian32(raw, 20, height);
        return raw;
    }

    private static byte[] BuildJpeg(int width, int height)
    {
        var bytes = new byte[]
        {
            0xFF, 0xD8,
            0xFF, 0xC0, 0x00, 0x0B, 0x08,
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x01, 0x11, 0x00,
            0xFF, 0xD9,
        };
        bytes[7] = (byte)((height >> 8) & 0xFF);
        bytes[8] = (byte)(height & 0xFF);
        bytes[9] = (byte)((width >> 8) & 0xFF);
        bytes[10] = (byte)(width & 0xFF);
        return bytes;
    }

    private static byte[] BuildGif(int width, int height)
    {
        var bytes = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x00, 0x00, 0x00, 0x00, 0x3B };
        bytes[6] = (byte)(width & 0xFF);
        bytes[7] = (byte)((width >> 8) & 0xFF);
        bytes[8] = (byte)(height & 0xFF);
        bytes[9] = (byte)((height >> 8) & 0xFF);
        return bytes;
    }

    private static byte[] BuildWebP(int width, int height)
    {
        var bytes = new byte[30];
        bytes[0] = (byte)'R';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'F';
        bytes[3] = (byte)'F';
        bytes[8] = (byte)'W';
        bytes[9] = (byte)'E';
        bytes[10] = (byte)'B';
        bytes[11] = (byte)'P';
        bytes[12] = (byte)'V';
        bytes[13] = (byte)'P';
        bytes[14] = (byte)'8';
        bytes[15] = (byte)'X';
        var minusOne = width - 1;
        bytes[24] = (byte)(minusOne & 0xFF);
        bytes[25] = (byte)((minusOne >> 8) & 0xFF);
        bytes[26] = (byte)((minusOne >> 16) & 0xFF);
        minusOne = height - 1;
        bytes[27] = (byte)(minusOne & 0xFF);
        bytes[28] = (byte)((minusOne >> 8) & 0xFF);
        bytes[29] = (byte)((minusOne >> 16) & 0xFF);
        return bytes;
    }

    private static void WriteBigEndian32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)((value >> 24) & 0xFF);
        buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }
}
