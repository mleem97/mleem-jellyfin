using System.Net;
using System.Reflection;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Drawing;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public class MusicHoarderzDecoderTests
{
    [Fact]
    public async Task FailedPixelDecode_RejectsOtherwiseValidContainer()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(MusicHoarderzImageFixtures.Png()) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        var decoder = new MusicHoarderzTestImageDecoder { Result = false };
        var validator = new CoverImageValidator(new MusicHoarderzTestHttpClientFactory(new MusicHoarderzQueueHandler((_, _) => response)),
            MusicHoarderzNoopLogger<CoverImageValidator>.Instance, decoder, (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

        Assert.Null(await validator.ValidateAsync("https://covers.example.com/a.png", 1, 1, CancellationToken.None));
        Assert.Equal(1, decoder.Calls);
    }

    [Fact]
    public async Task OversizedGifFrame_IsRejectedBeforeHostDecoder()
    {
        var gif = MusicHoarderzImageFixtures.Gif();
        gif[6] = 1;
        gif[7] = 0;
        gif[8] = 1;
        gif[9] = 0;
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(gif) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/gif");
        var decoder = new MusicHoarderzTestImageDecoder();
        var validator = new CoverImageValidator(new MusicHoarderzTestHttpClientFactory(new MusicHoarderzQueueHandler((_, _) => response)),
            MusicHoarderzNoopLogger<CoverImageValidator>.Instance, decoder, (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

        Assert.Null(await validator.ValidateAsync("https://covers.example.com/a.gif", 1, 1, CancellationToken.None));
        Assert.Equal(0, decoder.Calls);
    }

    [Theory]
    [InlineData("valid-hash", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public async Task HostDecoder_RequiresDecodedPixelsAndCleansTemporaryFile(string? hash, bool expected)
    {
        using var directory = new TemporaryDirectory();
        var content = MusicHoarderzImageFixtures.Png();
        var paths = MusicHoarderzInterfaceProxy.Create<IApplicationPaths>((method, _) =>
            method.Name == "get_TempDirectory" ? directory.Path : throw new NotSupportedException());
        var calls = 0;
        var encoder = MusicHoarderzInterfaceProxy.Create<IImageEncoder>((method, arguments) =>
        {
            Assert.Equal("GetImageBlurHash", method.Name);
            calls++;
            var path = Assert.IsType<string>(arguments![2]);
            Assert.Equal(content, File.ReadAllBytes(path));
            Assert.EndsWith(".png", path);
            if (hash is null)
            {
                throw new InvalidDataException("Invalid compressed pixels");
            }

            return hash;
        });
        var decoder = new JellyfinCoverImageDecoder(encoder, paths);

        Assert.Equal(expected, await decoder.CanDecodeAsync(content, "image/png", CancellationToken.None));
        Assert.Equal(1, calls);
        Assert.Empty(Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories));
    }
}

public class MusicHoarderzInterfaceProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = Create<T, MusicHoarderzInterfaceProxy>();
        ((MusicHoarderzInterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
}
