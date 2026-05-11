using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Client.Updater;

namespace Client.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task CheckAndApplyAsync_UpdatesRuleSetZipAndReturnsWatcherConfig()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loki-update-test-" + Guid.NewGuid().ToString("N"));
        var dataDirectory = Path.Combine(directory, "data");
        var ruleSetsDirectory = Path.Combine(dataDirectory, "assets", "rule-sets");
        Directory.CreateDirectory(ruleSetsDirectory);

        try
        {
            var zipBytes = CreateRuleSetZip();
            var manifest = new
            {
                channel = "stable",
                version = "0.1.0",
                ruleSets = new[]
                {
                    new
                    {
                        id = "global",
                        version = "test",
                        url = "https://updates.example.test/global.zip",
                        sha256 = Sha256(zipBytes)
                    }
                },
                watcher = new
                {
                    endpoint = "https://watcher.example.test",
                    sni = ""
                }
            };

            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var service = new UpdateService(new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>
            {
                ["https://updates.example.test/manifest.json"] = manifestBytes,
                ["https://updates.example.test/global.zip"] = zipBytes
            })));

            var result = await service.CheckAndApplyAsync(
                new UpdateEndpointConfig
                {
                    ManifestUrl = new Uri("https://updates.example.test/manifest.json")
                },
                "0.1.0",
                dataDirectory,
                ruleSetsDirectory,
                directory);

            Assert.True(result.Success, result.Message);
            Assert.Contains("global", result.UpdatedRuleSets);
            Assert.Equal("https://watcher.example.test", result.Watcher?.Endpoint);
            Assert.True(File.Exists(Path.Combine(ruleSetsDirectory, "global.zip")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CheckAndApplyAsync_SkipsWhenManifestUrlIsMissing()
    {
        var service = new UpdateService(new HttpClient(new StaticResponseHandler(new Dictionary<string, byte[]>())));

        var result = await service.CheckAndApplyAsync(
            new UpdateEndpointConfig(),
            "0.1.0",
            Path.GetTempPath(),
            Path.GetTempPath(),
            Path.GetTempPath());

        Assert.True(result.Success);
        Assert.True(result.ManifestUnavailable);
    }

    private static byte[] CreateRuleSetZip()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("global.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("""
            {
              "id": "global",
              "rules": [
                {
                  "outboundTag": "proxy",
                  "port": "0-65535",
                  "remarks": "proxy everything"
                }
              ]
            }
            """);
        }

        return stream.ToArray();
    }

    private static string Sha256(byte[] value)
    {
        return Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    }

    private sealed class StaticResponseHandler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!responses.TryGetValue(request.RequestUri?.ToString() ?? string.Empty, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            });
        }
    }
}
