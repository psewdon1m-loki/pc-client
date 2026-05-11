namespace Client.Platform.Windows;

public sealed record SystemProxyState
{
    public bool ProxyEnabled { get; init; }
    public string? ProxyServer { get; init; }
    public string? ProxyOverride { get; init; }
    public string? AutoConfigUrl { get; init; }
    public int? WinHttpAccessType { get; init; }
    public string? WinHttpProxy { get; init; }
    public string? WinHttpProxyBypass { get; init; }
    public string? HttpProxyEnvironment { get; init; }
    public string? HttpsProxyEnvironment { get; init; }
    public string? AllProxyEnvironment { get; init; }
    public string? NoProxyEnvironment { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
}
