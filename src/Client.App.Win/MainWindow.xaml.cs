using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Client.App.Win.Services;
using Client.Core;
using Forms = System.Windows.Forms;

namespace Client.App.Win;

public partial class MainWindow : Window
{
    private const int GwlStyle = -16;
    private const long WsSysMenu = 0x00080000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const int WmSysCommand = 0x0112;
    private const int ScMinimize = 0xF020;
    private const int ScRestore = 0xF120;
    private const string OnboardingFileName = "onboarding.done";
    private readonly ClientController _controller = new();
    private readonly ObservableCollection<ProfileRow> _profileRows = [];
    private readonly DispatcherTimer _clockTimer = new();
    private readonly DispatcherTimer _sessionTimer = new();
    private readonly DispatcherTimer _errorResetTimer = new();
    private readonly DispatcherTimer _operationProgressTimer = new();
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _trayTurnOnItem;
    private Forms.ToolStripMenuItem? _trayTurnOffItem;
    private System.Drawing.Icon? _trayIconImage;
    private AppSettings _settings = new();
    private ProfileRow? _contextProfile;
    private DateTimeOffset? _connectedAt;
    private bool _connected;
    private bool _busy;
    private bool _minimizeAfterAnimation;
    private bool _isMinimizeAnimationRunning;
    private bool _closeAfterAnimation;
    private bool _isCloseAnimationRunning;
    private bool _exitRequested;
    private bool _skipShutdownOnClose;
    private bool _isRefreshingProfiles;
    private DateTimeOffset _operationProgressStartedAt;
    private double _operationProgress;
    private HwndSource? _windowSource;

    public MainWindow()
    {
        InitializeComponent();
        ApplyPixelFont(this, FontBootstrapper.CreatePixelFontFamily(), []);
        ProfilesList.ItemsSource = _profileRows;
        InitializeTimers();
        InitializeTray();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlStyle).ToInt64();
        style |= WsSysMenu | WsMinimizeBox;
        style &= ~WsMaximizeBox;
        _ = SetWindowLongPtr(handle, GwlStyle, new IntPtr(style));
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _ = SetCurrentProcessExplicitAppUserModelID("Loki.Proxy.VPN");
        _ = PlayOpenAnimationAsync();

