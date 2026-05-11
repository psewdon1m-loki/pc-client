using System.Diagnostics;
using System.Text;

namespace Client.Platform.Windows;

public sealed class BrowserProxyCompatibilityService
{
    private const string ManagedBlockStart = "// Loki Proxy VPN managed proxy compatibility start";
    private const string ManagedBlockEnd = "// Loki Proxy VPN managed proxy compatibility end";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly BrowserFamily[] BrowserFamilies =
    [
        new("zen", "zen", "zen"),
        new("Firefox", Path.Combine("Mozilla", "Firefox"), "firefox"),
        new("LibreWolf", "LibreWolf", "librewolf"),
        new("Floorp", "Floorp", "floorp"),
        new("Waterfox", "Waterfox", "waterfox")
    ];

    public BrowserProxyCompatibilityResult EnsureSystemProxyCompatibility()
    {
        var profileDirectories = FindBrowserProfileDirectories().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var updated = 0;
        var errors = new List<string>();

        foreach (var profileDirectory in profileDirectories)
        {
            try
            {
                Directory.CreateDirectory(profileDirectory);
                var userJsPath = Path.Combine(profileDirectory, "user.js");
                var previous = File.Exists(userJsPath)
                    ? File.ReadAllText(userJsPath, Encoding.UTF8)
                    : string.Empty;
                var next = UpsertManagedBlock(previous);
                if (!string.Equals(previous, next, StringComparison.Ordinal))
                {
                    File.WriteAllText(userJsPath, next, Utf8NoBom);
                    updated++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{profileDirectory}: {ex.Message}");
            }
        }

        return new BrowserProxyCompatibilityResult(
            profileDirectories.Length,
            updated,
            GetBrowserProcessCount(),
            errors);
    }

    private static IEnumerable<string> FindBrowserProfileDirectories()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            yield break;
        }

        foreach (var family in BrowserFamilies)
        {
            var browserRoot = Path.Combine(appData, family.ConfigDirectory);
            if (!Directory.Exists(browserRoot))
            {
                continue;
            }

            var profilesIniPath = Path.Combine(browserRoot, "profiles.ini");
            if (File.Exists(profilesIniPath))
            {
                foreach (var path in ParseProfilePaths(profilesIniPath, browserRoot))
                {
                    yield return path;
                }
            }

            var profilesRoot = Path.Combine(browserRoot, "Profiles");
            if (!Directory.Exists(profilesRoot))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(profilesRoot))
            {
                if (File.Exists(Path.Combine(directory, "prefs.js"))
                    || File.Exists(Path.Combine(directory, "user.js")))
                {
                    yield return directory;
                }
            }
        }
    }

    private static IEnumerable<string> ParseProfilePaths(string profilesIniPath, string zenRoot)
    {
        var isRelative = true;
        string? profilePath = null;

        foreach (var rawLine in File.ReadLines(profilesIniPath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                foreach (var path in BuildProfilePath(profilePath, isRelative, zenRoot))
                {
                    yield return path;
                }

                isRelative = true;
                profilePath = null;
                continue;
            }

            if (line.StartsWith("IsRelative=", StringComparison.OrdinalIgnoreCase))
            {
                isRelative = line.EndsWith("1", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
            {
                profilePath = line["Path=".Length..].Replace('/', Path.DirectorySeparatorChar);
            }
        }

        foreach (var path in BuildProfilePath(profilePath, isRelative, zenRoot))
        {
            yield return path;
        }
    }

    private static IEnumerable<string> BuildProfilePath(string? profilePath, bool isRelative, string zenRoot)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            yield break;
        }

        yield return isRelative
            ? Path.GetFullPath(Path.Combine(zenRoot, profilePath))
            : Path.GetFullPath(profilePath);
    }

    private static string UpsertManagedBlock(string content)
    {
        var managedBlock = string.Join(Environment.NewLine,
            ManagedBlockStart,
            "user_pref(\"network.proxy.type\", 5);",
            "user_pref(\"network.proxy.failover_direct\", false);",
            "user_pref(\"network.proxy.no_proxies_on\", \"localhost, 127.0.0.1, ::1\");",
            "user_pref(\"network.http.http3.enabled\", false);",
            "user_pref(\"media.peerconnection.ice.proxy_only_if_behind_proxy\", true);",
            "user_pref(\"media.peerconnection.ice.default_address_only\", true);",
            "user_pref(\"media.peerconnection.ice.no_host\", true);",
            ManagedBlockEnd,
            string.Empty);

        var startIndex = content.IndexOf(ManagedBlockStart, StringComparison.Ordinal);
        if (startIndex >= 0)
        {
            var endIndex = content.IndexOf(ManagedBlockEnd, startIndex, StringComparison.Ordinal);
            if (endIndex >= 0)
            {
                endIndex += ManagedBlockEnd.Length;
                while (endIndex < content.Length && (content[endIndex] == '\r' || content[endIndex] == '\n'))
                {
                    endIndex++;
                }

                var prefix = content[..startIndex].TrimEnd();
                var suffix = content[endIndex..].TrimStart();
                return JoinSections(prefix, managedBlock.TrimEnd(), suffix);
            }
        }

        return JoinSections(content.TrimEnd(), managedBlock.TrimEnd(), string.Empty);
    }

    private static string JoinSections(params string[] sections)
    {
        var nonEmptySections = sections
            .Where(section => !string.IsNullOrWhiteSpace(section))
            .ToArray();
        return nonEmptySections.Length == 0
            ? string.Empty
            : string.Join(Environment.NewLine + Environment.NewLine, nonEmptySections) + Environment.NewLine;
    }

    private static int GetBrowserProcessCount()
    {
        var count = 0;
        try
        {
            foreach (var family in BrowserFamilies)
            {
                count += Process.GetProcessesByName(family.ProcessName).Length;
            }

            return count;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private sealed record BrowserFamily(string DisplayName, string ConfigDirectory, string ProcessName);
}

public sealed record BrowserProxyCompatibilityResult(
    int ProfileCount,
    int UpdatedProfileCount,
    int RunningProcessCount,
    IReadOnlyList<string> Errors);
