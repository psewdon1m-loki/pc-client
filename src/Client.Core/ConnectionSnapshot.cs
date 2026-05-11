namespace Client.Core;

public sealed record ConnectionSnapshot
{
    public string State { get; init; } = ConnectionStates.Disconnected;
    public string? ActiveProfileName { get; init; }
    public string RoutingMode { get; init; } = RoutingModes.RussiaSmart;
    public string? LastError { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public static class ConnectionStates
{
    public const string Disconnected = "disconnected";
    public const string Connecting = "connecting";
    public const string Connected = "connected";
    public const string Error = "error";
}