        await RunUiAsync(async () =>
        {
            await _controller.InitializeAsync();
            _settings = await _controller.LoadSettingsAsync();
            await RefreshProfilesAsync();
            UpdateSnapshot(_controller.Snapshot);
            ShowScreen(HasCompletedOnboarding() ? MainScreen : EntryScreen);
        });
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            e.Cancel = true;
            await HideToTrayAsync();
            return;
        }

        if (!_closeAfterAnimation)
        {
            e.Cancel = true;
            if (_isCloseAnimationRunning)
            {
                return;
            }

            _isCloseAnimationRunning = true;
            await PlayCloseAnimationAsync();
            _closeAfterAnimation = true;
            Close();
            return;
        }

        _windowSource?.RemoveHook(WindowMessageHook);
        _clockTimer.Stop();
        _sessionTimer.Stop();
        _errorResetTimer.Stop();
        _operationProgressTimer.Stop();
        _trayIcon?.Dispose();
        _trayIconImage?.Dispose();
        if (!_skipShutdownOnClose)
        {
            await _controller.ShutdownAsync();
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        _ = MinimizeSmoothlyAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSysCommand && (wParam.ToInt32() & 0xFFF0) == ScMinimize && !_minimizeAfterAnimation)
        {
            handled = true;
            _ = MinimizeSmoothlyAsync();
        }
        else if (msg == WmSysCommand && (wParam.ToInt32() & 0xFFF0) == ScRestore && WindowState == WindowState.Minimized)
        {
            handled = true;
            WindowState = WindowState.Normal;
            ResetWindowVisualState();
            Activate();
            _ = PlayOpenAnimationAsync();
        }

        return IntPtr.Zero;
    }

    private async Task MinimizeSmoothlyAsync()
    {
        if (_isMinimizeAnimationRunning || WindowState == WindowState.Minimized)
        {
            return;
        }

        _isMinimizeAnimationRunning = true;
        await AnimateMinimizeToIconAsync();
        _minimizeAfterAnimation = true;
        WindowState = WindowState.Minimized;
        ResetWindowVisualState();
        _minimizeAfterAnimation = false;
        _isMinimizeAnimationRunning = false;
    }

    private async Task HideToTrayAsync()
    {
        if (_isCloseAnimationRunning || !IsVisible)
        {
            return;
        }

        _isCloseAnimationRunning = true;
        await PlayCloseAnimationAsync();
        Hide();
        ResetWindowVisualState();
        _isCloseAnimationRunning = false;
    }

    private async Task PlayOpenAnimationAsync()
    {
        if (WindowState == WindowState.Minimized || _isCloseAnimationRunning)
        {
            return;
        }

        ResetWindowVisualState(opacity: 0, scaleX: 0.965, scaleY: 0.965);
        await AnimateWindowAsync(1, 1, 160);
    }

    private Task PlayCloseAnimationAsync()
    {
        return AnimateWindowAsync(0, 0.965, 140);
    }

    private Task AnimateWindowAsync(double opacity, double scale, int milliseconds)
    {
        var completion = new TaskCompletionSource();
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var opacityAnimation = new DoubleAnimation(opacity, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };
        var scaleXAnimation = new DoubleAnimation(scale, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };
        var scaleYAnimation = new DoubleAnimation(scale, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };

        opacityAnimation.Completed += (_, _) => completion.TrySetResult();
        BeginAnimation(OpacityProperty, opacityAnimation);
        WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation);
        WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnimation);

        return completion.Task;
    }

    private Task AnimateMinimizeToIconAsync()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var screen = Forms.Screen.FromHandle(handle).Bounds;
        var dpi = VisualTreeHelper.GetDpi(this);
        var screenLeft = screen.Left / dpi.DpiScaleX;
        var screenTop = screen.Top / dpi.DpiScaleY;
        var screenWidth = screen.Width / dpi.DpiScaleX;
        var screenHeight = screen.Height / dpi.DpiScaleY;
        var windowCenterX = Left + ActualWidth / 2;
        var windowCenterY = Top + ActualHeight / 2;
        var targetX = screenLeft + screenWidth / 2;
        var targetY = screenTop + screenHeight - 20;

        return AnimateWindowToAsync(
            opacity: 0,
            scaleX: 0.08,
            scaleY: 0.02,
            translateX: targetX - windowCenterX,
            translateY: targetY - windowCenterY,
            milliseconds: 320);
    }

    private Task AnimateWindowToAsync(double opacity, double scaleX, double scaleY, double translateX, double translateY, int milliseconds)
    {
        var completion = new TaskCompletionSource();
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var opacityAnimation = new DoubleAnimation(opacity, duration) { EasingFunction = easing, FillBehavior = FillBehavior.HoldEnd };
        var scaleXAnimation = new DoubleAnimation(scaleX, duration) { EasingFunction = easing, FillBehavior = FillBehavior.HoldEnd };
        var scaleYAnimation = new DoubleAnimation(scaleY, duration) { EasingFunction = easing, FillBehavior = FillBehavior.HoldEnd };
        var translateXAnimation = new DoubleAnimation(translateX, duration) { EasingFunction = easing, FillBehavior = FillBehavior.HoldEnd };
        var translateYAnimation = new DoubleAnimation(translateY, duration) { EasingFunction = easing, FillBehavior = FillBehavior.HoldEnd };

        opacityAnimation.Completed += (_, _) => completion.TrySetResult();
        BeginAnimation(OpacityProperty, opacityAnimation);
        WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation);
        WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnimation);
        WindowTranslate.BeginAnimation(TranslateTransform.XProperty, translateXAnimation);
        WindowTranslate.BeginAnimation(TranslateTransform.YProperty, translateYAnimation);

        return completion.Task;
    }

    private void ResetWindowVisualState(double opacity = 1, double scaleX = 1, double scaleY = 1, double translateX = 0, double translateY = 0)
    {
        BeginAnimation(OpacityProperty, null);
        WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        WindowTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        WindowTranslate.BeginAnimation(TranslateTransform.YProperty, null);

        Opacity = opacity;
        WindowScale.ScaleX = scaleX;
        WindowScale.ScaleY = scaleY;
        WindowTranslate.X = translateX;
        WindowTranslate.Y = translateY;
    }

    private void InitializeTimers()
    {
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        _sessionTimer.Interval = TimeSpan.FromSeconds(1);
        _sessionTimer.Tick += (_, _) => UpdateSessionTime();

        _errorResetTimer.Interval = TimeSpan.FromSeconds(3);
        _errorResetTimer.Tick += (_, _) =>
        {
            _errorResetTimer.Stop();
            _connected = false;
            _connectedAt = null;
            _sessionTimer.Stop();
            _controller.ResetErrorState();
            UpdateSnapshot(new ConnectionSnapshot
            {
                State = ConnectionStates.Disconnected,
                RoutingMode = _settings.RoutingMode,
                LastError = null,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        };

        _operationProgressTimer.Interval = TimeSpan.FromMilliseconds(50);
        _operationProgressTimer.Tick += (_, _) =>
        {
            var elapsed = DateTimeOffset.Now - _operationProgressStartedAt;
            var eased = 1 - Math.Exp(-elapsed.TotalSeconds / 2.2);
            _operationProgress = Math.Min(0.92, 0.08 + eased * 0.84);
            UpdateMainProgressDots(_operationProgress);
        };
    }

    private static void ApplyPixelFont(DependencyObject root, System.Windows.Media.FontFamily pixelFont, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root))
        {
            return;
        }

        switch (root)
        {
            case TextBlock textBlock when ShouldUsePixelFont(textBlock.FontFamily):
                textBlock.FontFamily = pixelFont;
                break;
            case System.Windows.Controls.Control control when ShouldUsePixelFont(control.FontFamily):
                control.FontFamily = pixelFont;
                break;
            case TextElement textElement when ShouldUsePixelFont(textElement.FontFamily):
                textElement.FontFamily = pixelFont;
                break;
        }

        foreach (var logicalChild in LogicalTreeHelper.GetChildren(root))
        {
            if (logicalChild is DependencyObject dependencyChild)
            {
                ApplyPixelFont(dependencyChild, pixelFont, visited);
            }
        }

        if (root is not Visual && root is not Visual3D)
        {
            return;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            ApplyPixelFont(VisualTreeHelper.GetChild(root, i), pixelFont, visited);
        }
    }

    private static bool ShouldUsePixelFont(System.Windows.Media.FontFamily fontFamily)
    {
        var source = fontFamily.Source;
        return !source.Contains("Sekuya", StringComparison.OrdinalIgnoreCase)
            && !source.Contains("Bad Script", StringComparison.OrdinalIgnoreCase)
            && !source.Contains("Segoe UI Symbol", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("dd MMM HH:mm:ss").ToLowerInvariant();
    }

    private void UpdateSessionTime()
    {
        if (_connectedAt is null || !_connected)
        {
            return;
        }

        var elapsed = DateTimeOffset.Now - _connectedAt.Value;
        MainLine2Text.Text = $"{(int)elapsed.TotalHours:00}-{elapsed.Minutes:00}-{elapsed.Seconds:00}";
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var showProgress = !_connected;
        if (showProgress)
        {
            BeginOperationProgress();
        }

        try
        {
            await ToggleConnectionAsync();
        }
        finally
        {
            if (showProgress)
            {
                await CompleteOperationProgressAsync();
            }
            else
            {
                _operationProgressTimer.Stop();
                MainProgressRing.Visibility = Visibility.Collapsed;
                UpdateMainProgressDots(0);
            }

            AnimateMainConnectButtonScale(MainConnectButton.IsMouseOver ? 1.025 : 1, 80);
        }
    }

    private Task ToggleConnectionAsync()
    {
        return _connected ? TurnOffAsync() : TurnOnAsync();
    }

    private void MainConnectButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        AnimateMainConnectButtonScale(1.025, 110);
    }

    private void MainConnectButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        AnimateMainConnectButtonScale(1, 110);
    }

    private void MainConnectButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        AnimateMainConnectButtonScale(0.985, 70);
    }

    private void MainConnectButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        AnimateMainConnectButtonScale(MainConnectButton.IsMouseOver ? 1.025 : 1, 80);
    }

    private void AnimateMainConnectButtonScale(double scale, int milliseconds)
    {
        var currentScaleTransform = MainConnectButton.RenderTransform as ScaleTransform;
        var scaleTransform = currentScaleTransform;
        if (scaleTransform is null || scaleTransform.IsFrozen)
        {
            scaleTransform = new ScaleTransform(
                currentScaleTransform?.ScaleX ?? 1,
                currentScaleTransform?.ScaleY ?? 1);
            MainConnectButton.RenderTransform = scaleTransform;
        }

        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var scaleXAnimation = new DoubleAnimation(scale, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        var scaleYAnimation = new DoubleAnimation(scale, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };

        scaleXAnimation.Completed += (_, _) => scaleTransform.ScaleX = scale;
        scaleYAnimation.Completed += (_, _) => scaleTransform.ScaleY = scale;
        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnimation);
    }

    private void BeginOperationProgress()
    {
        _operationProgressTimer.Stop();
        _operationProgressStartedAt = DateTimeOffset.Now;
        _operationProgress = 0.02;
        MainProgressRing.Visibility = Visibility.Visible;
        UpdateMainProgressDots(_operationProgress);
        _operationProgressTimer.Start();
    }

    private async Task CompleteOperationProgressAsync()
    {
        _operationProgressTimer.Stop();
        UpdateMainProgressDots(1);
        await Task.Delay(180);
        MainProgressRing.Visibility = Visibility.Collapsed;
        UpdateMainProgressDots(0);
    }

    private void UpdateMainProgressDots(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        const int dotCount = 40;
        const double center = 120;
        const double radius = 108;
        const double dotSize = 7;
        var activeDots = (int)Math.Ceiling(progress * dotCount);
        if (MainProgressDots.Children.Count != dotCount)
        {
            MainProgressDots.Children.Clear();
            for (var i = 0; i < dotCount; i++)
            {
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = dotSize,
                    Height = dotSize,
                    Fill = (System.Windows.Media.Brush)FindResource("GreenBrush"),
                    Opacity = 0.18
                };
                var point = PointOnCircle(center, center, radius, -90 + i * (360d / dotCount));
                Canvas.SetLeft(dot, point.X - dotSize / 2);
                Canvas.SetTop(dot, point.Y - dotSize / 2);
                MainProgressDots.Children.Add(dot);
            }
        }

        for (var i = 0; i < MainProgressDots.Children.Count; i++)
        {
            MainProgressDots.Children[i].Opacity = i < activeDots ? 1 : 0.18;
        }
    }

    private static System.Windows.Point PointOnCircle(double centerX, double centerY, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180;
        return new System.Windows.Point(
            centerX + radius * Math.Cos(radians),
            centerY + radius * Math.Sin(radians));
    }

    private Task TurnOnAsync()
    {
        return RunUiAsync(async () =>
        {
            if (_connected)
            {
                return;
            }

            _settings = await _controller.LoadSettingsAsync();
            var result = await _controller.ConnectAsync(_settings.ActiveProfileId);
            _connected = result.Success;
              if (result.Success)
              {
                  _connectedAt = DateTimeOffset.Now;
                  _sessionTimer.Start();
                  UpdateSnapshot(_controller.Snapshot);
                  RefreshProfilesSoon();
                  return;
              }
  
              UpdateSnapshot(_controller.Snapshot);
        });
    }

    private Task TurnOffAsync()
    {
        return RunUiAsync(async () =>
        {
              if (!_connected)
              {
                  return;
              }
  
              _connected = false;
              _connectedAt = null;
              _sessionTimer.Stop();
              UpdateSnapshot(new ConnectionSnapshot
              {
                  State = ConnectionStates.Disconnected,
                  RoutingMode = _settings.RoutingMode,
                  UpdatedAt = DateTimeOffset.UtcNow
              });

              await Task.Run(() => _controller.DisconnectAsync());
              UpdateSnapshot(_controller.Snapshot);
          });
      }

    private void RefreshProfilesSoon()
    {
        _ = Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                _settings = await _controller.LoadSettingsAsync();
                await RefreshProfilesAsync();
            }
            catch (Exception ex)
            {
                ShowSettingsMessage(ex.Message, System.Windows.Media.Brushes.Firebrick);
            }
        }));
    }

    private void EntrySkip_Click(object sender, RoutedEventArgs e)
    {
        ShowScreen(GuideScreen);
    }

    private void LetMeIn_Click(object sender, RoutedEventArgs e)
    {
        File.WriteAllText(GetOnboardingPath(), DateTimeOffset.UtcNow.ToString("O"));
        ShowScreen(MainScreen);
    }

    private void MainMenu_Click(object sender, RoutedEventArgs e)
    {
        ShowScreen(MainScreen);
    }

    private void Connections_Click(object sender, RoutedEventArgs e)
    {
        ConnectionContextPopup.IsOpen = false;
        ShowScreen(ConnectionsScreen);
    }

    private void AddConnection_Click(object sender, RoutedEventArgs e)
    {
        ShowScreen(AddConnectionScreen);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        UpdateSettingsView();
        ShowScreen(SettingsScreen);
    }

    private void RoutingSettings_Click(object sender, RoutedEventArgs e)
    {
        UpdateRoutingButtons();
        ShowScreen(RoutingSettingsScreen);
    }

    private void RuleSettings_Click(object sender, RoutedEventArgs e)
    {
        ShowScreen(RuleSettingsScreen);
    }

    private void RuleDetails_Click(object sender, RoutedEventArgs e)
    {
        ShowScreen(RuleDetailsScreen);
    }

    private async void ImportClipboard_Click(object sender, RoutedEventArgs e)
    {
        await ImportFromClipboardAsync();
    }

    private async void ImportSubscription_Click(object sender, RoutedEventArgs e)
    {
        await ImportFromClipboardAsync();
    }

    private async Task ImportFromClipboardAsync()
    {
        if (!System.Windows.Clipboard.ContainsText())
        {
            ShowSettingsMessage("clipboard is empty", System.Windows.Media.Brushes.Firebrick);
            ShowScreen(SettingsScreen);
            return;
        }

        await RunUiAsync(async () =>
        {
            var result = await _controller.ImportAsync(System.Windows.Clipboard.GetText());
            _settings = await _controller.LoadSettingsAsync();
            await RefreshProfilesAsync();
            ShowScreen(ConnectionsScreen);
            if (result.Success)
            {
                SettingsMessageText.Text = string.Empty;
            }
            else
            {
                ShowSettingsMessage(result.Message, System.Windows.Media.Brushes.Firebrick);
            }
        });
    }

    private async void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _contextProfile = ProfilesList.SelectedItem as ProfileRow;
        var selectedProfile = _contextProfile;
        if (_isRefreshingProfiles || selectedProfile is null)
        {
            return;
        }

        await RunUiAsync(async () =>
        {
            await ApplyActiveProfileAsync(selectedProfile, closeContextPopup: false);
        });
    }

    private void ProfilesList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindParent<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (item is null)
        {
            return;
        }

        item.IsSelected = true;
        _contextProfile = item.DataContext as ProfileRow;
        ConnectionContextPopup.IsOpen = true;
        e.Handled = true;
    }

    private async void SetActiveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_contextProfile is null)
        {
            return;
        }

        await RunUiAsync(async () =>
        {
            await ApplyActiveProfileAsync(_contextProfile, closeContextPopup: true);
        });
    }

    private async Task ApplyActiveProfileAsync(ProfileRow profile, bool closeContextPopup)
    {
        if (_connected)
        {
            await _controller.DisconnectAsync();
            _connected = false;
            _connectedAt = null;
            _sessionTimer.Stop();
            UpdateSnapshot(_controller.Snapshot);
        }

        await _controller.SetActiveProfileAsync(profile.Profile.Id);
        _settings = await _controller.LoadSettingsAsync();

        await RefreshProfilesAsync();
        if (closeContextPopup)
        {
            ConnectionContextPopup.IsOpen = false;
        }
    }

    private async void ClearActiveProfile_Click(object sender, RoutedEventArgs e)
    {
        await RunUiAsync(async () =>
        {
            await _controller.SetActiveProfileAsync(null);
            _settings = await _controller.LoadSettingsAsync();
            await RefreshProfilesAsync();
            ConnectionContextPopup.IsOpen = false;
        });
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_contextProfile is null)
        {
            return;
        }

        await RunUiAsync(async () =>
        {
            await _controller.DeleteProfileAsync(_contextProfile.Profile.Id);
            _settings = await _controller.LoadSettingsAsync();
            await RefreshProfilesAsync();
            ConnectionContextPopup.IsOpen = false;
        });
    }

    private async void RussiaMode_Click(object sender, RoutedEventArgs e)
    {
        await SetRoutingModeAsync(RoutingModes.RussiaSmart);
    }

    private async void GlobalMode_Click(object sender, RoutedEventArgs e)
    {
        await SetRoutingModeAsync(RoutingModes.GlobalProxy);
    }

    private async void WhitelistMode_Click(object sender, RoutedEventArgs e)
    {
        await SetRoutingModeAsync(RoutingModes.Whitelist);
    }

    private async void BlacklistMode_Click(object sender, RoutedEventArgs e)
    {
        await SetRoutingModeAsync(RoutingModes.Blacklist);
    }

    private async Task SetRoutingModeAsync(string routingMode)
    {
        await RunUiAsync(async () =>
        {
            _settings = _settings with { RoutingMode = routingMode };
            await _controller.SaveSettingsAsync(_settings);
            SettingsMessageText.Text = string.Empty;
            UpdateRoutingButtons();
        });
    }

    private async void LogsToggle_Click(object sender, RoutedEventArgs e)
    {
        await RunUiAsync(async () =>
        {
            _settings = _settings with { LogsConsent = !_settings.LogsConsent };
            await _controller.SaveSettingsAsync(_settings);
            UpdateSettingsView();
            SettingsMessageText.Text = string.Empty;
        });
    }

    private async void AutoUpdateToggle_Click(object sender, RoutedEventArgs e)
    {
        await RunUiAsync(async () =>
        {
            _settings = _settings with { AutoUpdateRules = !_settings.AutoUpdateRules };
            await _controller.SaveSettingsAsync(_settings);
            UpdateSettingsView();
            SettingsMessageText.Text = string.Empty;
        });
    }

    private void ShowFirstLaunchGuide_Click(object sender, RoutedEventArgs e)
    {
        SettingsMessageText.Text = string.Empty;
        ShowScreen(EntryScreen);
    }

    private void DeleteApp_Click(object sender, RoutedEventArgs e)
    {
        DeleteAppConfirmOverlay.Visibility = Visibility.Visible;
    }

    private void CancelDeleteApp_Click(object sender, RoutedEventArgs e)
    {
        DeleteAppConfirmOverlay.Visibility = Visibility.Collapsed;
    }

    private async void ConfirmDeleteApp_Click(object sender, RoutedEventArgs e)
    {
        DeleteAppConfirmOverlay.Visibility = Visibility.Collapsed;
        await RunUiAsync(async () =>
        {
            ShowScreen(SettingsScreen);
            ShowSettingsMessage("stopping Loki before uninstall...", System.Windows.Media.Brushes.SeaGreen);
            await DisconnectForShutdownAsync();
            await Task.Run(() => _controller.ShutdownAsync());

            var uninstallerPath = FindInstalledUninstaller();
            if (uninstallerPath is null)
            {
                ShowSettingsMessage("uninstaller not found in app folder", System.Windows.Media.Brushes.Firebrick);
                return;
            }

            StartDelayedUninstaller(uninstallerPath);
            _skipShutdownOnClose = true;
            _exitRequested = true;
            Close();
        });
    }

    private void UpdateSettingsView()
    {
        LogsToggleText.Text = _settings.LogsConsent
            ? "2. Logs collection enabled"
            : "2. Logs collection disabled";
        AutoUpdateToggleText.Text = _settings.AutoUpdateRules
            ? "3. Auto updates enabled"
            : "3. Auto updates disabled";
    }

    private async Task RefreshProfilesAsync()
    {
        _isRefreshingProfiles = true;
        try
        {
            _profileRows.Clear();
            var profiles = await _controller.ListProfilesAsync();
            var index = 1;
            foreach (var profile in profiles)
            {
                _profileRows.Add(new ProfileRow(index++, profile, profile.Id == _settings.ActiveProfileId));
            }

            var activeIndex = _profileRows
                .Select((row, rowIndex) => new { row, rowIndex })
                .FirstOrDefault(item => item.row.IsActive)?.rowIndex ?? -1;
            if (activeIndex >= 0)
            {
                ProfilesList.SelectedIndex = activeIndex;
            }
            else if (_profileRows.Count > 0 && ProfilesList.SelectedIndex < 0)
            {
                ProfilesList.SelectedIndex = 0;
            }
        }
        finally
        {
            _isRefreshingProfiles = false;
        }
    }

    private void UpdateSnapshot(ConnectionSnapshot snapshot)
    {
        switch (snapshot.State)
        {
            case ConnectionStates.Connected:
                _errorResetTimer.Stop();
                MainStateText.Text = "ACTIVE";
                MainStateText.Foreground = (System.Windows.Media.Brush)FindResource("GreenBrush");
                MainLine1Text.Text = ".connection time";
                UpdateSessionTime();
                break;
            case ConnectionStates.Error:
                _errorResetTimer.Stop();
                _errorResetTimer.Start();
                MainStateText.Text = "ERROR";
                MainStateText.Foreground = (System.Windows.Media.Brush)FindResource("RedBrush");
                MainLine1Text.Text = "-error type";
                MainLine2Text.Text = ToShortError(snapshot.LastError);
                break;
            case ConnectionStates.Connecting:
                _errorResetTimer.Stop();
                MainStateText.Text = "WAIT";
                MainStateText.Foreground = (System.Windows.Media.Brush)FindResource("GreenBrush");
                MainLine1Text.Text = "-connecting";
                MainLine2Text.Text = "please wait";
                break;
            default:
                _errorResetTimer.Stop();
                MainStateText.Text = "OFF";
                MainStateText.Foreground = System.Windows.Media.Brushes.White;
                MainLine1Text.Text = "Hey!";
                MainLine2Text.Text = "Push on me!";
                break;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Text = $"Loki Proxy: {MainStateText.Text}";
        }

        UpdateTrayMenuState();
    }

    private void UpdateRoutingButtons()
    {
        RussiaModeButton.Foreground = _settings.RoutingMode == RoutingModes.RussiaSmart ? System.Windows.Media.Brushes.SeaGreen : System.Windows.Media.Brushes.White;
        GlobalModeButton.Foreground = _settings.RoutingMode == RoutingModes.GlobalProxy ? System.Windows.Media.Brushes.SeaGreen : System.Windows.Media.Brushes.White;
        WhitelistModeButton.Foreground = _settings.RoutingMode == RoutingModes.Whitelist ? System.Windows.Media.Brushes.SeaGreen : System.Windows.Media.Brushes.White;
        BlacklistModeButton.Foreground = _settings.RoutingMode == RoutingModes.Blacklist ? System.Windows.Media.Brushes.SeaGreen : System.Windows.Media.Brushes.White;
    }

    private void ShowSettingsMessage(string message, System.Windows.Media.Brush brush)
    {
        SettingsMessageText.Foreground = brush;
        SettingsMessageText.Text = message;
    }

    private async Task DisconnectForShutdownAsync()
    {
        _connected = false;
        _connectedAt = null;
        _sessionTimer.Stop();
        UpdateSnapshot(new ConnectionSnapshot
        {
            State = ConnectionStates.Disconnected,
            RoutingMode = _settings.RoutingMode,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await Task.Run(() => _controller.DisconnectAsync());
        UpdateSnapshot(_controller.Snapshot);
    }

    private static string? FindInstalledUninstaller()
    {
        return Directory.Exists(AppContext.BaseDirectory)
            ? Directory.EnumerateFiles(AppContext.BaseDirectory, "unins*.exe", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;
    }

    private static void StartDelayedUninstaller(string uninstallerPath)
    {
        var workingDirectory = Path.GetDirectoryName(uninstallerPath) ?? AppContext.BaseDirectory;
        var command = $"/c timeout /t 2 /nobreak >nul & \"{uninstallerPath}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = command,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private async Task RunUiAsync(Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        try
        {
            _busy = true;
            await action();
        }
        catch (Exception ex)
        {
            ShowSettingsMessage(ex.Message, System.Windows.Media.Brushes.Firebrick);
            UpdateSnapshot(_controller.Snapshot with { State = ConnectionStates.Error, LastError = ex.Message });
        }
        finally
        {
            _busy = false;
            UpdateTrayMenuState();
        }
    }

    private void ShowScreen(UIElement screen)
    {
        EntryScreen.Visibility = Visibility.Collapsed;
        GuideScreen.Visibility = Visibility.Collapsed;
        MainScreen.Visibility = Visibility.Collapsed;
        ConnectionsScreen.Visibility = Visibility.Collapsed;
        AddConnectionScreen.Visibility = Visibility.Collapsed;
        SettingsScreen.Visibility = Visibility.Collapsed;
        RoutingSettingsScreen.Visibility = Visibility.Collapsed;
        RuleSettingsScreen.Visibility = Visibility.Collapsed;
        RuleDetailsScreen.Visibility = Visibility.Collapsed;
        if (ReferenceEquals(screen, SettingsScreen))
        {
            UpdateSettingsView();
        }

        screen.Visibility = Visibility.Visible;
        UpdateTopBar(screen);
    }

    private void UpdateTopBar(UIElement screen)
    {
        var isLightScreen = ReferenceEquals(screen, AddConnectionScreen);
        TopBar.Background = isLightScreen ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black;
        var buttonBrush = isLightScreen ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
        TopMinimizeText.Foreground = buttonBrush;
        TopCloseText.Foreground = buttonBrush;
    }

    private bool HasCompletedOnboarding()
    {
        return File.Exists(GetOnboardingPath());
    }

    private string GetOnboardingPath()
    {
        return System.IO.Path.Combine(_controller.Paths.DataDirectory, OnboardingFileName);
    }

    private static string ToShortError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "host / system / subs error";
        }

        var lower = error.ToLowerInvariant();
        if (lower.Contains("no connections") || lower.Contains("no profiles") || lower.Contains("нет проф"))
        {
            return "no connection";
        }
        if (lower.Contains("subscription") || lower.Contains("подпис") || lower.Contains("url"))
        {
            return "subscription problem";
        }

        if (lower.Contains("server") || lower.Contains("http") || lower.Contains("xray"))
        {
            return "server problem";
        }

        return "host problem";
    }

    private static T? FindParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void InitializeTray()
    {
        _trayIconImage = LoadTrayIcon();
        _trayTurnOnItem = new Forms.ToolStripMenuItem("Turn ON");
        _trayTurnOffItem = new Forms.ToolStripMenuItem("Turn OFF");

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayIconImage,
            Text = "Loki Proxy",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };

        _trayTurnOnItem.Click += (_, _) => QueueUiTask(TurnOnAsync);
        _trayTurnOffItem.Click += (_, _) => QueueUiTask(TurnOffAsync);
        _trayIcon.ContextMenuStrip.Items.AddRange(new Forms.ToolStripItem[]
        {
            _trayTurnOnItem,
            _trayTurnOffItem,
            new Forms.ToolStripSeparator(),
            new Forms.ToolStripMenuItem("Open", null, (_, _) => QueueUiAction(OpenFromTray)),
            new Forms.ToolStripMenuItem("Exit", null, (_, _) => QueueUiAction(ExitFromTray))
        });
        _trayIcon.DoubleClick += (_, _) => QueueUiAction(OpenFromTray);
        UpdateTrayMenuState();
    }

    private void OpenFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ResetWindowVisualState();
        Activate();
        _ = PlayOpenAnimationAsync();
    }

    public void ActivateFromExternalLaunch()
    {
        OpenFromTray();
    }

    private void ExitFromTray()
    {
        _exitRequested = true;
        Close();
    }

    private void QueueUiAction(Action action)
    {
        _ = Dispatcher.BeginInvoke(action);
    }

    private void QueueUiTask(Func<Task> action)
    {
        _ = Dispatcher.BeginInvoke(new Action(async () => await action()));
    }

    private void UpdateTrayMenuState()
    {
        if (_trayTurnOnItem is null || _trayTurnOffItem is null)
        {
            return;
        }

        _trayTurnOnItem.Enabled = !_busy && !_connected;
        _trayTurnOffItem.Enabled = !_busy && _connected;
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "app.ico");
        if (File.Exists(iconPath))
        {
            return new System.Drawing.Icon(iconPath);
        }

        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    private sealed record ProfileRow(int Index, ProxyProfile Profile, bool IsActive)
    {
        public string Label => $"{Index}. {Profile.Name}";
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
