using System.Globalization;

namespace Client.Telemetry;

public sealed record TelemetryEndpointConfig
{
    public Uri Endpoint { get; init; } = new("http://127.0.0.1:18080");
    public string? SniHost { get; init; }
    public TimeSpan UploadInterval { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan CommandPollInterval { get; init; } = TimeSpan.FromMinutes(5);

    public static TelemetryEndpointConfig Load(string appBaseDirectory, string dataDirectory)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        LoadFile(values, Path.Combine(appBaseDirectory, "loki.env"));
        LoadFile(values, Path.Combine(dataDirectory, "loki.env"));

        foreach (var name in new[]
                 {
                     "LOKI_TELEMETRY_ENDPOINT",
                     "LOKI_TELEMETRY_SNI",
                     "LOKI_TELEMETRY_UPLOAD_INTERVAL_MINUTES",
                     "LOKI_TELEMETRY_COMMAND_POLL_SECONDS"
                 })
        {
            var environmentValue = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                values[name] = environmentValue;
            }
        }

        var endpoint = values.TryGetValue("LOKI_TELEMETRY_ENDPOINT", out var endpointValue)
            && Uri.TryCreate(endpointValue, UriKind.Absolute, out var parsedEndpoint)
                ? parsedEndpoint
                : new Uri("http://127.0.0.1:18080");

        return new TelemetryEndpointConfig
        {
            Endpoint = endpoint,
            SniHost = values.TryGetValue("LOKI_TELEMETRY_SNI", out var sni) ? NullIfWhiteSpace(sni) : null,
            UploadInterval = ReadMinutes(values, "LOKI_TELEMETRY_UPLOAD_INTERVAL_MINUTES", 60, 5, 1440),
            CommandPollInterval = ReadSeconds(values, "LOKI_TELEMETRY_COMMAND_POLL_SECONDS", 300, 15, 3600)
        };
    }

    private static void LoadFile(IDictionary<string, string> values, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');
            if (key.Length > 0)
            {
                values[key] = value;
            }
        }
    }

    private static TimeSpan ReadMinutes(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback,
        int min,
        int max)
    {
        if (!values.TryGetValue(key, out var value)
            || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            parsed = fallback;
        }

        return TimeSpan.FromMinutes(Math.Clamp(parsed, min, max));
    }

    private static TimeSpan ReadSeconds(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback,
        int min,
        int max)
    {
        if (!values.TryGetValue(key, out var value)
            || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            parsed = fallback;
        }

        return TimeSpan.FromSeconds(Math.Clamp(parsed, min, max));
    }

    private static string? NullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
