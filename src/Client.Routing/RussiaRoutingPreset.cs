namespace Client.Routing;

public sealed class RussiaRoutingPreset
{
    public IReadOnlyList<RoutingRule> BuildSmartRules(RoutingRule? ozonRule = null, bool includeRussianDomains = false)
    {
        var rules = new List<RoutingRule>
        {
            ozonRule ?? new OzonDirectRuleProvider().GetDefaultRule(),
            new()
            {
                OutboundTag = "direct",
                Protocols = ["bittorrent"],
                Remarks = "Direct bittorrent"
            },
            new()
            {
                OutboundTag = "direct",
                Ips = ["geoip:private"],
                Remarks = "Direct private networks"
            },
            new()
            {
                OutboundTag = "direct",
                Domains = ["geosite:private"],
                Remarks = "Direct private domains"
            }
        };

        if (includeRussianDomains)
        {
            rules.Add(new RoutingRule
            {
                OutboundTag = "direct",
                Domains = ["geosite:ru"],
                Remarks = "Direct Russian domains"
            });
        }

        rules.Add(new RoutingRule
        {
            OutboundTag = "direct",
            Ips = ["geoip:ru"],
            Remarks = "Direct Russian IP"
        });

        rules.Add(new RoutingRule
        {
            OutboundTag = "proxy",
            Port = "0-65535",
            Remarks = "Proxy everything else"
        });

        return rules;
    }
}
