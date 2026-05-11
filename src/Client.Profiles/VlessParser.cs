using Client.Core;

namespace Client.Profiles;

public sealed class VlessParser
{
    public OperationResult<ProxyProfile> Parse(string input, string? subscriptionUrl = null)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return OperationResult<ProxyProfile>.Fail("Пустая ссылка профиля.");
        }

        var raw = input.Trim();
        if (!raw.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<ProxyProfile>.Fail("Поддерживаются только vless:// ссылки.");
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return OperationResult<ProxyProfile>.Fail("Некорректная vless:// ссылка.");
        }

        if (!uri.Scheme.Equals("vless", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<ProxyProfile>.Fail("Некорректный протокол профиля.");
        }

        var userId = Uri.UnescapeDataString(uri.UserInfo);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return OperationResult<ProxyProfile>.Fail("В VLESS ссылке отсутствует UUID пользователя.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return OperationResult<ProxyProfile>.Fail("В VLESS ссылке отсутствует host.");
        }

        if (uri.Port <= 0 || uri.Port > 65535)
        {
            return OperationResult<ProxyProfile>.Fail("В VLESS ссылке некорректный порт.");
        }

        var query = ParseQuery(uri.Query);
        var name = string.IsNullOrWhiteSpace(uri.Fragment)
            ? $"{uri.Host}:{uri.Port}"
            : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

        var profile = new ProxyProfile
        {
            Source = raw,
            Protocol = "vless",
            Name = string.IsNullOrWhiteSpace(name) ? $"{uri.Host}:{uri.Port}" : name,
            Host = uri.Host,
            Port = uri.Port,
            UserId = userId,
            Encryption = Get(query, "encryption") ?? "none",
            Security = Get(query, "security") ?? "none",
            Network = Get(query, "type") ?? "tcp",
            Flow = Get(query, "flow"),
            Sni = Get(query, "sni"),
            Fingerprint = Get(query, "fp"),
            PublicKey = Get(query, "pbk"),
            ShortId = Get(query, "sid"),
            Path = Get(query, "path"),
            ServiceName = Get(query, "serviceName"),
            HeaderType = Get(query, "headerType"),
            HostHeader = Get(query, "host"),
            SubscriptionUrl = subscriptionUrl,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        return OperationResult<ProxyProfile>.Ok(profile);
    }

    private static string? Get(IReadOnlyDictionary<string, string> query, string key)
    {
        return query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return result;
        }

        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }
}

