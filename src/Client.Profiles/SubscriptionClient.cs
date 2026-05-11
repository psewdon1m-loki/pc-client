using Client.Core;

namespace Client.Profiles;

public sealed class SubscriptionClient(HttpClient httpClient)
{
    private readonly SubscriptionParser _parser = new();

    public static SubscriptionClient Create(bool allowInvalidTls = false)
    {
        if (!allowInvalidTls)
        {
            return new SubscriptionClient(new HttpClient());
        }

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        return new SubscriptionClient(new HttpClient(handler));
    }

    public async Task<OperationResult<IReadOnlyList<ProxyProfile>>> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return OperationResult<IReadOnlyList<ProxyProfile>>.Fail("Некорректный subscription URL.");
        }

        using var response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return OperationResult<IReadOnlyList<ProxyProfile>>.Fail($"Subscription вернул HTTP {(int)response.StatusCode}.");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var profiles = _parser.ParseContent(content, url);
        return profiles.Count == 0
            ? OperationResult<IReadOnlyList<ProxyProfile>>.Fail("В subscription нет поддерживаемых VLESS профилей.")
            : OperationResult<IReadOnlyList<ProxyProfile>>.Ok(profiles);
    }
}
