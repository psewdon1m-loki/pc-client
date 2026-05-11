namespace Client.Routing;

public sealed record RoutingRule
{
    public string OutboundTag { get; init; } = "proxy";
    public IReadOnlyList<string> Domains { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Ips { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Protocols { get; init; } = Array.Empty<string>();
    public string? Port { get; init; }
    public string? Network { get; init; }
    public bool Enabled { get; init; } = true;
    public string Remarks { get; init; } = string.Empty;
}
