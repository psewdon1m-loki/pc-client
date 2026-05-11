using System.Diagnostics;
using System.Net;
using Client.Core;
using Client.Platform.Windows;
using Client.Profiles;
using Client.Routing;
using Client.Transport.Xray;

var subscriptionUrl = args.FirstOrDefault(arg => arg.StartsWith("http", StringComparison.OrdinalIgnoreCase))
    ?? "https://loki-panel.shmoza.net:8000/sub/SzlYMlI3TDVNNFE4WjFQQiwxNzc4MzMyNjQ3lGVgfDSRJi";
var allowInvalidTls = args.Contains("--allow-invalid-subscription-tls", StringComparer.OrdinalIgnoreCase)
    || subscriptionUrl.Contains("loki-panel.shmoza.net", StringComparison.OrdinalIgnoreCase);
var testSystemProxy = args.Contains("--test-system-proxy", StringComparer.OrdinalIgnoreCase);

var root = FindRoot(AppContext.BaseDirectory);
var runtime = Path.Combine(root, "artifacts", "smoke", "runtime");
Directory.CreateDirectory(runtime);
PrepareRuntime(root, runtime);

Console.WriteLine($"Subscription: {subscriptionUrl}");
Console.WriteLine($"Allow invalid subscription TLS: {allowInvalidTls}");

var fetched = await SubscriptionClient.Create(allowInvalidTls).FetchAsync(subscriptionUrl);
if (!fetched.Success || fetched.Value is null)
{
    Console.Error.WriteLine(fetched.Message);
    return 10;
}

Console.WriteLine($"Profiles parsed: {fetched.Value.Count}");
if (fetched.Value.Count != 2)
{
    Console.Error.WriteLine($"Expected 2 profiles, got {fetched.Value.Count}.");
    return 11;
}

var profileIndex = 0;
foreach (var profile in fetched.Value)
{
    profileIndex++;
    var httpPort = 18090 + profileIndex;
    var socksPort = 18190 + profileIndex;
    Console.WriteLine($"Testing profile {profileIndex}: {profile.Name} {profile.Host}:{profile.Port}");

    var settings = new AppSettings
    {
        HttpPort = httpPort,
        SocksPort = socksPort,
        RoutingMode = RoutingModes.RussiaSmart
    };
    var config = new XrayConfigRenderer().Render(
        profile,
        settings,
        new RussiaRoutingPreset().BuildSmartRules(),
        Path.Combine(runtime, $"xray-access-{profileIndex}.log"),
        Path.Combine(runtime, $"xray-error-{profileIndex}.log"));
    var configPath = Path.Combine(runtime, $"config-{profileIndex}.json");
    await File.WriteAllTextAsync(configPath, config);

    var validation = new XrayConfigValidator().ValidateJson(config);
    if (!validation.Success)
    {
        Console.Error.WriteLine(validation.Message);
        return 12;
    }

    var xrayTest = await RunXrayTestAsync(runtime, configPath);
    if (xrayTest != 0)
    {
        return xrayTest;
    }

    var manager = new XrayProcessManager();
    var started = await manager.StartAsync(new XrayRuntimeOptions
    {
        XrayExecutablePath = Path.Combine(runtime, "xray.exe"),
        WorkingDirectory = runtime,
        ConfigPath = configPath,
        HttpPort = httpPort,
        SocksPort = socksPort
    });

    if (!started.Success)
    {
        Console.Error.WriteLine(started.Message);
        await manager.StopAsync();
        return 20 + profileIndex;
    }

    try
    {
        if (testSystemProxy && profileIndex == 1)
        {
            var proxyService = new SystemProxyService();
            var previous = proxyService.Read();
            try
            {
                var enabled = proxyService.EnableHttpProxy(httpPort, socksPort);
                if (!enabled.Success)
                {
                    Console.Error.WriteLine(enabled.Message);
                    return 30;
                }

                var proxyState = proxyService.Read();
                var systemProxyCheck = await CheckSystemProxyAsync(httpPort, proxyState);
                if (!systemProxyCheck.Success)
                {
                    Console.Error.WriteLine(systemProxyCheck.Message);
                    return 32;
                }

                Console.WriteLine(systemProxyCheck.Message);
            }
            finally
            {
                var restored = proxyService.Restore(previous);
                if (!restored.Success)
                {
                    Console.Error.WriteLine(restored.Message);
                }
            }

            Console.WriteLine("System proxy enable/restore OK.");
        }

        var proxyCheck = await CheckProxyAsync(httpPort);
        if (!proxyCheck.Success)
        {
            Console.Error.WriteLine(proxyCheck.Message);
            return 40 + profileIndex;
        }

        Console.WriteLine(proxyCheck.Message);
    }
    finally
    {
        await manager.StopAsync();
    }
}

