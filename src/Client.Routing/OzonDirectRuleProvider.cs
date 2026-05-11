using System.Text.Json;

namespace Client.Routing;

public sealed class OzonDirectRuleProvider
{
    public RoutingRule GetDefaultRule()
    {
        return new RoutingRule
        {
            OutboundTag = "direct",
            Domains =
            [
                "domain:ozon.ru",
                "domain:ozone.ru",
                "domain:ozonusercontent.com"
            ],
            Remarks = "Direct Ozon"
        };
    }

    public RoutingRule LoadOrDefault(string path)
    {
        if (!File.Exists(path))
        {
            return GetDefaultRule();
        }

        try
        {
            var json = File.ReadAllText(path);
            var model = JsonSerializer.Deserialize<OzonOverrideFile>(json, JsonOptions.Default);
            if (model is null || model.Enabled == false || model.Domains.Count == 0)
            {
                return GetDefaultRule();
            }

            return new RoutingRule
            {
                OutboundTag = string.IsNullOrWhiteSpace(model.OutboundTag) ? "direct" : model.OutboundTag,
                Domains = model.Domains,
                Remarks = string.IsNullOrWhiteSpace(model.Name) ? "Direct Ozon" : model.Name,
                Enabled = model.Enabled
            };
        }
        catch (JsonException)
        {
            return GetDefaultRule();
        }
    }

    public void EnsureDefaultFile(string path)
    {
        if (File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var model = new OzonOverrideFile
        {
            Id = "ozon-direct",
            Name = "Direct Ozon",
            OutboundTag = "direct",
            Domains = GetDefaultRule().Domains.ToList(),
            Enabled = true
        };
        File.WriteAllText(path, JsonSerializer.Serialize(model, JsonOptions.Pretty));
    }

    private sealed class OzonOverrideFile
    {
        public string Id { get; set; } = "ozon-direct";
        public string Name { get; set; } = "Direct Ozon";
        public string OutboundTag { get; set; } = "direct";
        public List<string> Domains { get; set; } = [];
        public bool Enabled { get; set; } = true;
    }
}
