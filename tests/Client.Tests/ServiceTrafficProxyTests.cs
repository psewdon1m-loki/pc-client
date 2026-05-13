using Client.Core;

namespace Client.Tests;

public sealed class ServiceTrafficProxyTests
{
    [Fact]
    public void DisabledProxyBypassesExternalRequests()
    {
        var proxy = new ServiceTrafficProxy();
        var destination = new Uri("https://example.com/manifest.json");

        Assert.True(proxy.IsBypassed(destination));
        Assert.Same(destination, proxy.GetProxy(destination));
        Assert.False(proxy.IsEnabled);
    }

    [Fact]
    public void EnabledProxyRoutesExternalRequestsThroughLocalHttpProxy()
    {
        var proxy = new ServiceTrafficProxy();
        var destination = new Uri("https://example.com/manifest.json");

        proxy.EnableHttpProxy(18089);

        Assert.False(proxy.IsBypassed(destination));
        Assert.Equal(new Uri("http://127.0.0.1:18089/"), proxy.GetProxy(destination));
        Assert.True(proxy.IsEnabled);
    }

    [Fact]
    public void EnabledProxyStillBypassesLoopbackRequests()
    {
        var proxy = new ServiceTrafficProxy();
        proxy.EnableHttpProxy(18089);

        var loopback = new Uri("http://127.0.0.1:18080/manifest.json");
        var localhost = new Uri("http://localhost:18080/manifest.json");

        Assert.True(proxy.IsBypassed(loopback));
        Assert.True(proxy.IsBypassed(localhost));
        Assert.Same(loopback, proxy.GetProxy(loopback));
        Assert.Same(localhost, proxy.GetProxy(localhost));
    }
}
