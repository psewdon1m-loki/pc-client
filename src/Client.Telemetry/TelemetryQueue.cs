using System.Text.Json;

namespace Client.Telemetry;

public sealed class TelemetryQueue(string telemetryDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _queuePath = Path.Combine(telemetryDirectory, "events.jsonl");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task EnqueueAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_queuePath)!);
        var line = JsonSerializer.Serialize(telemetryEvent, JsonOptions) + Environment.NewLine;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_queuePath, line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<TelemetryEvent>> ReadBatchAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_queuePath))
            {
                return [];
            }

            var events = new List<TelemetryEvent>(maxCount);
            foreach (var line in File.ReadLines(_queuePath).Take(maxCount))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var telemetryEvent = JsonSerializer.Deserialize<TelemetryEvent>(line, JsonOptions);
                if (telemetryEvent is not null)
                {
                    events.Add(telemetryEvent);
                }
            }

            return events;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveSentAsync(int count, CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_queuePath))
            {
                return;
            }

            var remaining = File.ReadLines(_queuePath).Skip(count).ToArray();
            if (remaining.Length == 0)
            {
                File.Delete(_queuePath);
                return;
            }

            await File.WriteAllLinesAsync(_queuePath, remaining, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
