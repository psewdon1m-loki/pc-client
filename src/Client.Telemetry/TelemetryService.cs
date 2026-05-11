using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Client.Core;

namespace Client.Telemetry;

public sealed class TelemetryService
{
    private readonly AppPaths _paths;
    private readonly TelemetryTransport _transport;
    private readonly NetworkTrafficSampler _trafficSampler;
    private readonly TelemetryLogCollector _logCollector;
    private readonly string _telemetryDirectory;
    private readonly TelemetryQueue _queue;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private CancellationTokenSource? _loopCancellation;
    private Task? _uploadLoop;
    private Task? _commandLoop;
    private TelemetryIdentity? _identity;
    private TelemetryEndpointConfig _config = new();
    private bool _enabled;
    private bool _includeLogs = true;
    private bool _connected;
    private string _lastConnectionStatus = ConnectionStates.Disconnected;
    private string? _routingMode;
    private IReadOnlyList<TelemetryConnectionInfo> _connections = [];

    public event Func<TelemetryCommand, CancellationToken, Task>? CommandReceived;

    public TelemetryService(AppPaths paths, TelemetryTransport transport, NetworkTrafficSampler trafficSampler)
    {
        _paths = paths;
        _transport = transport;
        _trafficSampler = trafficSampler;
        _logCollector = new TelemetryLogCollector(paths.LogsDirectory, new SecretRedactor());
        _telemetryDirectory = Path.Combine(paths.DataDirectory, "telemetry");
        _queue = new TelemetryQueue(_telemetryDirectory);
    }

    public async Task ConfigureAsync(bool includeLogs, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _config = TelemetryEndpointConfig.Load(AppContext.BaseDirectory, _paths.DataDirectory);
            _includeLogs = includeLogs;
            _identity ??= await TelemetryIdentity.LoadOrCreateAsync(_telemetryDirectory, cancellationToken).ConfigureAwait(false);
            _enabled = true;
            if (_loopCancellation is null)
            {
                _loopCancellation = new CancellationTokenSource();
                _uploadLoop = RunUploadLoopAsync(_loopCancellation.Token);
                _commandLoop = RunCommandLoopAsync(_loopCancellation.Token);
            }

            RequestImmediateSync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void UpdateContext(AppSettings settings, IReadOnlyList<ProxyProfile> profiles)
    {
        _routingMode = settings.RoutingMode;
        _connections = profiles.Select(ToConnectionInfo).ToArray();
    }

    public async Task<OperationResult> VerifyAsync(CancellationToken cancellationToken = default)
    {
        await ConfigureAsync(_includeLogs, cancellationToken).ConfigureAwait(false);
        var identity = await GetIdentityAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.EnrollAsync(_config, identity, CreateDeviceInfo(identity), cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok("Telemetry verified.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return OperationResult.Fail($"Watcher verification failed: {ex.Message}");
        }
    }

    public Task ReportContextChangedAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return Task.CompletedTask;
        }

        return TryIgnoreNetworkErrorsAsync(() => CollectNowAsync(cancellationToken));
    }

    public async Task RecordStatusAsync(ConnectionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return;
        }

        if (snapshot.State == ConnectionStates.Connected && !_connected)
        {
            _trafficSampler.Start();
            _connected = true;
        }

        var delta = _connected ? _trafficSampler.CaptureDelta() : 0;
        if (snapshot.State is ConnectionStates.Disconnected or ConnectionStates.Error)
        {
            _connected = false;
            _trafficSampler.Stop();
        }

        var identity = await GetIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (delta > 0)
        {
            identity = identity with { TotalTrafficBytes = identity.TotalTrafficBytes + delta };
            _identity = identity;
            await TelemetryIdentity.SaveAsync(_telemetryDirectory, identity, cancellationToken).ConfigureAwait(false);
        }

