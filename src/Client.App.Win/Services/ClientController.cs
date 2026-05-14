using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Client.Core;
using Client.Platform.Windows;
using Client.Profiles;
using Client.Routing;
using Client.Storage;
using Client.Telemetry;
using Client.Transport.Xray;
using Client.Updater;

namespace Client.App.Win.Services;

public sealed class ClientController
{
    private readonly AppPaths _paths = AppPaths.CreateDefault();
    private readonly ClientDatabase _database;
    private readonly ProfileRepository _profiles;
    private readonly SettingsRepository _settings;
    private readonly LastKnownGoodConfigStore _lastKnownGood;
    private readonly VlessParser _vlessParser = new();
    private readonly OzonDirectRuleProvider _ozonProvider = new();
    private readonly RuleSetProvider _ruleSetProvider = new();
    private readonly XrayConfigRenderer _renderer = new();
    private readonly XrayConfigValidator _validator = new();
    private readonly XrayProcessManager _xray = new();
    private readonly SystemProxyService _proxy = new();
    private readonly ProxyConnectivityVerifier _connectivityVerifier = new();
    private readonly BrowserProxyCompatibilityService _browserProxyCompatibility = new();
    private readonly ServiceTrafficProxy _serviceTrafficProxy = new();
    private readonly HttpClient _updateStateHttpClient;
    private readonly GeoAssetUpdater _geoUpdater;
    private readonly UpdateService _updateService;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly SemaphoreSlim _updateCheckLock = new(1, 1);
    private readonly TelemetryService _telemetry;
    private readonly SimpleFileLogger _logger;
    private SystemProxyState? _previousProxyState;
    private string? _lastGeneratedConfig;
    private CancellationTokenSource? _geoLoopCancellation;
    private Task? _geoLoop;
    private CancellationTokenSource? _updateLoopCancellation;
    private Task? _updateLoop;
    private int _operationGeneration;

    public ClientController()
    {
        _ = _proxy.DisableLokiLocalHttpProxy();
        _geoUpdater = new GeoAssetUpdater(CreateServiceHttpClient(TimeSpan.FromSeconds(60)));
        _updateService = new UpdateService(CreateServiceHttpClient(TimeSpan.FromSeconds(60)));
        _updateStateHttpClient = CreateServiceHttpClient(TimeSpan.FromSeconds(10));
        _database = new ClientDatabase(_paths.DatabasePath);
        _profiles = new ProfileRepository(_database);
        _settings = new SettingsRepository(_database);
        _lastKnownGood = new LastKnownGoodConfigStore(_paths.LastKnownGoodConfigPath);
        _logger = new SimpleFileLogger(_paths);
        _telemetry = new TelemetryService(
            _paths,
            new TelemetryTransport(CreateServiceHttpClient(TimeSpan.FromSeconds(15))),
            new NetworkTrafficSampler());
        _telemetry.CommandReceived += HandleTelemetryCommandAsync;
    }

    public AppPaths Paths => _paths;
    public ConnectionSnapshot Snapshot { get; private set; } = new();

    private HttpClient CreateServiceHttpClient(TimeSpan timeout, bool allowInvalidTls = false)
    {
        var handler = new HttpClientHandler
        {
            UseProxy = true,
            Proxy = _serviceTrafficProxy
        };

        if (allowInvalidTls)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return new HttpClient(handler)
        {
            Timeout = timeout
        };
    }

