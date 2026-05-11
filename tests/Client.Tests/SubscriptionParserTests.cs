using System.Text;
using Client.Profiles;

namespace Client.Tests;

public sealed class SubscriptionParserTests
{
    [Fact]
    public void ParseContent_DecodesBase64AndIgnoresUnsupportedLines()
    {
        var link = "vless://11111111-1111-1111-1111-111111111111@example.com:443?encryption=none&security=tls&type=tcp#one";
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes($"trojan://ignored{Environment.NewLine}{link}"));

        var profiles = new SubscriptionParser().ParseContent(content, "https://example.com/sub");

        Assert.Single(profiles);
        Assert.Equal("one", profiles[0].Name);
        Assert.Equal("https://example.com/sub", profiles[0].SubscriptionUrl);
    }
}

