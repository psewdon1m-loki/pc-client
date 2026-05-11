using System.Text.Json.Serialization;

namespace Client.Updater;

public sealed record UpdateManifest
{
    public string Channel { get; init; } = "stable";
    public string Version { get; init; } = string.Empty;
    public string? MinimumVersion { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public UpdateInstallerAsset? Installer { get; init; }
    public IReadOnlyList<UpdateRuleSetAsset> RuleSets { get; init; } = [];
    public UpdateWatcherConfig? Watcher { get; init; }
}

public sealed record UpdateInstallerAsset
{
    public string Url { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public bool Mandatory { get; init; }
}

public sealed record UpdateRuleSetAsset
{
    public string Id { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
}

public sealed record UpdateWatcherConfig
{
    public string Endpoint { get; init; } = string.Empty;
    public string? Sni { get; init; }
}

public sealed record UpdateCheckResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool ManifestUnavailable { get; init; }
    public bool AppInstallerStarted { get; init; }
    public bool WatcherChanged { get; init; }
    public IReadOnlyList<string> UpdatedRuleSets { get; init; } = [];
    public UpdateWatcherConfig? Watcher { get; init; }

    public static UpdateCheckResult Ok(
        string message,
        bool appInstallerStarted = false,
        bool watcherChanged = false,
        IReadOnlyList<string>? updatedRuleSets = null,
        UpdateWatcherConfig? watcher = null)
    {
        return new UpdateCheckResult
        {
            Success = true,
            Message = message,
            AppInstallerStarted = appInstallerStarted,
            WatcherChanged = watcherChanged,
            UpdatedRuleSets = updatedRuleSets ?? [],
            Watcher = watcher
        };
    }

    public static UpdateCheckResult Skipped(string message)
    {
        return new UpdateCheckResult { Success = true, Message = message, ManifestUnavailable = true };
    }

    public static UpdateCheckResult Fail(string message)
    {
        return new UpdateCheckResult { Success = false, Message = message };
    }
}

public sealed record UpdateEndpointConfig
{
    public Uri? ManifestUrl { get; init; }
    public string Channel { get; init; } = "stable";
    public string? PublicKeyPem { get; init; }
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromHours(6);

    [JsonIgnore]
    public bool IsConfigured => ManifestUrl is not null;
}
