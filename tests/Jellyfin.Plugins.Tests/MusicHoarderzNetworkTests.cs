using System.Net;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public class MusicHoarderzNetworkTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.9.8.7")]
    [InlineData("10.0.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("198.19.1.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.1.2.3")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:a00:1::1")]
    public void NonPublicAddresses_AreBlocked(string address)
    {
        Assert.True(NetworkGuard.IsPrivateOrLoopback(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:4860:4860::8888")]
    public void PublicAddresses_AreAllowed(string address)
    {
        Assert.False(NetworkGuard.IsPrivateOrLoopback(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("https://[::1]/a.png")]
    [InlineData("https://[::ffff:127.0.0.1]/a.png")]
    [InlineData("https://service.internal/a.png")]
    [InlineData("https://metadata/a.png")]
    [InlineData("https://user:secret@covers.example.com/a.png")]
    [InlineData("https://covers.example.com/a.png#secret")]
    public void UnsafeUris_AreRejected(string uri)
    {
        Assert.False(NetworkGuard.IsPublicHttps(new Uri(uri)));
    }

    [Fact]
    public void RegisteredTransports_DisableAutomaticRedirectsAndPinDns()
    {
        var services = new ServiceCollection();
        new Jellyfin.Plugin.MusicHoarderzProvider.PluginServiceRegistrator().RegisterServices(services, null!);
        using var provider = services.BuildServiceProvider();
        foreach (var name in new[] { "musichoarderz", "musichoarderz-images" })
        {
            var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(name);
            while (handler is DelegatingHandler delegating)
            {
                handler = delegating.InnerHandler!;
            }

            var transport = Assert.IsType<SocketsHttpHandler>(handler);
            Assert.False(transport.AllowAutoRedirect);
            Assert.False(transport.UseProxy);
            Assert.False(transport.UseCookies);
            Assert.NotNull(transport.ConnectCallback);
        }
    }

    [Fact]
    public async Task ProductionTransport_BlocksLoopbackBeforeConnecting()
    {
        using var client = new HttpClient(NetworkGuard.CreateHandler());

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://127.0.0.1/"));
    }
}