    public void ResetErrorState()
    {
        if (Snapshot.State == ConnectionStates.Error)
        {
            Snapshot = Snapshot with
            {
                State = ConnectionStates.Disconnected,
                LastError = null,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _database.Initialize();
        new AssetBootstrapper(_paths).EnsureRuntimeAssets();
        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        await CleanupStaleRuntimeAsync(settings, cancellationToken).ConfigureAwait(false);
        await CleanupBrowserProxyCompatibilityAsync(cancellationToken).ConfigureAwait(false);
        var profiles = await _profiles.ListAsync(cancellationToken).ConfigureAwait(false);
        _telemetry.UpdateContext(settings, profiles);
        await _telemetry.ConfigureAsync(settings.LogsConsent, cancellationToken).ConfigureAwait(false);
        StartGeoAssetLoop();
        ConfigureAutoUpdateLoop(settings.AutoUpdateRules);
        _ = RunBackgroundAsync(
            () => _telemetry.ReportContextChangedAsync(CancellationToken.None),
            "startup telemetry context");
        _ = RecordTelemetryStatusInBackground(Snapshot, "startup telemetry status");
        await _logger.InfoAsync(
            $"Loki start up | Loki Proxy | {AppContext.BaseDirectory} | {Environment.OSVersion.VersionString}",
            cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ProxyProfile>> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        return _profiles.ListAsync(cancellationToken);
    }

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetClientDisplayIdAsync(CancellationToken cancellationToken = default)
    {
        var identity = await TelemetryIdentity
            .LoadOrCreateAsync(Path.Combine(_paths.DataDirectory, "telemetry"), cancellationToken)
            .ConfigureAwait(false);
        return identity.DisplayId;
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _settings.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        var profiles = await _profiles.ListAsync(cancellationToken).ConfigureAwait(false);
        _telemetry.UpdateContext(settings, profiles);
        _ = RunBackgroundAsync(
            () => ConfigureAndReportTelemetryAsync(settings.LogsConsent),
            "settings telemetry update");
        ConfigureAutoUpdateLoop(settings.AutoUpdateRules);
    }

    public async Task SetActiveProfileAsync(string? profileId, CancellationToken cancellationToken = default)
    {
        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _settings.SaveAsync(settings with { ActiveProfileId = profileId }, cancellationToken).ConfigureAwait(false);
        await RefreshTelemetryContextAsync(cancellationToken).ConfigureAwait(false);
        _ = RunBackgroundAsync(
            () => _telemetry.ReportContextChangedAsync(CancellationToken.None),
            "active profile telemetry update");
    }

    public async Task DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await _profiles.DeleteAsync(profileId, cancellationToken).ConfigureAwait(false);
        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (settings.ActiveProfileId == profileId)
        {
            await _settings.SaveAsync(settings with { ActiveProfileId = null }, cancellationToken).ConfigureAwait(false);
        }

        await RefreshTelemetryContextAsync(cancellationToken).ConfigureAwait(false);
        _ = RunBackgroundAsync(
            () => _telemetry.ReportContextChangedAsync(CancellationToken.None),
            "delete profile telemetry update");
    }

    public async Task<OperationResult<IReadOnlyList<ProxyProfile>>> ImportAsync(string input, CancellationToken cancellationToken = default)
    {
        if (input.TrimStart().StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = _vlessParser.Parse(input);
            if (!parsed.Success || parsed.Value is null)
            {
                return OperationResult<IReadOnlyList<ProxyProfile>>.Fail(parsed.Message);
            }

            await _profiles.UpsertAsync(parsed.Value, cancellationToken).ConfigureAwait(false);
            var directSettings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(directSettings.ActiveProfileId))
            {
                await _settings.SaveAsync(directSettings with { ActiveProfileId = parsed.Value.Id }, cancellationToken).ConfigureAwait(false);
            }

            await RefreshTelemetryContextAsync(cancellationToken).ConfigureAwait(false);
            _ = RunBackgroundAsync(
                () => _telemetry.ReportContextChangedAsync(CancellationToken.None),
                "import profile telemetry update");
            await _logger.InfoAsync($"Imported VLESS profile {parsed.Value.Name}.", cancellationToken).ConfigureAwait(false);
            return OperationResult<IReadOnlyList<ProxyProfile>>.Ok([parsed.Value], "Профиль добавлен.");
        }

        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var fetched = await new SubscriptionClient(
                CreateServiceHttpClient(TimeSpan.FromSeconds(60), settings.AllowInvalidSubscriptionTls))
            .FetchAsync(input.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (!fetched.Success || fetched.Value is null)
        {
            return fetched;
        }

        foreach (var profile in fetched.Value)
        {
            await _profiles.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);
        }

        var currentSettings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(currentSettings.ActiveProfileId))
        {
            await _settings.SaveAsync(currentSettings with { ActiveProfileId = fetched.Value[0].Id }, cancellationToken).ConfigureAwait(false);
        }

        await RefreshTelemetryContextAsync(cancellationToken).ConfigureAwait(false);
        _ = RunBackgroundAsync(
            () => _telemetry.ReportContextChangedAsync(CancellationToken.None),
            "import subscription telemetry update");
        await _logger.InfoAsync($"Imported subscription profiles: {fetched.Value.Count}.", cancellationToken).ConfigureAwait(false);
        return OperationResult<IReadOnlyList<ProxyProfile>>.Ok(fetched.Value, "Subscription импортирован.");
    }

