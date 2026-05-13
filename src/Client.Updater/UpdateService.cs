using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Client.Routing;

namespace Client.Updater;

public sealed class UpdateService(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public async Task<UpdateCheckResult> CheckAndApplyAsync(
        UpdateEndpointConfig config,
        string currentVersion,
        string dataDirectory,
        string ruleSetsDirectory,
        string appBaseDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!config.IsConfigured)
        {
            return UpdateCheckResult.Skipped("Update manifest URL is not configured.");
        }

        UpdateManifest manifest;
        Uri manifestSource;
        try
        {
            var (manifestBytes, source) = await DownloadManifestWithFallbackAsync(config, cancellationToken).ConfigureAwait(false);
            manifest = JsonSerializer.Deserialize<UpdateManifest>(StripUtf8Bom(manifestBytes), JsonOptions) ?? new UpdateManifest();
            manifestSource = source;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or JsonException or CryptographicException)
        {
            return UpdateCheckResult.Fail($"Update check failed: {ex.Message}");
        }

        try
        {
            if (!string.Equals(manifest.Channel, config.Channel, StringComparison.OrdinalIgnoreCase))
            {
                return UpdateCheckResult.Ok($"Update manifest channel '{manifest.Channel}' ignored.", manifestSource: manifestSource);
            }

            var updatedRuleSets = await ApplyRuleSetUpdatesAsync(manifest, ruleSetsDirectory, cancellationToken).ConfigureAwait(false);
            var watcher = NormalizeWatcher(manifest.Watcher);
            var appInstallerStarted = await TryStartAppUpdateAsync(
                manifest,
                currentVersion,
                dataDirectory,
                appBaseDirectory,
                cancellationToken).ConfigureAwait(false);

            return UpdateCheckResult.Ok(
                appInstallerStarted ? $"App update {manifest.Version} installer started." : "Update check completed.",
                appInstallerStarted,
                watcherChanged: watcher is not null,
                updatedRuleSets,
                watcher,
                manifestSource);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or System.ComponentModel.Win32Exception)
        {
            return UpdateCheckResult.Fail($"Update apply failed: {ex.Message}");
        }
    }

    private async Task<(byte[] ManifestBytes, Uri Source)> DownloadManifestWithFallbackAsync(
        UpdateEndpointConfig config,
        CancellationToken cancellationToken)
    {
        Exception? primaryError = null;
        foreach (var manifestUrl in new[] { config.ManifestUrl, config.FallbackManifestUrl }.Where(item => item is not null).Cast<Uri>())
        {
            try
            {
                var manifestBytes = await httpClient.GetByteArrayAsync(manifestUrl, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(config.PublicKeyPem))
                {
                    var signatureUrl = new Uri(manifestUrl.ToString() + ".sig");
                    var signatureBytes = await httpClient.GetByteArrayAsync(signatureUrl, cancellationToken).ConfigureAwait(false);
                    if (!VerifySignature(manifestBytes, signatureBytes, config.PublicKeyPem))
                    {
                        throw new CryptographicException("Update manifest signature is invalid.");
                    }
                }

                return (manifestBytes, manifestUrl);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or CryptographicException)
            {
                primaryError ??= ex;
            }
        }

        throw primaryError ?? new HttpRequestException("Update manifest URL is not configured.");
    }

    public static string GetCurrentApplicationVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        var informationalVersion = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+', 2)[0];
        }

        return assembly?.GetName().Version?.ToString() ?? "0.0.0";
    }

    private async Task<IReadOnlyList<string>> ApplyRuleSetUpdatesAsync(
        UpdateManifest manifest,
        string ruleSetsDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ruleSetsDirectory);
        var updated = new List<string>();
        foreach (var asset in manifest.RuleSets)
        {
            var id = RuleSetProvider.GetRuleSetFileId(asset.Id);
            if (string.IsNullOrWhiteSpace(asset.Url) || string.IsNullOrWhiteSpace(asset.Sha256))
            {
                continue;
            }

            var targetPath = Path.Combine(ruleSetsDirectory, id + ".zip");
            if (File.Exists(targetPath) && FileHashMatches(targetPath, asset.Sha256))
            {
                continue;
            }

            var tempPath = Path.Combine(ruleSetsDirectory, id + ".zip.download");
            try
            {
                await DownloadFileAsync(asset.Url, tempPath, cancellationToken).ConfigureAwait(false);
                if (!FileHashMatches(tempPath, asset.Sha256))
                {
                    File.Delete(tempPath);
                    continue;
                }

                if (!IsValidRuleSetZip(tempPath, id))
                {
                    File.Delete(tempPath);
                    continue;
                }

                File.Move(tempPath, targetPath, overwrite: true);
                updated.Add(id);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        return updated;
    }

    private async Task<bool> TryStartAppUpdateAsync(
        UpdateManifest manifest,
        string currentVersion,
        string dataDirectory,
        string appBaseDirectory,
        CancellationToken cancellationToken)
    {
        if (manifest.Installer is not { } installer ||
            string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(installer.Url) ||
            string.IsNullOrWhiteSpace(installer.Sha256) ||
            !IsNewerVersion(manifest.Version, currentVersion))
        {
            return false;
        }

        var updatesDirectory = Path.Combine(dataDirectory, "updates");
        Directory.CreateDirectory(updatesDirectory);
        var fileName = Path.GetFileName(new Uri(installer.Url).LocalPath);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            fileName = $"LokiClientSetup-{manifest.Version}-win-x64.exe";
        }

        var installerPath = Path.Combine(updatesDirectory, fileName);
        if (!File.Exists(installerPath) || !FileHashMatches(installerPath, installer.Sha256))
        {
            var tempPath = installerPath + ".download";
            await DownloadFileAsync(installer.Url, tempPath, cancellationToken).ConfigureAwait(false);
            if (!FileHashMatches(tempPath, installer.Sha256))
            {
                File.Delete(tempPath);
                return false;
            }

            File.Move(tempPath, installerPath, overwrite: true);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
            WorkingDirectory = appBaseDirectory,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
        return true;
    }

    private async Task DownloadFileAsync(string url, string targetPath, CancellationToken cancellationToken)
    {
        await using var target = File.Create(targetPath);
        await using var source = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsValidRuleSetZip(string zipPath, string id)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            if (!archive.Entries.Any(entry => entry.Length > 0 && entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "loki-rules-validate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            try
            {
                File.Copy(zipPath, Path.Combine(tempDirectory, id + ".zip"));
                return new RuleSetProvider().LoadRuleSetOrDefault(tempDirectory, id).Rules.Count > 0;
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static bool FileHashMatches(string path, string expectedSha256)
    {
        if (!File.Exists(path) || string.IsNullOrWhiteSpace(expectedSha256))
        {
            return false;
        }

        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return string.Equals(actual, expectedSha256.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool VerifySignature(byte[] payload, byte[] signaturePayload, string publicKeyPem)
    {
        var signatureText = Encoding.UTF8.GetString(signaturePayload).Trim();
        var signature = TryFromBase64(signatureText) ?? signaturePayload;
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private static ReadOnlySpan<byte> StripUtf8Bom(byte[] value)
    {
        return value.Length >= 3 && value[0] == 0xEF && value[1] == 0xBB && value[2] == 0xBF
            ? value.AsSpan(3)
            : value;
    }

    private static byte[]? TryFromBase64(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsNewerVersion(string candidate, string current)
    {
        var candidateVersion = ParseVersion(candidate);
        var currentVersion = ParseVersion(current);
        return candidateVersion is not null && currentVersion is not null
            ? candidateVersion > currentVersion
            : string.Compare(candidate, current, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static Version? ParseVersion(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(normalized, out var version) ? version : null;
    }

    private static UpdateWatcherConfig? NormalizeWatcher(UpdateWatcherConfig? watcher)
    {
        return watcher is not null &&
               Uri.TryCreate(watcher.Endpoint, UriKind.Absolute, out _) &&
               !string.IsNullOrWhiteSpace(watcher.Endpoint)
            ? watcher
            : null;
    }
}
