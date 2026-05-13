using System.Net;
using System.Threading;

namespace Client.Core;

public sealed class ServiceTrafficProxy : IWebProxy
{
    private Uri? _proxyUri;

    public ICredentials? Credentials { get; set; }

    public Uri? CurrentProxy => Volatile.Read(ref _proxyUri);

    public bool IsEnabled => CurrentProxy is not null;

    public void EnableHttpProxy(int httpPort)
    {
        Volatile.Write(ref _proxyUri, new Uri($"http://127.0.0.1:{httpPort}"));
    }

    public void Disable()
    {
        Volatile.Write(ref _proxyUri, null);
    }

    public Uri GetProxy(Uri destination)
    {
        var proxy = CurrentProxy;
        return proxy is not null && !IsLocalEndpoint(destination)
            ? proxy
            : destination;
    }

    public bool IsBypassed(Uri host)
    {
        return CurrentProxy is null || IsLocalEndpoint(host);
    }

    private static bool IsLocalEndpoint(Uri uri)
    {
        return uri.IsLoopback
            || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }
}
