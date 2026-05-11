using System.Text;
using Client.Core;

namespace Client.Profiles;

public sealed class SubscriptionParser
{
    private readonly VlessParser _vlessParser = new();

    public IReadOnlyList<ProxyProfile> ParseContent(string content, string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<ProxyProfile>();
        }

        var normalized = DecodeIfBase64(content.Trim());
        var profiles = new List<ProxyProfile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in normalized.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Trim();
            if (!candidate.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parsed = _vlessParser.Parse(candidate, sourceUrl);
            if (!parsed.Success || parsed.Value is null)
            {
                continue;
            }

            var key = $"{parsed.Value.UserId}@{parsed.Value.Host}:{parsed.Value.Port}/{parsed.Value.Network}/{parsed.Value.Security}";
            if (seen.Add(key))
            {
                profiles.Add(parsed.Value);
            }
        }

        return profiles;
    }

    private static string DecodeIfBase64(string content)
    {
        if (content.Contains("vless://", StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        var compact = content.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        if (compact.Length == 0)
        {
            return content;
        }

        try
        {
            compact = compact.PadRight(compact.Length + ((4 - compact.Length % 4) % 4), '=');
            var bytes = Convert.FromBase64String(compact);
            var decoded = Encoding.UTF8.GetString(bytes);
            return decoded.Contains("vless://", StringComparison.OrdinalIgnoreCase) ? decoded : content;
        }
        catch (FormatException)
        {
            return content;
        }
    }
}

