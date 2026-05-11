using Client.Core;

namespace Client.Updater;

public sealed class GeoAssetUpdater(HttpClient httpClient)
{
    public async Task<OperationResult> EnsureFreshAsync(GeoAssetOptions options, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.GeoDirectory);

        var geoIpPath = Path.Combine(options.GeoDirectory, "geoip.dat");
        var geoSitePath = Path.Combine(options.GeoDirectory, "geosite.dat");

        if (!NeedsUpdate(geoIpPath, options.Ttl) && !NeedsUpdate(geoSitePath, options.Ttl))
        {
            return OperationResult.Ok("Geo assets актуальны.");
        }

        var geoIp = await DownloadAtomicAsync(options.GeoIpUrl, geoIpPath, options.MinimumAssetBytes, cancellationToken).ConfigureAwait(false);
        if (!geoIp.Success)
        {
            return geoIp;
        }

        var geoSite = await DownloadAtomicAsync(options.GeoSiteUrl, geoSitePath, options.MinimumAssetBytes, cancellationToken).ConfigureAwait(false);
        return geoSite.Success
            ? OperationResult.Ok("Geo assets обновлены.")
            : geoSite;
    }

    private static bool NeedsUpdate(string path, TimeSpan ttl)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        var age = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(path);
        return age > ttl;
    }

    private async Task<OperationResult> DownloadAtomicAsync(string url, string targetPath, long minimumBytes, CancellationToken cancellationToken)
    {
        var tempPath = $"{targetPath}.download";
        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return OperationResult.Fail($"Не удалось скачать {Path.GetFileName(targetPath)}: HTTP {(int)response.StatusCode}.");
            }

            await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var file = File.Create(tempPath))
            {
                await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            var info = new FileInfo(tempPath);
            if (info.Length < minimumBytes)
            {
                File.Delete(tempPath);
                return OperationResult.Fail($"{Path.GetFileName(targetPath)} слишком мал после скачивания.");
            }

            File.Move(tempPath, targetPath, overwrite: true);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            return OperationResult.Fail($"Ошибка обновления {Path.GetFileName(targetPath)}: {ex.Message}");
        }
    }
}

