using System.Net;
using System.Net.Http;
using Client.Core;

namespace Client.App.Win.Services;

public sealed class ProxyConnectivityVerifier
{
    private static readonly Uri[] ProbeUris =
    [
        new("https://api.ipify.org"),
        new("https://checkip.amazonaws.com"),
        new("https://ifconfig.me/ip")
    ];

    public async Task<OperationResult<ProxyConnectivityProbe>> VerifySystemProxyAsync(
        int httpPort,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        foreach (var uri in ProbeUris)
        {
            var direct = await ReadIpAsync(uri, ProxyProbeMode.Direct, httpPort, cancellationToken).ConfigureAwait(false);
            var explicitProxy = await ReadIpAsync(uri, ProxyProbeMode.ExplicitLocalProxy, httpPort, cancellationToken).ConfigureAwait(false);
            var systemProxy = await ReadIpAsync(uri, ProxyProbeMode.WindowsSystemProxy, httpPort, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(explicitProxy))
            {
                errors.Add($"{uri.Host}: local proxy did not return external IP");
                continue;
            }

            if (string.IsNullOrWhiteSpace(systemProxy))
            {
                errors.Add($"{uri.Host}: Windows system proxy did not return external IP");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(direct)
                && string.Equals(direct, explicitProxy, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{uri.Host}: local proxy external IP equals direct IP ({direct})");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(direct)
                && string.Equals(direct, systemProxy, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{uri.Host}: Windows system proxy still uses direct IP ({direct})");
                continue;
            }

            var probe = new ProxyConnectivityProbe(uri.Host, direct, explicitProxy, systemProxy);
            return OperationResult<ProxyConnectivityProbe>.Ok(
                probe,
                $"Proxy verified | target={probe.Target} | direct={probe.DirectIp ?? "unknown"} | local-proxy={probe.ExplicitProxyIp} | system-proxy={probe.SystemProxyIp}");
        }

        return OperationResult<ProxyConnectivityProbe>.Fail("Proxy verification failed. " + string.Join(" | ", errors));
    }

    private static async Task<string?> ReadIpAsync(
        Uri uri,
        ProxyProbeMode mode,
        int httpPort,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler();
        switch (mode)
        {
            case ProxyProbeMode.Direct:
                handler.UseProxy = false;
                break;
            case ProxyProbeMode.ExplicitLocalProxy:
                handler.UseProxy = true;
                handler.Proxy = new WebProxy($"http://127.0.0.1:{httpPort}");
                break;
            case ProxyProbeMode.WindowsSystemProxy:
                handler.UseProxy = true;
                handler.Proxy = WebRequest.GetSystemWebProxy();
                break;
        }

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        try
        {
            var value = await client.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            return value.Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private enum ProxyProbeMode
    {
        Direct,
        ExplicitLocalProxy,
        WindowsSystemProxy
    }
}

public sealed record ProxyConnectivityProbe(
    string Target,
    string? DirectIp,
    string ExplicitProxyIp,
    string SystemProxyIp);