Console.WriteLine("Real subscription smoke OK.");
return 0;

static async Task<int> RunXrayTestAsync(string runtime, string configPath)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = Path.Combine(runtime, "xray.exe"),
        Arguments = $"run -test -c \"{configPath}\"",
        WorkingDirectory = runtime,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var process = Process.Start(startInfo);
    if (process is null)
    {
        Console.Error.WriteLine("Failed to start xray -test.");
        return 13;
    }

    var output = await process.StandardOutput.ReadToEndAsync();
    var error = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    Console.Write(output);
    Console.Error.Write(error);
    return process.ExitCode == 0 ? 0 : 14;
}

static async Task<OperationResult> CheckProxyAsync(int httpPort)
{
    var handler = new HttpClientHandler
    {
        Proxy = new WebProxy($"http://127.0.0.1:{httpPort}"),
        UseProxy = true
    };

    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    var targets = new[]
    {
        "https://www.google.com/generate_204",
        "https://www.gstatic.com/generate_204",
        "https://www.cloudflare.com/cdn-cgi/trace"
    };

    var errors = new List<string>();
    foreach (var target in targets)
    {
        try
        {
            using var response = await client.GetAsync(target);
            if ((int)response.StatusCode is >= 200 and < 400)
            {
                return OperationResult.Ok($"Proxy HTTP check OK: {target} -> {(int)response.StatusCode}");
            }

            errors.Add($"{target}: HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            errors.Add($"{target}: {ex.Message}");
        }
    }

    return OperationResult.Fail("Proxy HTTP check failed. " + string.Join(" | ", errors));
}

static async Task<OperationResult> CheckSystemProxyAsync(int httpPort, SystemProxyState proxyState)
{
    var expectedProxy = $"127.0.0.1:{httpPort}";
    if (proxyState.WinHttpProxy?.Contains(expectedProxy, StringComparison.OrdinalIgnoreCase) != true)
    {
        return OperationResult.Fail($"WinHTTP proxy was not applied. value={proxyState.WinHttpProxy ?? "<empty>"}");
    }

    var direct = await ReadIpAsync(useProxy: false, proxy: null);
    var explicitProxy = await ReadIpAsync(useProxy: true, proxy: new WebProxy($"http://127.0.0.1:{httpPort}"));
    var systemProxy = await ReadIpAsync(useProxy: true, proxy: new WebProxy($"http://127.0.0.1:{httpPort}"));

    if (string.IsNullOrWhiteSpace(explicitProxy))
    {
        return OperationResult.Fail("Explicit local proxy IP check failed.");
    }

    if (string.IsNullOrWhiteSpace(systemProxy))
    {
        return OperationResult.Fail("Windows system proxy IP check failed.");
    }

    if (!string.IsNullOrWhiteSpace(direct) && string.Equals(direct, explicitProxy, StringComparison.OrdinalIgnoreCase))
    {
        return OperationResult.Fail($"Explicit proxy returned direct IP: {direct}");
    }

    if (!string.IsNullOrWhiteSpace(direct) && string.Equals(direct, systemProxy, StringComparison.OrdinalIgnoreCase))
    {
        return OperationResult.Fail($"Configured Windows proxy returned direct IP: {direct}");
    }

    return OperationResult.Ok($"System proxy IP check OK: direct={direct ?? "unknown"}, explicit={explicitProxy}, system={systemProxy}");
}

static async Task<string?> ReadIpAsync(bool useProxy, IWebProxy? proxy)
{
    using var handler = new HttpClientHandler
    {
        UseProxy = useProxy
    };
    if (proxy is not null)
    {
        handler.Proxy = proxy;
    }

    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    try
    {
        return (await client.GetStringAsync("https://api.ipify.org")).Trim();
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        return null;
    }
}

static string FindRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Client.sln")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
}

static void PrepareRuntime(string root, string runtime)
{
    Copy(Path.Combine(root, "src", "Client.App.Win", "Assets", "geo", "geoip.dat"), Path.Combine(runtime, "geoip.dat"));
    Copy(Path.Combine(root, "src", "Client.App.Win", "Assets", "geo", "geosite.dat"), Path.Combine(runtime, "geosite.dat"));
    Copy(Path.Combine(root, "src", "Client.App.Win", "Assets", "xray", "xray.exe"), Path.Combine(runtime, "xray.exe"));
}

static void Copy(string source, string target)
{
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.Copy(source, target, overwrite: true);
}
