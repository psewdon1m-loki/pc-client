using System.Security.Cryptography;
using System.Text.Json;

namespace Client.Telemetry;

public sealed record TelemetryIdentity
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public required string ClientId { get; init; }
    public required string DisplayId { get; init; }
    public required string ClientSecret { get; init; }
    public DateTimeOffset InstalledAt { get; init; } = DateTimeOffset.UtcNow;
    public long TotalTrafficBytes { get; init; }

    public static async Task<TelemetryIdentity> LoadOrCreateAsync(string telemetryDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(telemetryDirectory);
        var path = Path.Combine(telemetryDirectory, "identity.json");
        if (File.Exists(path))
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var identity = JsonSerializer.Deserialize<TelemetryIdentity>(json, JsonOptions);
            if (identity is not null
                && !string.IsNullOrWhiteSpace(identity.ClientId)
                && !string.IsNullOrWhiteSpace(identity.DisplayId)
                && !string.IsNullOrWhiteSpace(identity.ClientSecret))
            {
                return identity;
            }
        }

        var created = Create();
        await SaveAsync(telemetryDirectory, created, cancellationToken).ConfigureAwait(false);
        return created;
    }

    public static async Task SaveAsync(
        string telemetryDirectory,
        TelemetryIdentity identity,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(telemetryDirectory);
        var path = Path.Combine(telemetryDirectory, "identity.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(identity, JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    private static TelemetryIdentity Create()
    {
        var clientIdBytes = RandomNumberGenerator.GetBytes(16);
        var secretBytes = RandomNumberGenerator.GetBytes(32);

        return new TelemetryIdentity
        {
            ClientId = Convert.ToHexString(clientIdBytes).ToLowerInvariant(),
            DisplayId = CreateDisplayId(),
            ClientSecret = Base64UrlEncode(secretBytes),
            InstalledAt = DateTimeOffset.UtcNow
        };
    }

    private static string CreateDisplayId()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return new string(bytes.ToArray().Select(value => Alphabet[value % Alphabet.Length]).ToArray());
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
