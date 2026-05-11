namespace Client.Updater;

public sealed record GeoAssetOptions
{
    public required string GeoDirectory { get; init; }
    public string GeoIpUrl { get; init; } = "https://github.com/runetfreedom/russia-v2ray-rules-dat/releases/latest/download/geoip.dat";
    public string GeoSiteUrl { get; init; } = "https://github.com/runetfreedom/russia-v2ray-rules-dat/releases/latest/download/geosite.dat";
    public TimeSpan Ttl { get; init; } = TimeSpan.FromHours(4);
    public long MinimumAssetBytes { get; init; } = 1024;
}
