using Client.Telemetry;

namespace Client.Tests;

public sealed class TelemetryTests
{
    [Fact]
    public void Redact_RemovesProxySecrets()
    {
        var input = "vless://11111111-1111-1111-1111-111111111111@example.com:443?pbk=secret&sid=abcd#name";

        var redacted = new SecretRedactor().Redact(input);

        Assert.DoesNotContain("11111111-1111-1111-1111-111111111111", redacted);
        Assert.DoesNotContain("secret", redacted);
        Assert.Contains("<redacted>", redacted);
    }

    [Fact]
    public async Task Identity_LoadOrCreate_PersistsStableClientId()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"loki-telemetry-{Guid.NewGuid():N}");
        try
        {
            var first = await TelemetryIdentity.LoadOrCreateAsync(directory);
            var second = await TelemetryIdentity.LoadOrCreateAsync(directory);

            Assert.Equal(first.ClientId, second.ClientId);
            Assert.Equal(first.ClientSecret, second.ClientSecret);
            Assert.Matches("^[A-Z2-9]{16}$", first.DisplayId);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void EndpointConfig_Load_ReadsEnvFileAndClampsIntervals()
    {
        var appDirectory = Path.Combine(Path.GetTempPath(), $"loki-app-{Guid.NewGuid():N}");
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"loki-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDirectory);
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, "loki.env"), """
            LOKI_TELEMETRY_ENDPOINT=https://telemetry.example.test
            LOKI_TELEMETRY_SNI=telemetry.example.test
            LOKI_TELEMETRY_UPLOAD_INTERVAL_MINUTES=1
            LOKI_TELEMETRY_COMMAND_POLL_SECONDS=1
            """);

        try
        {
            var config = TelemetryEndpointConfig.Load(appDirectory, dataDirectory);

            Assert.Equal("https://telemetry.example.test/", config.Endpoint.ToString());
            Assert.Equal("telemetry.example.test", config.SniHost);
            Assert.Equal(TimeSpan.FromMinutes(5), config.UploadInterval);
            Assert.Equal(TimeSpan.FromSeconds(15), config.CommandPollInterval);
        }
        finally
        {
            Directory.Delete(appDirectory, recursive: true);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void TransportSignature_IsDeterministic()
    {
        const string secret = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        var first = TelemetryTransport.CreateSignature(secret, "POST", "/api/v1/telemetry/batch", "123", """{"ok":true}""");
        var second = TelemetryTransport.CreateSignature(secret, "POST", "/api/v1/telemetry/batch", "123", """{"ok":true}""");

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }
}
