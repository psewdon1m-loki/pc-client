using System.IO;
using Client.Core;

namespace Client.App.Win.Services;

public sealed class SimpleFileLogger(AppPaths paths)
{
    private readonly string _logPath = Path.Combine(paths.LogsDirectory, "app.log");
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task InfoAsync(string message, CancellationToken cancellationToken = default)
    {
        await WriteAsync("INFO", message, cancellationToken).ConfigureAwait(false);
    }

    public async Task ErrorAsync(string message, CancellationToken cancellationToken = default)
    {
        await WriteAsync("ERROR", message, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAsync(string level, string message, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        var line = $"{DateTimeOffset.UtcNow:O} [{level}] {message}{Environment.NewLine}";
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(
                _logPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
