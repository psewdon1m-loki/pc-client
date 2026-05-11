namespace Client.Transport.Xray;

public sealed record XrayRuntimeOptions
{
    public required string XrayExecutablePath { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string ConfigPath { get; init; }
    public int SocksPort { get; init; } = 10808;
    public int HttpPort { get; init; } = 10809;
}

