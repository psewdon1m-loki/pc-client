using System.IO;
using Client.Core;
using Client.Routing;

namespace Client.App.Win.Services;

public sealed class AssetBootstrapper(AppPaths paths)
{
    public void EnsureRuntimeAssets()
    {
        var baseDirectory = AppContext.BaseDirectory;
        CopyIfMissingOrNewer(Path.Combine(baseDirectory, "Assets", "xray", "xray.exe"), Path.Combine(paths.RuntimeDirectory, "xray.exe"));
        CopyIfMissingOrNewer(Path.Combine(baseDirectory, "Assets", "geo", "geoip.dat"), Path.Combine(paths.RuntimeDirectory, "geoip.dat"));
        CopyIfMissingOrNewer(Path.Combine(baseDirectory, "Assets", "geo", "geosite.dat"), Path.Combine(paths.RuntimeDirectory, "geosite.dat"));
        CopyIfMissingOrNewer(Path.Combine(baseDirectory, "Assets", "overrides", "ozon.direct.json"), Path.Combine(paths.OverridesDirectory, "ozon.direct.json"));
        CopyRuleSetDefaults(Path.Combine(baseDirectory, "Assets", "rule-sets"), paths.RuleSetsDirectory);
        new OzonDirectRuleProvider().EnsureDefaultFile(Path.Combine(paths.OverridesDirectory, "ozon.direct.json"));
    }

    private static void CopyRuleSetDefaults(string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(targetDirectory);
        foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var target = Path.Combine(targetDirectory, Path.GetFileName(source));
            if (!File.Exists(target))
            {
                File.Copy(source, target);
            }
        }
    }

    private static void CopyIfMissingOrNewer(string source, string target)
    {
        if (!File.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var sourceInfo = new FileInfo(source);
        var targetInfo = new FileInfo(target);
        if (!targetInfo.Exists || sourceInfo.Length != targetInfo.Length || sourceInfo.LastWriteTimeUtc > targetInfo.LastWriteTimeUtc)
        {
            File.Copy(source, target, overwrite: true);
        }
    }
}
