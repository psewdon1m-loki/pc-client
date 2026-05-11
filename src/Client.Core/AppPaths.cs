namespace Client.Core;

public sealed record AppPaths
{
    public required string DataDirectory { get; init; }
    public required string DatabasePath { get; init; }
    public required string LogsDirectory { get; init; }
    public required string RuntimeDirectory { get; init; }
    public required string AssetsDirectory { get; init; }
    public required string GeoDirectory { get; init; }
    public required string OverridesDirectory { get; init; }
    public required string RuleSetsDirectory { get; init; }
    public required string LastKnownGoodConfigPath { get; init; }

    public static AppPaths CreateDefault(string appName = "LokiClient")
    {
        var root = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appName);

        var paths = new AppPaths
        {
            DataDirectory = root,
            DatabasePath = System.IO.Path.Combine(root, "client.sqlite"),
            LogsDirectory = System.IO.Path.Combine(root, "logs"),
            RuntimeDirectory = System.IO.Path.Combine(root, "runtime"),
            AssetsDirectory = System.IO.Path.Combine(root, "assets"),
            GeoDirectory = System.IO.Path.Combine(root, "assets", "geo"),
            OverridesDirectory = System.IO.Path.Combine(root, "assets", "overrides"),
            RuleSetsDirectory = System.IO.Path.Combine(root, "assets", "rule-sets"),
            LastKnownGoodConfigPath = System.IO.Path.Combine(root, "runtime", "last-known-good.json")
        };

        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.LogsDirectory);
        Directory.CreateDirectory(paths.RuntimeDirectory);
        Directory.CreateDirectory(paths.AssetsDirectory);
        Directory.CreateDirectory(paths.GeoDirectory);
        Directory.CreateDirectory(paths.OverridesDirectory);
        Directory.CreateDirectory(paths.RuleSetsDirectory);
        return paths;
    }
}
