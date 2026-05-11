using System.Text.Encodings.Web;
using System.Text.Json;
using Client.Core;
using Client.Routing;

namespace Client.Transport.Xray;

public sealed class XrayConfigRenderer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string Render(
        ProxyProfile profile,
        AppSettings settings,
        IReadOnlyList<RoutingRule> rules,
        string domainStrategy = "AsIs",
        string? accessLogPath = null,
        string? errorLogPath = null)
    {
        if (!profile.IsVless)
        {
            throw new NotSupportedException("MVP supports only VLESS profiles.");
        }

        var document = new Dictionary<string, object?>
        {
            ["log"] = WithoutNulls(new Dictionary<string, object?>
            {
                ["loglevel"] = string.IsNullOrWhiteSpace(accessLogPath) && string.IsNullOrWhiteSpace(errorLogPath)
                    ? "warning"
                    : "info",
                ["access"] = accessLogPath,
                ["error"] = errorLogPath
            }),
            ["inbounds"] = BuildInbounds(settings),
            ["outbounds"] = BuildOutbounds(profile),
            ["routing"] = new Dictionary<string, object?>
            {
                ["domainStrategy"] = string.IsNullOrWhiteSpace(domainStrategy) ? "AsIs" : domainStrategy,
                ["rules"] = BuildRules(rules)
            }
        };

        return JsonSerializer.Serialize(document, Options);
    }

    private static object[] BuildInbounds(AppSettings settings)
    {
        return
        [
            new Dictionary<string, object?>
            {
                ["tag"] = "socks-in",
                ["listen"] = "127.0.0.1",
                ["port"] = settings.SocksPort,
                ["protocol"] = "socks",
                ["settings"] = new Dictionary<string, object?>
                {
                    ["udp"] = true
                },
                ["sniffing"] = BuildSniffing()
            },
            new Dictionary<string, object?>
            {
                ["tag"] = "http-in",
                ["listen"] = "127.0.0.1",
                ["port"] = settings.HttpPort,
                ["protocol"] = "http",
                ["settings"] = new Dictionary<string, object?>(),
                ["sniffing"] = BuildSniffing()
            }
        ];
    }

    private static Dictionary<string, object?> BuildSniffing()
    {
        return new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["destOverride"] = new[] { "http", "tls", "quic" },
            ["routeOnly"] = false
        };
    }

    private static object[] BuildOutbounds(ProxyProfile profile)
    {
        var user = new Dictionary<string, object?>
        {
            ["id"] = profile.UserId,
            ["encryption"] = string.IsNullOrWhiteSpace(profile.Encryption) ? "none" : profile.Encryption
        };
        AddIfPresent(user, "flow", profile.Flow);

        var proxy = new Dictionary<string, object?>
        {
            ["tag"] = "proxy",
            ["protocol"] = "vless",
            ["settings"] = new Dictionary<string, object?>
            {
                ["vnext"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["address"] = profile.Host,
                        ["port"] = profile.Port,
                        ["users"] = new[] { user }
                    }
                }
            },
            ["streamSettings"] = BuildStreamSettings(profile)
        };

        return
        [
            proxy,
            new Dictionary<string, object?>
            {
                ["tag"] = "direct",
                ["protocol"] = "freedom"
            },
            new Dictionary<string, object?>
            {
                ["tag"] = "block",
                ["protocol"] = "blackhole"
            }
        ];
    }

    private static Dictionary<string, object?> BuildStreamSettings(ProxyProfile profile)
    {
        var network = string.IsNullOrWhiteSpace(profile.Network) ? "tcp" : profile.Network;
        var security = string.IsNullOrWhiteSpace(profile.Security) ? "none" : profile.Security;
        var stream = new Dictionary<string, object?>
        {
            ["network"] = network,
            ["security"] = security
        };

        if (security.Equals("tls", StringComparison.OrdinalIgnoreCase))
        {
            stream["tlsSettings"] = WithoutNulls(new Dictionary<string, object?>
            {
                ["serverName"] = profile.Sni,
                ["fingerprint"] = profile.Fingerprint,
                ["allowInsecure"] = false
            });
        }
        else if (security.Equals("reality", StringComparison.OrdinalIgnoreCase))
        {
            stream["realitySettings"] = WithoutNulls(new Dictionary<string, object?>
            {
                ["serverName"] = profile.Sni,
                ["fingerprint"] = profile.Fingerprint,
                ["publicKey"] = profile.PublicKey,
                ["shortId"] = profile.ShortId,
                ["spiderX"] = string.IsNullOrWhiteSpace(profile.Path) ? "/" : profile.Path
            });
        }

        if (network.Equals("grpc", StringComparison.OrdinalIgnoreCase))
        {
            stream["grpcSettings"] = WithoutNulls(new Dictionary<string, object?>
            {
                ["serviceName"] = profile.ServiceName,
                ["multiMode"] = false
            });
        }
        else if (network.Equals("ws", StringComparison.OrdinalIgnoreCase))
        {
            stream["wsSettings"] = WithoutNulls(new Dictionary<string, object?>
            {
                ["path"] = profile.Path,
                ["headers"] = string.IsNullOrWhiteSpace(profile.HostHeader)
                    ? null
                    : new Dictionary<string, object?> { ["Host"] = profile.HostHeader }
            });
        }
        else if (network.Equals("tcp", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(profile.HeaderType))
        {
            stream["tcpSettings"] = new Dictionary<string, object?>
            {
                ["header"] = new Dictionary<string, object?>
                {
                    ["type"] = profile.HeaderType
                }
            };
        }

        return stream;
    }

    private static object[] BuildRules(IReadOnlyList<RoutingRule> rules)
    {
        return rules
            .Where(rule => rule.Enabled)
            .Select(rule =>
            {
                var item = new Dictionary<string, object?>
                {
                    ["type"] = "field",
                    ["outboundTag"] = rule.OutboundTag
                };
                if (rule.Domains.Count > 0) item["domain"] = rule.Domains;
                if (rule.Ips.Count > 0) item["ip"] = rule.Ips;
                if (rule.Protocols.Count > 0) item["protocol"] = rule.Protocols;
                AddIfPresent(item, "port", rule.Port);
                AddIfPresent(item, "network", rule.Network);
                return item;
            })
            .Cast<object>()
            .ToArray();
    }

    private static void AddIfPresent(IDictionary<string, object?> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }

    private static Dictionary<string, object?> WithoutNulls(Dictionary<string, object?> source)
    {
        return source
            .Where(pair => pair.Value is not null && (pair.Value is not string value || !string.IsNullOrWhiteSpace(value)))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }
}
