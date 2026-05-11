namespace Client.Telemetry;

public sealed record TelemetryEvent
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Type { get; init; } = "heartbeat";
    public string ConnectionStatus { get; init; } = "disconnected";
    public string? ActiveProfileHash { get; init; }
    public string? RoutingMode { get; init; }
    public IReadOnlyList<TelemetryConnectionInfo> Connections { get; init; } = [];
    public string? Message { get; init; }
    public IReadOnlyList<string> LogLines { get; init; } = [];
    public long TrafficTotalBytes { get; init; }
    public long TrafficDeltaBytes { get; init; }
    public string TrafficMeteringMode { get; init; } = "device-network-while-connected";
}

public sealed record TelemetryConnectionInfo
{
    public string? ProfileIdHash { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Protocol { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Network { get; init; } = string.Empty;
    public string Security { get; init; } = string.Empty;
    public string? Sni { get; init; }
    public bool FromSubscription { get; init; }
}

public sealed record TelemetryDeviceInfo
{
    public string DeviceType { get; init; } = "desktop-windows";
    public string UserName { get; init; } = Environment.UserName;
    public string MachineName { get; init; } = Environment.MachineName;
    public string Os { get; init; } = Environment.OSVersion.VersionString;
    public string WindowsVersion { get; init; } = Environment.OSVersion.Version.ToString();
    public string Architecture { get; init; } = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
    public string Runtime { get; init; } = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
    public string AppVersion { get; init; } = "dev";
    public DateTimeOffset InstalledAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record TelemetryCommand(string Id, string Type, System.Text.Json.JsonElement? Payload = null);

public sealed record TelemetryCommandResponse(IReadOnlyList<TelemetryCommand> Commands);
