using System.Globalization;

namespace Client.Updater;

public static class UpdateEndpointConfigLoader
{
    public static UpdateEndpointConfig Load(string appBaseDirectory, string dataDirectory)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        LoadFile(values, Path.Combine(appBaseDirectory, "loki.env"));
        LoadFile(values, Path.Combine(dataDirectory, "loki.env"));

        foreach (var name in new[]
                 {
                     "LOKI_UPDATE_MANIFEST_URL",
                     "LOKI_UPDATE_CHANNEL",
                     "LOKI_UPDATE_PUBLIC_KEY_PEM",
                     "LOKI_UPDATE_CHECK_INTERVAL_MINUTES"
                 })
        {
            var environmentValue = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                values[name] = environmentValue;
            }
        }

        var manifestUrl = values.TryGetValue("LOKI_UPDATE_MANIFEST_URL", out var rawUrl)
            && Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsedUrl)
                ? parsedUrl
                : null;

        return new UpdateEndpointConfig
        {
            ManifestUrl = manifestUrl,
            Channel = values.TryGetValue("LOKI_UPDATE_CHANNEL", out var channel) && !string.IsNullOrWhiteSpace(channel)
                ? channel.Trim()
                : "stable",
            PublicKeyPem = values.TryGetValue("LOKI_UPDATE_PUBLIC_KEY_PEM", out var pem)
                ? NullIfWhiteSpace(pem.Replace("\\n", "\n"))
                : null,
            CheckInterval = ReadMinutes(values, "LOKI_UPDATE_CHECK_INTERVAL_MINUTES", 360, 15, 1440)
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

    private static string? NullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
