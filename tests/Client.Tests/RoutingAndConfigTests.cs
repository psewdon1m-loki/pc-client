using System.Text.Json;
using System.IO.Compression;
using Client.Core;
using Client.Routing;
using Client.Storage;
using Client.Transport.Xray;
using Microsoft.Data.Sqlite;

namespace Client.Tests;

public sealed class RoutingAndConfigTests
{
    [Fact]
    public void RussiaSmartRules_PreserveRequiredOrder()
    {
        var rules = new RussiaRoutingPreset().BuildSmartRules();

        Assert.Equal("direct", rules[0].OutboundTag);
        Assert.Contains("domain:ozon.ru", rules[0].Domains);
        Assert.Equal("direct", rules[1].OutboundTag);
        Assert.Contains("bittorrent", rules[1].Protocols);
        Assert.Contains("geoip:ru", rules[^2].Ips);
        Assert.Equal("proxy", rules[^1].OutboundTag);
    }

    [Theory]
    [InlineData(RoutingModes.RussiaSmart, 6)]
    [InlineData(RoutingModes.GlobalProxy, 4)]
    [InlineData(RoutingModes.Whitelist, 8)]
    [InlineData(RoutingModes.Blacklist, 11)]
    public void BundledRuleSets_LoadAndRenderValidXrayConfig(string routingMode, int expectedCount)
    {
        var ruleSet = new RuleSetProvider().LoadRuleSetOrDefault(FindRuleSetsDirectory(), routingMode);
        var rules = ruleSet.Rules;

        Assert.Equal(expectedCount, rules.Count);
        Assert.All(rules, rule => Assert.Contains(rule.OutboundTag, new[] { "proxy", "direct", "block" }));

        var json = new XrayConfigRenderer().Render(CreateProfile(), new AppSettings { RoutingMode = routingMode }, rules, ruleSet.DomainStrategy);
        var validation = new XrayConfigValidator().ValidateJson(json);

        Assert.True(validation.Success, validation.Message);
    }

    [Fact]
    public void BundledRussiaSmartRuleSet_MatchesReferenceShape()
    {
        var ruleSet = new RuleSetProvider().LoadRuleSetOrDefault(FindRuleSetsDirectory(), RoutingModes.RussiaSmart);
        var rules = ruleSet.Rules;

        Assert.Equal("IPOnDemand", ruleSet.DomainStrategy);
        Assert.Equal("direct", rules[0].OutboundTag);
        Assert.Contains("domain:ozon.ru", rules[0].Domains);
        Assert.Equal("direct", rules[1].OutboundTag);
        Assert.Contains("bittorrent", rules[1].Protocols);
        Assert.Equal("direct", rules[2].OutboundTag);
        Assert.Contains("geoip:private", rules[2].Ips);
        Assert.Equal("direct", rules[3].OutboundTag);
        Assert.Contains("geosite:private", rules[3].Domains);
        Assert.Equal("direct", rules[4].OutboundTag);
        Assert.Contains("geoip:ru", rules[4].Ips);
        Assert.Equal("proxy", rules[5].OutboundTag);
        Assert.Equal("0-65535", rules[5].Port);
    }

    [Fact]
    public void RuleSetProvider_LoadsZipOverrideFromRuleSetsFolder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loki-rule-set-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var zipPath = Path.Combine(directory, "global.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("custom.json");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write("""
                [
                  {
                    "outboundTag": "proxy",
                    "port": "0-65535",
                    "enabled": true,
                    "remarks": "zip override"
                  }
                ]
                """);
            }

            var rules = new RuleSetProvider().LoadOrDefault(directory, RoutingModes.GlobalProxy);

            Assert.Single(rules);
            Assert.Equal("proxy", rules[0].OutboundTag);
            Assert.Equal("0-65535", rules[0].Port);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(100);
                SqliteConnection.ClearAllPools();
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SettingsRepository_NormalizesLegacyLocalOnlyRoutingMode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loki-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var database = new ClientDatabase(Path.Combine(directory, "client.sqlite"));
            database.Initialize();
            var repository = new SettingsRepository(database);

            await repository.SaveAsync(new AppSettings { RoutingMode = RoutingModes.LocalOnly });
            var settings = await repository.LoadAsync();

            Assert.Equal(RoutingModes.RussiaSmart, settings.RoutingMode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Render_ConfigContainsRussiaRoutingAndValidJson()
    {
        var profile = CreateProfile();

        var ruleSet = new RuleSetProvider().LoadRuleSetOrDefault(FindRuleSetsDirectory(), RoutingModes.RussiaSmart);
        var json = new XrayConfigRenderer().Render(profile, new AppSettings(), ruleSet.Rules, ruleSet.DomainStrategy);
        var validation = new XrayConfigValidator().ValidateJson(json);

        Assert.True(validation.Success, validation.Message);
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.ToString();
        Assert.Contains("domain:ozon.ru", text);
        Assert.Contains("geoip:ru", text);
        Assert.Contains("geosite:private", text);
        Assert.Contains("IPOnDemand", text);
    }

    private static ProxyProfile CreateProfile()
    {
        return new ProxyProfile
        {
            Name = "RU Main",
            Host = "example.com",
            Port = 443,
            UserId = "11111111-1111-1111-1111-111111111111",
            Security = "reality",
            Network = "tcp",
            Sni = "www.microsoft.com",
            Fingerprint = "chrome",
            PublicKey = "public-key",
            ShortId = "abcd",
            Flow = "xtls-rprx-vision"
        };
    }

    private static string FindRuleSetsDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "client", "src", "Client.App.Win", "Assets", "rule-sets");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(directory.FullName, "src", "Client.App.Win", "Assets", "rule-sets");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not find bundled rule-sets directory.");
    }
}
