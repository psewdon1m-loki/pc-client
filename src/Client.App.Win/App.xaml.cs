namespace Client.App.Win;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\LokiClient.SingleInstance";
    private const string ActivationEventName = @"Local\LokiClient.Activate";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationListenerCancellation;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _ownsSingleInstanceMutex = true;
        _activationEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ActivationEventName);
        _activationListenerCancellation = new CancellationTokenSource();
        StartActivationListener(_activationListenerCancellation.Token);

        base.OnStartup(e);
        FontBootstrapper.EnsureInstalled();
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _activationListenerCancellation?.Cancel();
        _activationEvent?.Set();
        _activationEvent?.Dispose();
        _activationListenerCancellation?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void StartActivationListener(CancellationToken cancellationToken)
    {
        var activationEvent = _activationEvent;
        if (activationEvent is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            var waitHandles = new WaitHandle[] { activationEvent, cancellationToken.WaitHandle };
            while (!cancellationToken.IsCancellationRequested)
            {
                if (WaitHandle.WaitAny(waitHandles) != 0 || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is MainWindow window)
                    {
                        window.ActivateFromExternalLaunch();
                    }
                });
            }
        }, CancellationToken.None);
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }
}
