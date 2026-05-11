namespace Client.Core;

public sealed record ProxyProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Source { get; init; } = string.Empty;
    public string Protocol { get; init; } = "vless";
    public string Name { get; init; } = "Proxy";
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string Encryption { get; init; } = "none";
    public string Security { get; init; } = "none";
    public string Network { get; init; } = "tcp";
    public string? Flow { get; init; }
    public string? Sni { get; init; }
    public string? Fingerprint { get; init; }
    public string? PublicKey { get; init; }
    public string? ShortId { get; init; }
    public string? Path { get; init; }
    public string? ServiceName { get; init; }
    public string? HeaderType { get; init; }
    public string? HostHeader { get; init; }
    public string? SubscriptionUrl { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool IsVless => Protocol.Equals("vless", StringComparison.OrdinalIgnoreCase);
}

