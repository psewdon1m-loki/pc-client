using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Client.Core;

namespace Client.Routing;

public sealed class RuleSetProvider
{
    private const string DefaultDomainStrategy = "AsIs";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public IReadOnlyList<RoutingRule> LoadOrDefault(string ruleSetsDirectory, string routingMode, RoutingRule? ozonRule = null)
    {
        return LoadRuleSetOrDefault(ruleSetsDirectory, routingMode, ozonRule).Rules;
    }

    public RoutingRuleSet LoadRuleSetOrDefault(string ruleSetsDirectory, string routingMode, RoutingRule? ozonRule = null)
    {
        var fileId = GetRuleSetFileId(routingMode);
        var ruleSet = TryLoadFromZip(Path.Combine(ruleSetsDirectory, fileId + ".zip"), fileId)
            ?? TryLoadFromJson(Path.Combine(ruleSetsDirectory, fileId + ".json"));

        if (ruleSet is { Rules.Count: > 0 })
        {
            return ruleSet;
        }

        return BuildFallback(fileId, ozonRule);
    }

    public static string GetRuleSetFileId(string routingMode)
    {
        return routingMode switch
        {
            RoutingModes.RussiaSmart => "russia-smart",
            RoutingModes.GlobalProxy => "global",
            "global" => "global",
            RoutingModes.Whitelist => "whitelist",
            RoutingModes.Blacklist => "blacklist",
            _ => "russia-smart"
        };
    }

    private static RoutingRuleSet? TryLoadFromJson(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return Parse(File.ReadAllText(path));
    }

    private static RoutingRuleSet? TryLoadFromZip(string path, string id)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var archive = ZipFile.OpenRead(path);
        var entry = archive.Entries
            .Where(item => item.Length > 0 && item.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.FullName.Count(ch => ch == '/' || ch == '\\'))
            .ThenBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var parsed = Parse(reader.ReadToEnd());
        return parsed is null ? null : parsed with { Id = id };
    }

    private static RoutingRuleSet? Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        var root = document.RootElement;
        var rulesElement = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("rules", out var value)
                ? value
                : default;

        if (rulesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var dtos = JsonSerializer.Deserialize<List<RuleDto>>(rulesElement.GetRawText(), Options);
        var rules = dtos?
            .Select(ToRule)
            .Where(IsValid)
            .ToArray();

        if (rules is not { Length: > 0 })
        {
            return null;
        }

        return new RoutingRuleSet
        {
            Id = ReadString(root, "id") ?? "custom",
            Name = ReadString(root, "name") ?? "Custom",
            DomainStrategy = NormalizeDomainStrategy(ReadString(root, "domainStrategy") ?? ReadString(root, "DomainStrategy")),
            Rules = rules
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string NormalizeDomainStrategy(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DefaultDomainStrategy : value.Trim();
    }

    private static RoutingRule ToRule(RuleDto dto)
    {
        return new RoutingRule
        {
            OutboundTag = string.IsNullOrWhiteSpace(dto.OutboundTag) ? "proxy" : dto.OutboundTag,
            Domains = Normalize(dto.Domains ?? dto.Domain),
            Ips = Normalize(dto.Ips ?? dto.Ip),
            Protocols = Normalize(dto.Protocols ?? dto.Protocol),
            Port = string.IsNullOrWhiteSpace(dto.Port) ? null : dto.Port,
            Network = string.IsNullOrWhiteSpace(dto.Network) ? null : dto.Network,
            Enabled = dto.Enabled,
            Remarks = dto.Remarks ?? string.Empty
        };
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? values)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray() ?? Array.Empty<string>();
    }

    private static bool IsValid(RoutingRule rule)
    {
        if (!rule.Enabled)
        {
            return true;
        }

        if (rule.OutboundTag is not ("proxy" or "direct" or "block"))
        {
            return false;
        }

        return rule.Domains.Count > 0 ||
               rule.Ips.Count > 0 ||
               rule.Protocols.Count > 0 ||
               !string.IsNullOrWhiteSpace(rule.Port) ||
               !string.IsNullOrWhiteSpace(rule.Network);
    }

    private static RoutingRuleSet BuildFallback(string fileId, RoutingRule? ozonRule)
    {
        var rules = fileId switch
        {
            "global" =>
            [
                new() { OutboundTag = "block", Port = "443", Network = "udp", Remarks = "Block UDP 443" },
                new() { OutboundTag = "direct", Ips = ["geoip:private"], Remarks = "Direct private IP" },
                new() { OutboundTag = "direct", Domains = ["geosite:private"], Remarks = "Direct private domains" },
                new() { OutboundTag = "proxy", Port = "0-65535", Remarks = "Proxy everything else" }
            ],
            "whitelist" =>
            [
                new() { OutboundTag = "block", Port = "443", Network = "udp", Remarks = "Block UDP 443" },
                new() { OutboundTag = "proxy", Domains = ["geosite:google"], Remarks = "Proxy Google" },
                new() { OutboundTag = "direct", Ips = ["geoip:private"], Remarks = "Direct private IP" },
                new() { OutboundTag = "direct", Domains = ["geosite:private"], Remarks = "Direct private domains" },
                new() { OutboundTag = "direct", Ips = ["geoip:cn"], Remarks = "Direct China IP" },
                new() { OutboundTag = "direct", Domains = ["geosite:cn"], Remarks = "Direct China domains" }
            ],
            "blacklist" =>
            [
                new() { OutboundTag = "direct", Protocols = ["bittorrent"], Remarks = "Direct bittorrent" },
                new() { OutboundTag = "block", Port = "443", Network = "udp", Remarks = "Block UDP 443" },
                new() { OutboundTag = "proxy", Domains = ["geosite:google", "geosite:gfw", "geosite:greatfire"], Remarks = "Proxy blocked domains" },
                new() { OutboundTag = "direct", Ips = ["geoip:private"], Remarks = "Direct private IP" },
                new() { OutboundTag = "direct", Domains = ["geosite:private"], Remarks = "Direct private domains" },
                new() { OutboundTag = "direct", Port = "0-65535", Remarks = "Direct everything else" }
            ],
            _ => new RussiaRoutingPreset().BuildSmartRules(ozonRule)
        };

        return new RoutingRuleSet
        {
            Id = fileId,
            Name = fileId,
            DomainStrategy = fileId == "russia-smart" ? "IPOnDemand" : DefaultDomainStrategy,
            Rules = rules
        };
    }

    private sealed record RuleDto
    {
        public string? Remarks { get; init; }
        public string? OutboundTag { get; init; }
        public IReadOnlyList<string>? Domains { get; init; }
        public IReadOnlyList<string>? Ips { get; init; }
        public IReadOnlyList<string>? Protocols { get; init; }
        public string? Port { get; init; }
        public string? Network { get; init; }
        public bool Enabled { get; init; } = true;

        [JsonPropertyName("Domain")]
        public IReadOnlyList<string>? Domain { get; init; }

        [JsonPropertyName("Ip")]
        public IReadOnlyList<string>? Ip { get; init; }

        [JsonPropertyName("Protocol")]
        public IReadOnlyList<string>? Protocol { get; init; }
    }
}
