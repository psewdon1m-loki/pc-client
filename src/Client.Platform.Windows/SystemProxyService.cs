using System.Runtime.InteropServices;
using Client.Core;
using Microsoft.Win32;

namespace Client.Platform.Windows;

public sealed class SystemProxyService
{
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string EnvironmentKey = @"Environment";
    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;
    private const int InternetOptionProxySettingsChanged = 95;
    private const int HwndBroadcast = 0xffff;
    private const int WmSettingChange = 0x001a;
    private const int SmtoAbortIfHung = 0x0002;
    private const int WinHttpAccessTypeNoProxy = 1;
    private const int WinHttpAccessTypeNamedProxy = 3;

    public SystemProxyState Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: false);
        var winHttp = ReadWinHttp();
        return new SystemProxyState
        {
            ProxyEnabled = Convert.ToInt32(key?.GetValue("ProxyEnable") ?? 0) == 1,
            ProxyServer = key?.GetValue("ProxyServer") as string,
            ProxyOverride = key?.GetValue("ProxyOverride") as string,
            AutoConfigUrl = key?.GetValue("AutoConfigURL") as string,
            WinHttpAccessType = winHttp?.AccessType,
            WinHttpProxy = winHttp?.Proxy,
            WinHttpProxyBypass = winHttp?.ProxyBypass,
            HttpProxyEnvironment = GetUserEnvironmentValue("HTTP_PROXY"),
            HttpsProxyEnvironment = GetUserEnvironmentValue("HTTPS_PROXY"),
            AllProxyEnvironment = GetUserEnvironmentValue("ALL_PROXY"),
            NoProxyEnvironment = GetUserEnvironmentValue("NO_PROXY")
        };
    }

    public OperationResult EnableHttpProxy(int httpPort, int socksPort, string? bypass = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
            if (key is null)
            {
                return OperationResult.Fail("Не удалось открыть настройки Windows system proxy.");
            }

            var proxyServer = BuildLocalProxyServer(httpPort, socksPort);
            var winHttpProxyServer = BuildLocalHttpProxyServer(httpPort);
            var proxyBypass = bypass ?? BuildDefaultBypass();
            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);
            key.SetValue("ProxyOverride", proxyBypass, RegistryValueKind.String);
            key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
            SetWinHttpProxy(winHttpProxyServer, proxyBypass);
            RunProxySideEffects(() =>
            {
                SetProxyEnvironment(httpPort, proxyBypass);
                NotifyWindows();
            });
            return OperationResult.Ok("System proxy включен.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return OperationResult.Fail($"Не удалось включить system proxy: {ex.Message}");
        }
    }

    public bool IsEnabledForLocalHttpProxy(int httpPort)
    {
        var state = Read();
        return state.ProxyEnabled
            && IsLocalHttpProxy(state.ProxyServer, httpPort)
            && IsLocalHttpProxy(state.WinHttpProxy, httpPort);
    }

    public OperationResult DisableLocalHttpProxy(int httpPort)
    {
        var state = Read();
        if ((!state.ProxyEnabled || !IsLocalHttpProxy(state.ProxyServer, httpPort))
            && !IsLocalHttpProxy(state.WinHttpProxy, httpPort))
        {
            return OperationResult.Ok("System proxy is not owned by Loki.");
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
            if (key is null)
            {
                return OperationResult.Fail("Не удалось открыть настройки Windows system proxy.");
            }

            key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            key.DeleteValue("ProxyServer", throwOnMissingValue: false);
            key.DeleteValue("ProxyOverride", throwOnMissingValue: false);
            key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
            if (IsLocalHttpProxy(state.WinHttpProxy, httpPort))
            {
                ClearWinHttpProxy();
            }

            RunProxySideEffects(() =>
            {
                ClearLocalProxyEnvironment(httpPort);
                NotifyWindows();
            });
            return OperationResult.Ok("Loki system proxy disabled.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return OperationResult.Fail($"Не удалось выключить Loki system proxy: {ex.Message}");
        }
    }

    public bool IsEnabledForLokiLocalHttpProxy()
    {
        var state = Read();
        return (state.ProxyEnabled && IsLokiLocalHttpProxy(state.ProxyServer))
            || IsLokiLocalHttpProxy(state.WinHttpProxy);
    }

    public OperationResult DisableLokiLocalHttpProxy()
    {
        var state = Read();
        if ((!state.ProxyEnabled || !IsLokiLocalHttpProxy(state.ProxyServer))
            && !IsLokiLocalHttpProxy(state.WinHttpProxy)
            && !HasLokiLocalProxyEnvironment(state))
        {
            return OperationResult.Ok("System proxy is not owned by Loki.");
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
            if (key is null)
            {
                return OperationResult.Fail("Не удалось открыть настройки Windows system proxy.");
            }

            key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            key.DeleteValue("ProxyServer", throwOnMissingValue: false);
            key.DeleteValue("ProxyOverride", throwOnMissingValue: false);
            key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
            if (IsLokiLocalHttpProxy(state.WinHttpProxy))
            {
                ClearWinHttpProxy();
            }

            RunProxySideEffects(() =>
            {
                ClearLokiProxyEnvironment();
                NotifyWindows();
            });
            return OperationResult.Ok("Loki system proxy disabled.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return OperationResult.Fail($"Не удалось выключить Loki system proxy: {ex.Message}");
        }
    }

    public bool HasLocalProxyTraces()
    {
        var state = Read();
        return IsLocalProxy(state.ProxyServer)
            || IsLocalProxy(state.AutoConfigUrl)
            || IsLocalProxy(state.WinHttpProxy)
            || IsLocalProxy(state.HttpProxyEnvironment)
            || IsLocalProxy(state.HttpsProxyEnvironment)
            || IsLocalProxy(state.AllProxyEnvironment);
    }

    public bool HasLokiLocalProxyTraces(SystemProxyState? state = null)
    {
        state ??= Read();
        return IsLokiLocalHttpProxy(state.ProxyServer)
            || IsLokiLocalHttpProxy(state.WinHttpProxy)
            || HasLokiLocalProxyEnvironment(state);
    }

    public OperationResult DisableLocalProxyTraces()
    {
        var state = Read();
        if (!IsLocalProxy(state.ProxyServer)
            && !IsLocalProxy(state.AutoConfigUrl)
            && !IsLocalProxy(state.WinHttpProxy)
            && !IsLocalProxy(state.HttpProxyEnvironment)
            && !IsLocalProxy(state.HttpsProxyEnvironment)
            && !IsLocalProxy(state.AllProxyEnvironment))
        {
            return OperationResult.Ok("No local proxy traces found.");
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
            if (key is null)
            {
                return OperationResult.Fail("Cannot open Windows system proxy settings.");
            }

            key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            key.DeleteValue("ProxyServer", throwOnMissingValue: false);
            key.DeleteValue("ProxyOverride", throwOnMissingValue: false);
            key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
            if (IsLocalProxy(state.WinHttpProxy))
            {
                ClearWinHttpProxy();
            }

            RunProxySideEffects(() =>
            {
                ClearLocalProxyEnvironment();
                NotifyWindows();
            });
            return OperationResult.Ok("Local proxy traces disabled.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return OperationResult.Fail($"Cannot clear local proxy traces: {ex.Message}");
        }
    }

    public OperationResult Restore(SystemProxyState state)
    {
        if (HasLokiLocalProxyTraces(state))
        {
            return DisableLokiLocalHttpProxy();
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
            if (key is null)
            {
                return OperationResult.Fail("Не удалось открыть настройки Windows system proxy.");
            }

            key.SetValue("ProxyEnable", state.ProxyEnabled ? 1 : 0, RegistryValueKind.DWord);
            SetOrDelete(key, "ProxyServer", state.ProxyServer);
            SetOrDelete(key, "ProxyOverride", state.ProxyOverride);
            SetOrDelete(key, "AutoConfigURL", state.AutoConfigUrl);
            RestoreWinHttpProxy(state);
            RunProxySideEffects(() =>
            {
                RestoreProxyEnvironment(state);
                NotifyWindows();
            });
            return OperationResult.Ok("System proxy восстановлен.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return OperationResult.Fail($"Не удалось восстановить system proxy: {ex.Message}");
        }
    }

    private static string BuildDefaultBypass()
    {
        return "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>";
    }

    private static string BuildLocalProxyServer(int httpPort, int socksPort)
    {
        return $"http=127.0.0.1:{httpPort};https=127.0.0.1:{httpPort};ftp=127.0.0.1:{httpPort};socks=127.0.0.1:{socksPort}";
    }

    private static string BuildLocalHttpProxyServer(int httpPort)
    {
        return $"127.0.0.1:{httpPort}";
    }

    private static void RunProxySideEffects(Action action)
    {
        _ = Task.Run(() =>
        {
            try
            {
                action();
            }
            catch
            {
                // Proxy registry and WinHTTP are the critical path; environment/broadcast failures are non-fatal.
            }
        });
    }

    private static void SetOrDelete(RegistryKey key, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(name, value, RegistryValueKind.String);
        }
    }

    private static string? GetUserEnvironmentValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKey, writable: false);
        return key?.GetValue(name) as string;
    }

    private static void SetProxyEnvironment(int httpPort, string proxyBypass)
    {
        var proxy = $"http://127.0.0.1:{httpPort}";
        using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(EnvironmentKey, writable: true);
        key.SetValue("HTTP_PROXY", proxy, RegistryValueKind.String);
        key.SetValue("HTTPS_PROXY", proxy, RegistryValueKind.String);
        key.SetValue("ALL_PROXY", proxy, RegistryValueKind.String);
        key.SetValue("NO_PROXY", ProxyBypassToNoProxy(proxyBypass), RegistryValueKind.String);

        Environment.SetEnvironmentVariable("HTTP_PROXY", proxy, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("HTTPS_PROXY", proxy, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("ALL_PROXY", proxy, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("NO_PROXY", ProxyBypassToNoProxy(proxyBypass), EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("HTTP_PROXY", proxy, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("HTTPS_PROXY", proxy, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("ALL_PROXY", proxy, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("NO_PROXY", ProxyBypassToNoProxy(proxyBypass), EnvironmentVariableTarget.User);
    }

    private static void RestoreProxyEnvironment(SystemProxyState state)
    {
        SetOrDeleteUserEnvironment("HTTP_PROXY", state.HttpProxyEnvironment);
        SetOrDeleteUserEnvironment("HTTPS_PROXY", state.HttpsProxyEnvironment);
        SetOrDeleteUserEnvironment("ALL_PROXY", state.AllProxyEnvironment);
        SetOrDeleteUserEnvironment("NO_PROXY", state.NoProxyEnvironment);
    }

    private static void ClearLocalProxyEnvironment(int? httpPort = null)
    {
        foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" })
        {
            var value = GetUserEnvironmentValue(name);
            if (httpPort is null ? IsLocalProxy(value) : IsLocalHttpProxy(value, httpPort.Value))
            {
                SetOrDeleteUserEnvironment(name, null);
            }
        }

        var noProxy = GetUserEnvironmentValue("NO_PROXY");
        if (!string.IsNullOrWhiteSpace(noProxy) && IsLocalNoProxy(noProxy))
        {
            SetOrDeleteUserEnvironment("NO_PROXY", null);
        }
    }

    private static void ClearLokiProxyEnvironment()
    {
        foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" })
        {
            if (IsLokiLocalHttpProxy(GetUserEnvironmentValue(name)))
            {
                SetOrDeleteUserEnvironment(name, null);
            }
        }

        var noProxy = GetUserEnvironmentValue("NO_PROXY");
        if (!string.IsNullOrWhiteSpace(noProxy) && IsLocalNoProxy(noProxy))
        {
            SetOrDeleteUserEnvironment("NO_PROXY", null);
        }
    }

    private static void SetOrDeleteUserEnvironment(string name, string? value)
    {
        using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(EnvironmentKey, writable: true);
        if (string.IsNullOrWhiteSpace(value))
        {
            key.DeleteValue(name, throwOnMissingValue: false);
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
        }
        else
        {
            key.SetValue(name, value, RegistryValueKind.String);
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
        }
    }

    private static string ProxyBypassToNoProxy(string proxyBypass)
    {
        return proxyBypass
            .Replace(";", ",", StringComparison.Ordinal)
            .Replace("<local>", "localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("*", "", StringComparison.Ordinal);
    }

    private static bool IsLocalNoProxy(string value)
    {
        return value.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || value.Contains("127.", StringComparison.OrdinalIgnoreCase)
            || value.Contains("10.", StringComparison.OrdinalIgnoreCase)
            || value.Contains("192.168.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLocalHttpProxy(string? proxyServer, int httpPort)
    {
        if (string.IsNullOrWhiteSpace(proxyServer))
        {
            return false;
        }

        var localEndpoint = $"127.0.0.1:{httpPort}";
        return proxyServer.Equals(localEndpoint, StringComparison.OrdinalIgnoreCase)
            || proxyServer.Equals($"http://{localEndpoint}", StringComparison.OrdinalIgnoreCase)
            || proxyServer.Equals($"https://{localEndpoint}", StringComparison.OrdinalIgnoreCase)
            || proxyServer.Contains($"http={localEndpoint}", StringComparison.OrdinalIgnoreCase)
            || proxyServer.Contains($"https={localEndpoint}", StringComparison.OrdinalIgnoreCase)
            || proxyServer.Contains($"http=http://{localEndpoint}", StringComparison.OrdinalIgnoreCase)
            || proxyServer.Contains($"https=http://{localEndpoint}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLokiLocalHttpProxy(string? proxyServer)
    {
        for (var port = 18089; port < 18188; port += 2)
        {
            if (IsLocalHttpProxy(proxyServer, port))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLokiLocalProxyEnvironment(SystemProxyState state)
    {
        return IsLokiLocalHttpProxy(state.HttpProxyEnvironment)
            || IsLokiLocalHttpProxy(state.HttpsProxyEnvironment)
            || IsLokiLocalHttpProxy(state.AllProxyEnvironment);
    }

    private static bool IsLocalProxy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || value.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || value.Contains("[::1]", StringComparison.OrdinalIgnoreCase);
    }

    private static void NotifyWindows()
    {
        InternetSetOption(IntPtr.Zero, InternetOptionProxySettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
        SendMessageTimeout(
            new IntPtr(HwndBroadcast),
            WmSettingChange,
            IntPtr.Zero,
            InternetSettingsKey,
            SmtoAbortIfHung,
            1000,
            out _);
        SendMessageTimeout(
            new IntPtr(HwndBroadcast),
            WmSettingChange,
            IntPtr.Zero,
            "Environment",
            SmtoAbortIfHung,
            1000,
            out _);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        string lParam,
        int flags,
        int timeout,
        out IntPtr result);

    private static WinHttpState? ReadWinHttp()
    {
        if (!WinHttpGetDefaultProxyConfiguration(out var info))
        {
            return null;
        }

        var proxy = Marshal.PtrToStringUni(info.Proxy);
        var proxyBypass = Marshal.PtrToStringUni(info.ProxyBypass);
        FreeIfNeeded(info.Proxy);
        FreeIfNeeded(info.ProxyBypass);
        return new WinHttpState(info.AccessType, proxy, proxyBypass);
    }

    private static void SetWinHttpProxy(string proxyServer, string proxyBypass)
    {
        var info = new WinHttpProxyInfoSet
        {
            AccessType = WinHttpAccessTypeNamedProxy,
            Proxy = proxyServer,
            ProxyBypass = proxyBypass
        };
        _ = WinHttpSetDefaultProxyConfiguration(ref info);
    }

    private static void ClearWinHttpProxy()
    {
        var info = new WinHttpProxyInfoSet
        {
            AccessType = WinHttpAccessTypeNoProxy
        };
        _ = WinHttpSetDefaultProxyConfiguration(ref info);
    }

    private static void RestoreWinHttpProxy(SystemProxyState state)
    {
        if (state.WinHttpAccessType is null)
        {
            return;
        }

        var info = new WinHttpProxyInfoSet
        {
            AccessType = state.WinHttpAccessType.Value,
            Proxy = state.WinHttpProxy,
            ProxyBypass = state.WinHttpProxyBypass
        };
        _ = WinHttpSetDefaultProxyConfiguration(ref info);
    }

    private static void FreeIfNeeded(IntPtr value)
    {
        if (value != IntPtr.Zero)
        {
            _ = GlobalFree(value);
        }
    }

    private sealed record WinHttpState(int AccessType, string? Proxy, string? ProxyBypass);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinHttpProxyInfoRead
    {
        public int AccessType;
        public IntPtr Proxy;
        public IntPtr ProxyBypass;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinHttpProxyInfoSet
    {
        public int AccessType;
        public string? Proxy;
        public string? ProxyBypass;
    }

    [DllImport("winhttp.dll", SetLastError = true)]
    private static extern bool WinHttpGetDefaultProxyConfiguration(out WinHttpProxyInfoRead proxyInfo);

    [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WinHttpSetDefaultProxyConfiguration(ref WinHttpProxyInfoSet proxyInfo);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
