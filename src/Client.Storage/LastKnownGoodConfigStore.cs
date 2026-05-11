namespace Client.Storage;

public sealed class LastKnownGoodConfigStore(string path)
{
    public async Task SaveAsync(string json, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.tmp";
        await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
            : null;
    }
}

