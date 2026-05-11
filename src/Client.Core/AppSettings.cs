namespace Client.Core;

public sealed record AppSettings
{
    public string? ActiveProfileId { get; init; }
    public bool AutoConnect { get; init; }
    public bool StartWithWindows { get; init; }
    public bool AutoUpdateRules { get; init; } = true;
    public string RoutingMode { get; init; } = RoutingModes.RussiaSmart;
    public bool LogsConsent { get; init; } = true;
    public bool AllowInvalidSubscriptionTls { get; init; } = true;
    public int SocksPort { get; init; } = 18088;
    public int HttpPort { get; init; } = 18089;
}

public static class RoutingModes
{
    public const string RussiaSmart = "russia-smart";
    public const string GlobalProxy = "global-proxy";
    public const string Whitelist = "whitelist";
    public const string Blacklist = "blacklist";
    public const string LocalOnly = "local-only";
}
