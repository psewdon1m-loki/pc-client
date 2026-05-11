namespace Client.Telemetry;

public sealed class TelemetryLogCollector(string logsDirectory, SecretRedactor redactor)
{
    public async Task<IReadOnlyList<string>> ReadRecentAsync(int maxLines, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(logsDirectory) || maxLines <= 0)
        {
            return [];
        }

        var lines = new List<string>();
        foreach (var file in Directory
                     .GetFiles(logsDirectory, "*.log")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(3))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recent = await ReadTailAsync(file, Math.Max(1, maxLines - lines.Count), cancellationToken).ConfigureAwait(false);
            foreach (var line in recent)
            {
                lines.Add(redactor.Redact($"{Path.GetFileName(file)} | {line}"));
                if (lines.Count >= maxLines)
                {
                    return lines;
                }
            }
        }

        return lines;
    }

    private static async Task<IReadOnlyList<string>> ReadTailAsync(string path, int maxLines, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var allLines = content.Split(Environment.NewLine, StringSplitOptions.None);
        return allLines.TakeLast(maxLines).ToArray();
    }
}
