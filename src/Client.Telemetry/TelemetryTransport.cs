using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Client.Telemetry;

public sealed class TelemetryTransport(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task EnrollAsync(
        TelemetryEndpointConfig config,
        TelemetryIdentity identity,
        TelemetryDeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            identity.ClientId,
            identity.DisplayId,
            identity.ClientSecret,
            Device = device
        }, JsonOptions);

        using var request = CreateUnsignedRequest(config, HttpMethod.Post, "/api/v1/enroll", body);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task SendBatchAsync(
        TelemetryEndpointConfig config,
        TelemetryIdentity identity,
        TelemetryDeviceInfo device,
        IReadOnlyList<TelemetryEvent> events,
        CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            identity.ClientId,
            identity.DisplayId,
            Device = device,
            Events = events
        }, JsonOptions);

        using var request = CreateSignedRequest(config, identity, HttpMethod.Post, "/api/v1/telemetry/batch", body);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<TelemetryCommand>> FetchCommandsAsync(
        TelemetryEndpointConfig config,
        TelemetryIdentity identity,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateSignedRequest(config, identity, HttpMethod.Get, $"/api/v1/commands/{identity.ClientId}", "");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var commands = await response.Content.ReadFromJsonAsync<TelemetryCommandResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        return commands?.Commands ?? [];
    }

    public static string CreateSignature(
        string secret,
        string method,
        string pathAndQuery,
        string timestamp,
        string body)
    {
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var canonical = string.Join('\n', method.ToUpperInvariant(), pathAndQuery, timestamp, bodyHash);
        using var hmac = new HMACSHA256(DecodeBase64Url(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
    }

    private static HttpRequestMessage CreateUnsignedRequest(
        TelemetryEndpointConfig config,
        HttpMethod method,
        string path,
        string body)
    {
        var request = new HttpRequestMessage(method, new Uri(config.Endpoint, path));
        ApplyBodyAndHost(request, config, body);
        return request;
    }

    private static HttpRequestMessage CreateSignedRequest(
        TelemetryEndpointConfig config,
        TelemetryIdentity identity,
        HttpMethod method,
        string path,
        string body)
    {
        var request = CreateUnsignedRequest(config, method, path, body);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        request.Headers.TryAddWithoutValidation("X-Loki-Client-Id", identity.ClientId);
        request.Headers.TryAddWithoutValidation("X-Loki-Display-Id", identity.DisplayId);
        request.Headers.TryAddWithoutValidation("X-Loki-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation(
            "X-Loki-Signature",
            CreateSignature(identity.ClientSecret, method.Method, path, timestamp, body));
        return request;
    }

    private static void ApplyBodyAndHost(HttpRequestMessage request, TelemetryEndpointConfig config, string body)
    {
        if (!string.IsNullOrWhiteSpace(config.SniHost))
        {
            request.Headers.Host = config.SniHost;
        }

        if (request.Method != HttpMethod.Get)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}