    public async Task<OperationResult> ConnectAsync(string? profileId = null, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ConnectCoreAsync(profileId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<OperationResult> ConnectCoreAsync(string? profileId = null, CancellationToken cancellationToken = default)
    {
        if (Snapshot.State == ConnectionStates.Connected)
        {
            return OperationResult.Ok("Already connected.");
        }

        var operationGeneration = Interlocked.Increment(ref _operationGeneration);
        _ = RunBackgroundAsync(
            () => PrepareBrowserProxyCompatibilityAsync(CancellationToken.None),
            "browser proxy compatibility");
        await CleanupStaleRuntimeAsync(await _settings.LoadAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        Snapshot = Snapshot with { State = ConnectionStates.Connecting, LastError = null, UpdatedAt = DateTimeOffset.UtcNow };
        _ = RecordTelemetryStatusInBackground(Snapshot, "connecting telemetry");
        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var profiles = await _profiles.ListAsync(cancellationToken).ConfigureAwait(false);
        _telemetry.UpdateContext(settings, profiles);
        await _logger.InfoAsync(
            $"Connect requested | profiles={profiles.Count} | routing={settings.RoutingMode} | socks={settings.SocksPort} | http={settings.HttpPort}",
            cancellationToken).ConfigureAwait(false);
        if (profiles.Count == 0)
        {
            Snapshot = Snapshot with { State = ConnectionStates.Error, LastError = "no connections" };
            await _telemetry.RecordStatusAsync(Snapshot, cancellationToken).ConfigureAwait(false);
            return OperationResult.Fail("no connections");
        }

        var activeProfile = !string.IsNullOrWhiteSpace(profileId)
            ? profiles.FirstOrDefault(profile => profile.Id == profileId)
            : profiles.FirstOrDefault(profile => profile.Id == settings.ActiveProfileId) ?? profiles.FirstOrDefault();

        if (activeProfile is null)
        {
            Snapshot = Snapshot with { State = ConnectionStates.Error, LastError = "Нет профилей для подключения." };
            await _telemetry.RecordStatusAsync(Snapshot, cancellationToken).ConfigureAwait(false);
            return OperationResult.Fail("Сначала добавьте VLESS профиль или subscription URL.");
        }

        await _logger.InfoAsync(
            $"Active profile resolved | requested={profileId ?? "<settings>"} | settingsActive={settings.ActiveProfileId ?? "<none>"} | id={activeProfile.Id} | name={activeProfile.Name} | host={activeProfile.Host}:{activeProfile.Port}",
            cancellationToken).ConfigureAwait(false);

        _ = RunBackgroundAsync(VerifyTelemetryAsync, "telemetry verification");

        var effectiveSettings = EnsureAvailablePorts(settings);
        if (effectiveSettings.SocksPort != settings.SocksPort || effectiveSettings.HttpPort != settings.HttpPort)
        {
            await _logger.InfoAsync(
                $"Local ports changed because defaults were busy. socks={effectiveSettings.SocksPort}, http={effectiveSettings.HttpPort}.",
                cancellationToken).ConfigureAwait(false);
        }

        var geoOptions = new GeoAssetOptions { GeoDirectory = _paths.RuntimeDirectory };
        if (HasUsableGeoAssets(geoOptions.GeoDirectory))
        {
            _ = RunBackgroundAsync(
                () => RefreshGeoAssetsAsync(geoOptions, CancellationToken.None),
                "geo asset refresh");
        }
        else
        {
            await RefreshGeoAssetsAsync(geoOptions, cancellationToken).ConfigureAwait(false);
        }

        var ozonRule = _ozonProvider.LoadOrDefault(Path.Combine(_paths.OverridesDirectory, "ozon.direct.json"));
        var ruleSet = _ruleSetProvider.LoadRuleSetOrDefault(_paths.RuleSetsDirectory, effectiveSettings.RoutingMode, ozonRule);

        var config = _renderer.Render(
            activeProfile,
            effectiveSettings,
            ruleSet.Rules,
            ruleSet.DomainStrategy,
            Path.Combine(_paths.LogsDirectory, "xray-access.log"),
            Path.Combine(_paths.LogsDirectory, "xray-error.log"));
        var validation = _validator.ValidateJson(config);
        if (!validation.Success)
        {
            Snapshot = Snapshot with { State = ConnectionStates.Error, LastError = validation.Message };
            await _telemetry.RecordStatusAsync(Snapshot, cancellationToken).ConfigureAwait(false);
            return validation;
        }

        var configPath = Path.Combine(_paths.RuntimeDirectory, "config.json");
        await File.WriteAllTextAsync(configPath, config, cancellationToken).ConfigureAwait(false);
        _lastGeneratedConfig = config;
        await _logger.InfoAsync(
            $"Xray config rendered | profile={activeProfile.Name} | host={activeProfile.Host}:{activeProfile.Port} | path={configPath}",
            cancellationToken).ConfigureAwait(false);

        if (_previousProxyState is null)
        {
            var currentProxyState = _proxy.Read();
            _previousProxyState = _proxy.HasLokiLocalProxyTraces(currentProxyState) ? null : currentProxyState;
        }
        var xrayResult = await _xray.StartAsync(new XrayRuntimeOptions
        {
            XrayExecutablePath = Path.Combine(_paths.RuntimeDirectory, "xray.exe"),
            WorkingDirectory = _paths.RuntimeDirectory,
            ConfigPath = configPath,
            SocksPort = effectiveSettings.SocksPort,
            HttpPort = effectiveSettings.HttpPort
        }, cancellationToken).ConfigureAwait(false);

        if (!xrayResult.Success)
        {
            Snapshot = Snapshot with { State = ConnectionStates.Error, ActiveProfileName = activeProfile.Name, LastError = xrayResult.Message };
            await _logger.ErrorAsync(xrayResult.Message, cancellationToken).ConfigureAwait(false);
            await _telemetry.RecordStatusAsync(Snapshot, cancellationToken).ConfigureAwait(false);
            return xrayResult;
        }

        _serviceTrafficProxy.EnableHttpProxy(effectiveSettings.HttpPort);
        await _logger.InfoAsync(
            $"Service traffic proxy enabled | http=127.0.0.1:{effectiveSettings.HttpPort}",
            cancellationToken).ConfigureAwait(false);

        if (effectiveSettings.RoutingMode != RoutingModes.LocalOnly)
        {
            var proxyResult = _proxy.EnableHttpProxy(effectiveSettings.HttpPort, effectiveSettings.SocksPort);
            if (!proxyResult.Success)
            {
                _serviceTrafficProxy.Disable();
                await _xray.StopAsync(cancellationToken).ConfigureAwait(false);
                Snapshot = Snapshot with { State = ConnectionStates.Error, ActiveProfileName = activeProfile.Name, LastError = proxyResult.Message };
                await _logger.ErrorAsync(proxyResult.Message, cancellationToken).ConfigureAwait(false);
                await _telemetry.RecordStatusAsync(Snapshot, cancellationToken).ConfigureAwait(false);
                return proxyResult;
            }

            var proxyState = _proxy.Read();
            await _logger.InfoAsync(
                $"System proxy enabled | http=127.0.0.1:{effectiveSettings.HttpPort} | wininet={proxyState.ProxyServer ?? "<empty>"} | winhttp={proxyState.WinHttpProxy ?? "<empty>"}",
                cancellationToken).ConfigureAwait(false);
            if (!_proxy.IsEnabledForLocalHttpProxy(effectiveSettings.HttpPort))
            {
                _serviceTrafficProxy.Disable();
                await _xray.StopAsync(cancellationToken).ConfigureAwait(false);
                RestoreOrClearProxyState();

                var message = "Windows proxy settings were not applied.";
                Snapshot = Snapshot with { State = ConnectionStates.Error, ActiveProfileName = activeProfile.Name, LastError = message };
                await _logger.ErrorAsync(message, cancellationToken).ConfigureAwait(false);
                await _telemetry.RecordStatusAsync(Snapshot, cancellationToken).ConfigureAwait(false);
                return OperationResult.Fail(message);
            }

            if (!_proxy.IsWinHttpEnabledForLocalHttpProxy(effectiveSettings.HttpPort))
            {
                await _logger.InfoAsync(
                    "WinHTTP proxy was not applied; continuing with WinINet system proxy.",
                    cancellationToken).ConfigureAwait(false);
            }

            _ = RunBackgroundAsync(
                () => VerifyProxyHealthAsync(effectiveSettings.HttpPort, operationGeneration),
                "proxy health verification");
        }

        var connectedSettings = effectiveSettings with { ActiveProfileId = activeProfile.Id };
        _telemetry.UpdateContext(connectedSettings, profiles);
        var connectedSnapshot = new ConnectionSnapshot
        {
            State = ConnectionStates.Connected,
            ActiveProfileName = activeProfile.Name,
            RoutingMode = effectiveSettings.RoutingMode,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        Snapshot = connectedSnapshot;
        _ = RunBackgroundAsync(
            () => FinalizeConnectedAsync(config, connectedSettings, activeProfile.Name, connectedSnapshot, operationGeneration),
            "connected finalization");
        return OperationResult.Ok("Подключено.");
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task DisconnectCoreAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _operationGeneration);
        if (Snapshot.State == ConnectionStates.Disconnected
            && _previousProxyState is null
            && !_xray.IsRunning)
        {
            _serviceTrafficProxy.Disable();
            await CleanupBrowserProxyCompatibilityAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await _logger.InfoAsync("Disconnect requested.", cancellationToken).ConfigureAwait(false);
        _serviceTrafficProxy.Disable();
        await _logger.InfoAsync("Service traffic proxy disabled.", cancellationToken).ConfigureAwait(false);
        RestoreOrClearProxyState();
        await _logger.InfoAsync("System proxy restored or Loki proxy traces disabled.", cancellationToken).ConfigureAwait(false);

        await _xray.StopAsync(cancellationToken).ConfigureAwait(false);
        await _logger.InfoAsync("Xray process manager stopped.", cancellationToken).ConfigureAwait(false);
        await XrayProcessManager.KillProcessesByExecutablePathAsync(GetXrayExecutablePath(), cancellationToken).ConfigureAwait(false);
        await CleanupBrowserProxyCompatibilityAsync(cancellationToken).ConfigureAwait(false);
        Snapshot = Snapshot with { State = ConnectionStates.Disconnected, UpdatedAt = DateTimeOffset.UtcNow };
        await _logger.InfoAsync("Disconnected | system proxy restored | xray stopped.", cancellationToken).ConfigureAwait(false);
        _ = RecordTelemetryStatusInBackground(Snapshot, "disconnected telemetry");
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await StopAutoUpdateLoopAsync().ConfigureAwait(false);
        await StopGeoAssetLoopAsync().ConfigureAwait(false);
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await _telemetry.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<UpdateCheckResult> CheckForUpdatesNowAsync(CancellationToken cancellationToken = default)
    {
        await _updateCheckLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var config = UpdateEndpointConfigLoader.Load(AppContext.BaseDirectory, _paths.DataDirectory);
            var result = await _updateService.CheckAndApplyAsync(
                config,
                UpdateService.GetCurrentApplicationVersion(),
                _paths.DataDirectory,
                _paths.RuleSetsDirectory,
                AppContext.BaseDirectory,
                cancellationToken).ConfigureAwait(false);
            await HandleUpdateResultAsync(result, cancellationToken).ConfigureAwait(false);
            var reportConfig = UpdateEndpointConfigLoader.Load(AppContext.BaseDirectory, _paths.DataDirectory);
            await ReportUpdateStateAsync(reportConfig, result, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _updateCheckLock.Release();
        }
    }

    public async Task<string> BuildPreviewConfigAsync(ProxyProfile profile, CancellationToken cancellationToken = default)
    {
        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var ozonRule = _ozonProvider.LoadOrDefault(Path.Combine(_paths.OverridesDirectory, "ozon.direct.json"));
        var ruleSet = _ruleSetProvider.LoadRuleSetOrDefault(_paths.RuleSetsDirectory, settings.RoutingMode, ozonRule);
        return _renderer.Render(profile, settings, ruleSet.Rules, ruleSet.DomainStrategy);
    }

    private static AppSettings EnsureAvailablePorts(AppSettings settings)
    {
        if (IsPortFree(settings.SocksPort) && IsPortFree(settings.HttpPort))
        {
            return settings;
        }

        for (var socks = 18088; socks < 18188; socks += 2)
        {
            var http = socks + 1;
            if (IsPortFree(socks) && IsPortFree(http))
            {
                return settings with { SocksPort = socks, HttpPort = http };
            }
        }

        throw new InvalidOperationException("Не удалось найти свободные локальные порты для Xray.");
    }

    private async Task RefreshTelemetryContextAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var profiles = await _profiles.ListAsync(cancellationToken).ConfigureAwait(false);
        _telemetry.UpdateContext(settings, profiles);
    }

    private Task RecordTelemetryStatusInBackground(ConnectionSnapshot snapshot, string operation)
    {
        return RunBackgroundAsync(
            () => _telemetry.RecordStatusAsync(snapshot, CancellationToken.None),
            operation);
    }

    private async Task ConfigureAndReportTelemetryAsync(bool includeLogs)
    {
        await _telemetry.ConfigureAsync(includeLogs, CancellationToken.None).ConfigureAwait(false);
        await _telemetry.ReportContextChangedAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task FinalizeConnectedAsync(
        string config,
        AppSettings connectedSettings,
        string profileName,
        ConnectionSnapshot connectedSnapshot,
        int operationGeneration)
    {
        await _lastKnownGood.SaveAsync(config, CancellationToken.None).ConfigureAwait(false);
        await _settings.SaveAsync(connectedSettings, CancellationToken.None).ConfigureAwait(false);
        if (operationGeneration != Volatile.Read(ref _operationGeneration)
            || Snapshot.State != ConnectionStates.Connected)
        {
            return;
        }

        await _logger.InfoAsync($"Connected profile {profileName}.", CancellationToken.None).ConfigureAwait(false);
        await _telemetry.RecordStatusAsync(connectedSnapshot, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task VerifyTelemetryAsync()
    {
        var verification = await _telemetry.VerifyAsync(CancellationToken.None).ConfigureAwait(false);
        if (!verification.Success)
        {
            await _logger.ErrorAsync(
                $"Telemetry verification failed; connection startup was not blocked. {verification.Message}",
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task RefreshGeoAssetsAsync(GeoAssetOptions options, CancellationToken cancellationToken)
    {
        var updateResult = await _geoUpdater.EnsureFreshAsync(options, cancellationToken).ConfigureAwait(false);
        if (!updateResult.Success)
        {
            await _logger.ErrorAsync(updateResult.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private void StartGeoAssetLoop()
    {
        if (_geoLoopCancellation is not null)
        {
            return;
        }

        _geoLoopCancellation = new CancellationTokenSource();
        _geoLoop = RunGeoAssetLoopAsync(_geoLoopCancellation.Token);
    }

    private async Task StopGeoAssetLoopAsync()
    {
        var cancellation = _geoLoopCancellation;
        _geoLoopCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        await cancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            if (_geoLoop is not null)
            {
                await _geoLoop.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
            _geoLoop = null;
        }
    }

    private async Task RunGeoAssetLoopAsync(CancellationToken cancellationToken)
    {
        var options = new GeoAssetOptions { GeoDirectory = _paths.RuntimeDirectory };
        await RefreshGeoAssetsAsync(options, cancellationToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(options.Ttl);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await RefreshGeoAssetsAsync(options, cancellationToken).ConfigureAwait(false);
        }
    }

    private void ConfigureAutoUpdateLoop(bool enabled)
    {
        if (enabled)
        {
            if (_updateLoopCancellation is not null && _updateLoop is { IsCompleted: false })
            {
                return;
            }

            if (_updateLoopCancellation is not null)
            {
                _updateLoopCancellation.Dispose();
            }

            _updateLoopCancellation = new CancellationTokenSource();
            _updateLoop = RunAutoUpdateLoopAsync(_updateLoopCancellation.Token);
            return;
        }

        _ = StopAutoUpdateLoopAsync();
    }

    private async Task StopAutoUpdateLoopAsync()
    {
        var cancellation = _updateLoopCancellation;
        _updateLoopCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        await cancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            if (_updateLoop is not null)
            {
                await _updateLoop.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
            _updateLoop = null;
        }
    }

    private async Task RunAutoUpdateLoopAsync(CancellationToken cancellationToken)
    {
        await _logger.InfoAsync("Auto-update loop started.", cancellationToken).ConfigureAwait(false);
        try
        {
            await RunSingleUpdateCheckAsync(cancellationToken).ConfigureAwait(false);
            await WaitAndRunStartupUpdateRetryAsync(cancellationToken).ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested)
            {
                var config = UpdateEndpointConfigLoader.Load(AppContext.BaseDirectory, _paths.DataDirectory);
                var interval = config.CheckInterval;
                using var timer = new PeriodicTimer(interval);
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                await RunSingleUpdateCheckAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Auto-update loop failed: {ex.Message}", CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task WaitAndRunStartupUpdateRetryAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        await RunSingleUpdateCheckAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunSingleUpdateCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await CheckForUpdatesNowAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Success && !result.ManifestUnavailable)
            {
                await _logger.ErrorAsync(result.Message, cancellationToken).ConfigureAwait(false);
            }
            else if (!result.ManifestUnavailable)
            {
                await _logger.InfoAsync(result.Message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Auto-update check failed: {ex.Message}", CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task HandleUpdateResultAsync(UpdateCheckResult result, CancellationToken cancellationToken)
    {
        if (!result.Success)
        {
            return;
        }

        if (result.Watcher is not null)
        {
            await ApplyWatcherConfigAsync(result.Watcher, cancellationToken).ConfigureAwait(false);
        }

        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var activeRuleSet = RuleSetProvider.GetRuleSetFileId(settings.RoutingMode);
        if (Snapshot.State == ConnectionStates.Connected && result.UpdatedRuleSets.Contains(activeRuleSet))
        {
            await _logger.InfoAsync($"Active rule-set updated remotely: {activeRuleSet}. Reconnecting.", cancellationToken).ConfigureAwait(false);
            await DisconnectAsync(cancellationToken).ConfigureAwait(false);
            await ConnectAsync(settings.ActiveProfileId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReportUpdateStateAsync(
        UpdateEndpointConfig config,
        UpdateCheckResult result,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildUpdateStateEndpoint(config.ManifestUrl);
        if (endpoint is null)
        {
            return;
        }

        try
        {
            var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            var telemetryDirectory = Path.Combine(_paths.DataDirectory, "telemetry");
            var identity = await TelemetryIdentity.LoadOrCreateAsync(telemetryDirectory, cancellationToken).ConfigureAwait(false);
            var watcherConfig = TelemetryEndpointConfig.Load(AppContext.BaseDirectory, _paths.DataDirectory);
            var activeRuleSet = RuleSetProvider.GetRuleSetFileId(settings.RoutingMode);
            var body = JsonSerializer.Serialize(new
            {
                clientId = identity.ClientId,
                displayId = identity.DisplayId,
                appVersion = UpdateService.GetCurrentApplicationVersion(),
                routingMode = settings.RoutingMode,
                activeRuleSet,
                autoUpdatesEnabled = settings.AutoUpdateRules,
                logsUploadEnabled = settings.LogsConsent,
                watcherEndpoint = watcherConfig.Endpoint.ToString().TrimEnd('/'),
                watcherSni = watcherConfig.SniHost ?? string.Empty,
                updateManifestUrl = config.ManifestUrl?.ToString(),
                updateFallbackManifestUrl = config.FallbackManifestUrl?.ToString(),
                lastCheckSuccess = result.Success,
                lastCheckMessage = result.Message,
                updatedRuleSets = result.UpdatedRuleSets,
                ruleSets = BuildRuleSetState(),
                timestamp = DateTimeOffset.UtcNow
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            using var response = await _updateStateHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.NotFound)
            {
                await _telemetry.EnsureEnrolledAsync(cancellationToken).ConfigureAwait(false);
                using var retryRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                using var retryResponse = await _updateStateHttpClient.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
                if (!retryResponse.IsSuccessStatusCode)
                {
                    await _logger.InfoAsync($"Update state report skipped after re-enroll: HTTP {(int)retryResponse.StatusCode}", cancellationToken).ConfigureAwait(false);
                }
            }
            else if (!response.IsSuccessStatusCode)
            {
                await _logger.InfoAsync($"Update state report skipped: HTTP {(int)response.StatusCode}", cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or JsonException)
        {
            await _logger.InfoAsync($"Update state report failed: {ex.Message}", CancellationToken.None).ConfigureAwait(false);
        }
    }

    private IReadOnlyList<object> BuildRuleSetState()
    {
        var ids = new[] { "russia-smart", "global", "whitelist", "blacklist" };
        return ids.Select(id =>
        {
            var zipPath = Path.Combine(_paths.RuleSetsDirectory, id + ".zip");
            var jsonPath = Path.Combine(_paths.RuleSetsDirectory, id + ".json");
            var path = File.Exists(zipPath) ? zipPath : File.Exists(jsonPath) ? jsonPath : null;
            return new
            {
                id,
                source = path is null ? "fallback" : Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase) ? "zip" : "json",
                sha256 = path is null ? string.Empty : FileSha256(path)
            };
        }).ToArray();
    }

    private static Uri? BuildUpdateStateEndpoint(Uri? manifestUrl)
    {
        if (manifestUrl is null || manifestUrl.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var builder = new UriBuilder(manifestUrl)
        {
            Path = "/api/v1/update-state",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private async Task ApplyWatcherConfigAsync(UpdateWatcherConfig watcher, CancellationToken cancellationToken)
    {
        var watcherEndpoint = watcher.Endpoint.TrimEnd('/');
        var envPath = Path.Combine(_paths.DataDirectory, "loki.env");
        UpsertEnvValues(envPath, new Dictionary<string, string>
        {
            ["LOKI_TELEMETRY_ENDPOINT"] = watcherEndpoint,
            ["LOKI_TELEMETRY_SNI"] = watcher.Sni ?? string.Empty,
            ["LOKI_UPDATE_MANIFEST_URL"] = $"{watcherEndpoint}/manifest.json"
        });
        await _logger.InfoAsync($"Watcher endpoint updated: {watcherEndpoint}", cancellationToken).ConfigureAwait(false);
        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAndReportTelemetryAsync(settings.LogsConsent).ConfigureAwait(false);
    }

    private async Task HandleTelemetryCommandAsync(TelemetryCommand command, CancellationToken cancellationToken)
    {
        if (string.Equals(command.Type, "check_updates", StringComparison.OrdinalIgnoreCase))
        {
            await CheckForUpdatesNowAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.Equals(command.Type, "set_watcher_endpoint", StringComparison.OrdinalIgnoreCase)
            && command.Payload is { ValueKind: System.Text.Json.JsonValueKind.Object } payload
            && payload.TryGetProperty("endpoint", out var endpointElement)
            && endpointElement.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var endpoint = endpointElement.GetString() ?? string.Empty;
            var sni = payload.TryGetProperty("sni", out var sniElement) && sniElement.ValueKind == System.Text.Json.JsonValueKind.String
                ? sniElement.GetString()
                : null;
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            {
                await ApplyWatcherConfigAsync(new UpdateWatcherConfig { Endpoint = endpoint, Sni = sni }, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void UpsertEnvValues(string path, IReadOnlyDictionary<string, string> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : [];
        foreach (var pair in values)
        {
            var prefix = pair.Key + "=";
            var index = lines.FindIndex(line => line.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            var line = $"{pair.Key}={pair.Value}";
            if (index >= 0)
            {
                lines[index] = line;
            }
            else
            {
                lines.Add(line);
            }
        }

        File.WriteAllLines(path, lines);
    }

    private async Task VerifyProxyHealthAsync(int httpPort, int operationGeneration)
    {
        if (operationGeneration != Volatile.Read(ref _operationGeneration))
        {
            return;
        }

        var proxyVerification = await _connectivityVerifier
            .VerifySystemProxyAsync(httpPort, CancellationToken.None)
            .ConfigureAwait(false);
        if (operationGeneration != Volatile.Read(ref _operationGeneration))
        {
            return;
        }

        if (proxyVerification.Success)
        {
            await _logger.InfoAsync(proxyVerification.Message, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            await _logger.ErrorAsync(proxyVerification.Message, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task RunBackgroundAsync(Func<Task> action, string operation)
    {
        try
        {
            await Task.Run(action).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"{operation} failed: {ex.Message}", CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static bool HasUsableGeoAssets(string geoDirectory)
    {
        return IsUsableGeoAsset(Path.Combine(geoDirectory, "geoip.dat"))
            && IsUsableGeoAsset(Path.Combine(geoDirectory, "geosite.dat"));
    }

    private static bool IsUsableGeoAsset(string path)
    {
        return File.Exists(path) && new FileInfo(path).Length >= 1024;
    }

    private async Task PrepareBrowserProxyCompatibilityAsync(CancellationToken cancellationToken)
    {
        var compatibility = _browserProxyCompatibility.EnsureSystemProxyCompatibility();
        if (compatibility.UpdatedProfileCount > 0)
        {
            await _logger.InfoAsync(
                $"Browser proxy compatibility updated | profiles={compatibility.ProfileCount} | updated={compatibility.UpdatedProfileCount} | running={compatibility.RunningProcessCount}",
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var error in compatibility.Errors)
        {
            await _logger.ErrorAsync($"Browser proxy compatibility failed | {error}", cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CleanupBrowserProxyCompatibilityAsync(CancellationToken cancellationToken)
    {
        var compatibility = _browserProxyCompatibility.RemoveSystemProxyCompatibility();
        if (compatibility.UpdatedProfileCount > 0)
        {
            await _logger.InfoAsync(
                $"Browser proxy compatibility cleaned | profiles={compatibility.ProfileCount} | updated={compatibility.UpdatedProfileCount} | running={compatibility.RunningProcessCount}",
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var error in compatibility.Errors)
        {
            await _logger.ErrorAsync($"Browser proxy compatibility cleanup failed | {error}", cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CleanupStaleRuntimeAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (_proxy.HasLokiLocalProxyTraces())
        {
            var proxyResult = _proxy.DisableLokiLocalHttpProxy();
            if (!proxyResult.Success)
            {
                await _logger.ErrorAsync(proxyResult.Message, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _logger.InfoAsync("Cleaned local proxy traces before Loki start/connect.", cancellationToken).ConfigureAwait(false);
            }
        }

        var killed = await XrayProcessManager.KillProcessesByExecutablePathAsync(GetXrayExecutablePath(), cancellationToken).ConfigureAwait(false);
        if (killed > 0)
        {
            await _logger.InfoAsync($"Cleaned stale Loki Xray processes | count={killed}", cancellationToken).ConfigureAwait(false);
        }
    }

    private void RestoreOrClearProxyState()
    {
        var previous = _previousProxyState;
        _previousProxyState = null;
        if (previous is not null && !_proxy.HasLokiLocalProxyTraces(previous))
        {
            _ = _proxy.Restore(previous);
            return;
        }

        _ = _proxy.DisableLokiLocalHttpProxy();
    }

    private string GetXrayExecutablePath()
    {
        return Path.Combine(_paths.RuntimeDirectory, "xray.exe");
    }

    private static bool IsPortFree(int port)
    {
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        return properties.GetActiveTcpListeners().All(endpoint => endpoint.Port != port)
            && properties.GetActiveTcpConnections().All(connection => connection.LocalEndPoint.Port != port);
    }

}