        _lastConnectionStatus = snapshot.State;
        await _queue.EnqueueAsync(new TelemetryEvent
        {
            Type = "connection_state",
            ConnectionStatus = snapshot.State,
            RoutingMode = snapshot.RoutingMode ?? _routingMode,
            Connections = _connections,
            ActiveProfileHash = HashNullable(snapshot.ActiveProfileName),
            Message = new SecretRedactor().Redact(snapshot.LastError ?? string.Empty),
            TrafficDeltaBytes = delta,
            TrafficTotalBytes = identity.TotalTrafficBytes
        }, cancellationToken).ConfigureAwait(false);
        RequestImmediateSync();
    }

    public async Task CollectNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return;
        }

        var identity = await GetIdentityAsync(cancellationToken).ConfigureAwait(false);
        var delta = _connected ? _trafficSampler.CaptureDelta() : 0;
        if (delta > 0)
        {
            identity = identity with { TotalTrafficBytes = identity.TotalTrafficBytes + delta };
            _identity = identity;
            await TelemetryIdentity.SaveAsync(_telemetryDirectory, identity, cancellationToken).ConfigureAwait(false);
        }

        await _queue.EnqueueAsync(new TelemetryEvent
        {
            Type = "heartbeat",
            ConnectionStatus = _lastConnectionStatus,
            RoutingMode = _routingMode,
            Connections = _connections,
            LogLines = _includeLogs
                ? await _logCollector.ReadRecentAsync(200, cancellationToken).ConfigureAwait(false)
                : [],
            TrafficDeltaBytes = delta,
            TrafficTotalBytes = identity.TotalTrafficBytes
        }, cancellationToken).ConfigureAwait(false);

        await FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return;
        }

        await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var identity = await GetIdentityAsync(cancellationToken).ConfigureAwait(false);
            await _transport.EnrollAsync(_config, identity, CreateDeviceInfo(identity), cancellationToken).ConfigureAwait(false);
            var batch = await _queue.ReadBatchAsync(100, cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                return;
            }

            await _transport.SendBatchAsync(_config, identity, CreateDeviceInfo(identity), batch, cancellationToken).ConfigureAwait(false);
            await _queue.RemoveSentAsync(batch.Count, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _flushGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await StopLoopsAsync().ConfigureAwait(false);
        await CollectNowAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<TelemetryIdentity> GetIdentityAsync(CancellationToken cancellationToken)
    {
        _identity ??= await TelemetryIdentity.LoadOrCreateAsync(_telemetryDirectory, cancellationToken).ConfigureAwait(false);
        return _identity;
    }

    private void RequestImmediateSync()
    {
        if (!_enabled)
        {
            return;
        }

        _ = Task.Run(() => TryIgnoreNetworkErrorsAsync(() => CollectNowAsync(CancellationToken.None)));
    }

    private async Task StopLoopsAsync()
    {
        var cancellation = _loopCancellation;
        _loopCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        await cancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            if (_uploadLoop is not null)
            {
                await _uploadLoop.ConfigureAwait(false);
            }

            if (_commandLoop is not null)
            {
                await _commandLoop.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
            _uploadLoop = null;
            _commandLoop = null;
        }
    }

    private async Task RunUploadLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_config.UploadInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await TryIgnoreNetworkErrorsAsync(() => CollectNowAsync(cancellationToken)).ConfigureAwait(false);
        }
    }

    private async Task RunCommandLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_config.CommandPollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await TryIgnoreNetworkErrorsAsync(async () =>
            {
                var identity = await GetIdentityAsync(cancellationToken).ConfigureAwait(false);
                var commands = await _transport.FetchCommandsAsync(_config, identity, cancellationToken).ConfigureAwait(false);
                foreach (var command in commands)
                {
                    if (string.Equals(command.Type, "collect_now", StringComparison.OrdinalIgnoreCase))
                    {
                        await CollectNowAsync(cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var handler = CommandReceived;
                    if (handler is not null)
                    {
                        await handler(command, cancellationToken).ConfigureAwait(false);
                    }
                }
            }).ConfigureAwait(false);
        }
    }

    private static async Task TryIgnoreNetworkErrorsAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }

    private static TelemetryDeviceInfo CreateDeviceInfo(TelemetryIdentity identity)
    {
        return new TelemetryDeviceInfo
        {
            AppVersion = GetApplicationVersion(),
            InstalledAt = identity.InstalledAt
        };
    }

    private static string GetApplicationVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        var informationalVersion = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+', 2)[0];
        }

        return assembly?.GetName().Version?.ToString() ?? "dev";
    }

    private static TelemetryConnectionInfo ToConnectionInfo(ProxyProfile profile)
    {
        return new TelemetryConnectionInfo
        {
            ProfileIdHash = HashNullable(profile.Id),
            Name = profile.Name,
            Protocol = profile.Protocol,
            Host = profile.Host,
            Port = profile.Port,
            Network = profile.Network,
            Security = profile.Security,
            Sni = string.IsNullOrWhiteSpace(profile.Sni) ? null : profile.Sni,
            FromSubscription = !string.IsNullOrWhiteSpace(profile.SubscriptionUrl)
        };
    }

    private static string? HashNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
