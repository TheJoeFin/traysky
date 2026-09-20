using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Controls;
using Traysky.Models;
using Traysky.Services;
using Windows.UI.ViewManagement;
using Windows.Win32;
using WinUIEx;

namespace Traysky;

/// <summary>
/// Application root: owns the tray icon, the flyout window, and the wiring between the
/// session, the unread poller and what the tray shows. Structure follows Traydio's App.
/// </summary>
public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private TrayClickSequencer? _leftClickSequencer;
    private TrayClickSequencer? _rightClickSequencer;
    private TrayPopupWindow? _trayPopupWindow;
    private NotificationAnnouncer? _announcer;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _trayIconRestoreEvent;
    private DispatcherQueue? _uiDispatcherQueue;
    private DispatcherQueueTimer? _trayWatchdogTimer;
    private readonly UISettings _uiSettings = new();
    private string _currentIconPath = string.Empty;
    private bool _isShuttingDown;
    private bool _servicesStarted;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>The running instance, for code that needs to show the flyout (toasts, settings).</summary>
    public static new App Current => (App)Application.Current;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        const string mutexName = "Global\\Traysky_SingleInstance_Mutex";
        const string restoreEventName = "Global\\Traysky_RestoreTrayIcon_Event";

        try
        {
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                // Another instance is running: ask it to (re)show its tray icon and bow out.
                try
                {
                    using EventWaitHandle restoreEvent = EventWaitHandle.OpenExisting(restoreEventName);
                    restoreEvent.Set();
                }
                catch
                {
                    // Event missing: the other instance's watchdog will restore the icon anyway.
                }

                ShutdownAndExit();
                return;
            }
        }
        catch (Exception)
        {
            // Mutex creation can fail in restricted environments; carry on single-instance-less.
        }

        try
        {
            _trayIconRestoreEvent = new EventWaitHandle(false, EventResetMode.AutoReset, restoreEventName);
            ThreadPool.RegisterWaitForSingleObject(
                _trayIconRestoreEvent,
                (_, _) => _uiDispatcherQueue?.TryEnqueue(() => _ = EnsureTrayIconVisibleAsync(showFlyout: true)),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch
        {
        }

        _uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        LogService.Info("App", "Launched");

        // Services that must be born on the UI thread (they capture the dispatcher).
        BlueskySessionService session = BlueskySessionService.Instance;
        NotificationPollService poll = NotificationPollService.Instance;

        _servicesStarted = true;
        session.PropertyChanged += OnSessionPropertyChanged;
        poll.PropertyChanged += OnPollPropertyChanged;
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;

        InitializeTrayIcon();
        UpdateTrayIcon();
        StartTrayWatchdog();

        ToastService.Register(destination => ShowFlyout(destination));
        _announcer = new NotificationAnnouncer(() => _trayPopupWindow?.IsPopupVisible == true);

        // A toast can launch the app; land on what it pointed at once the session is back.
        ShellDestination? launchDestination = null;
        try
        {
            AppActivationArguments activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activation.Kind == ExtendedActivationKind.AppNotification)
                launchDestination = ToastService.DestinationFromLaunch(activation.Data as AppNotificationActivatedEventArgs);
        }
        catch (Exception ex)
        {
            LogService.Warn("App", $"Activation args unavailable: {ex.Message}");
        }

        bool restored = await session.TryRestoreAsync();
        LogService.Info("App", restored ? $"Session restored for @{session.Handle}" : "No saved session");

        if (launchDestination is ShellDestination destination)
            ShowFlyout(destination);
        else if (!restored)
            ShowFlyout(ShellDestination.Current); // first run / signed out: show the login page
    }

    // ---- Tray icon -------------------------------------------------------------------------

    private void InitializeTrayIcon()
    {
        if (_trayIcon is not null)
            return;

        _trayIcon = new TrayIcon(0, "Assets/Wings-MonoBlack.ico", "Traysky");
        _trayIcon.Selected += TrayIcon_Selected;
        _trayIcon.ContextMenu += TrayIcon_ContextMenu;
        _trayIcon.LeftDoubleClick += TrayIcon_LeftDoubleClick;
        _trayIcon.RightDoubleClick += TrayIcon_RightDoubleClick;
        _trayIcon.IsVisible = true;
        _currentIconPath = "Assets/Wings-MonoBlack.ico";

        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
        _leftClickSequencer ??= new TrayClickSequencer(dispatcher);
        _rightClickSequencer ??= new TrayClickSequencer(dispatcher);
        WindowPlacementService.SetTrayIconSource(_trayIcon);
    }

    private void TrayIcon_Selected(TrayIcon sender, TrayIconEventArgs args)
    {
        DateTime clickedAtUtc = DateTime.UtcNow;
        _leftClickSequencer?.OnClick(
            defer: TrayClickPolicy.ShouldDeferSingleClick(SettingsService.TrayLeftDoubleClickAction),
            () => RunTrayAction(SettingsService.TrayLeftClickAction, clickedAtUtc));
    }

    private void TrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        DateTime clickedAtUtc = DateTime.UtcNow;
        _rightClickSequencer?.OnClick(
            defer: TrayClickPolicy.ShouldDeferSingleClick(SettingsService.TrayRightDoubleClickAction),
            () => RunTrayAction(SettingsService.TrayRightClickAction, clickedAtUtc));
    }

    private void TrayIcon_LeftDoubleClick(TrayIcon sender, TrayIconEventArgs args)
    {
        TrayClickAction action = SettingsService.TrayLeftDoubleClickAction;
        if (action == TrayClickAction.None)
            return;
        _leftClickSequencer?.OnDoubleClick(() => RunTrayAction(action, DateTime.UtcNow));
    }

    private void TrayIcon_RightDoubleClick(TrayIcon sender, TrayIconEventArgs args)
    {
        TrayClickAction action = SettingsService.TrayRightDoubleClickAction;
        if (action == TrayClickAction.None)
            return;
        _rightClickSequencer?.OnDoubleClick(() => RunTrayAction(action, DateTime.UtcNow));
    }

    private void RunTrayAction(TrayClickAction action, DateTime clickedAtUtc)
    {
        if (action == TrayClickAction.None)
            return;

        // Touch and pen taps on the icon never move the hardware cursor; placement must come
        // from the icon's own rect (see WindowPlacementService.ClearPointerAnchor).
        WindowPlacementService.ClearPointerAnchor();

        ShellDestination destination = TrayClickPolicy.Resolve(action, BlueskySessionService.Instance.IsSignedIn);

        EnsurePopupWindow();

        if (destination == ShellDestination.Current)
        {
            _trayPopupWindow!.ToggleNearAnchor(clickedAtUtc);
            return;
        }

        _trayPopupWindow!.ShowAt(destination, clickedAtUtc);
    }

    /// <summary>Shows the flyout (never toggles) at a destination. Used by toasts and settings.</summary>
    public void ShowFlyout(ShellDestination destination)
    {
        WindowPlacementService.ClearPointerAnchor();
        EnsurePopupWindow();
        _trayPopupWindow!.ShowAt(destination);
    }

    public void HideFlyout() => _trayPopupWindow?.HidePopup();

    private void EnsurePopupWindow()
    {
        if (_trayPopupWindow is not null)
            return;

        _trayPopupWindow = new TrayPopupWindow();
        _trayPopupWindow.Closed += (_, _) => _trayPopupWindow = null;
    }

    /// <summary>
    /// Turns the raw single- and double-click events for one mouse button into at most one
    /// action per gesture. Ported from Traydio; see its remarks for the click/double/click
    /// sequence Windows delivers.
    /// </summary>
    private sealed class TrayClickSequencer
    {
        private readonly DispatcherQueueTimer _timer;
        private Action? _pendingSingleClick;
        private DateTimeOffset _lastDoubleClickAtUtc = DateTimeOffset.MinValue;

        public TrayClickSequencer(DispatcherQueue dispatcher)
        {
            _timer = dispatcher.CreateTimer();
            _timer.IsRepeating = false;
            _timer.Tick += (_, _) =>
            {
                Action? pending = _pendingSingleClick;
                _pendingSingleClick = null;
                pending?.Invoke();
            };
        }

        private static TimeSpan DoubleClickInterval =>
            TimeSpan.FromMilliseconds(PInvoke.GetDoubleClickTime());

        public void OnClick(bool defer, Action singleClick)
        {
            if (DateTimeOffset.UtcNow - _lastDoubleClickAtUtc < DoubleClickInterval)
                return;

            if (!defer)
            {
                singleClick();
                return;
            }

            _pendingSingleClick = singleClick;
            _timer.Interval = DoubleClickInterval;
            _timer.Stop();
            _timer.Start();
        }

        public void OnDoubleClick(Action doubleClick)
        {
            _timer.Stop();
            _pendingSingleClick = null;
            _lastDoubleClickAtUtc = DateTimeOffset.UtcNow;
            doubleClick();
        }
    }

    // ---- Icon state ------------------------------------------------------------------------

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BlueskySessionService.IsSignedIn) or nameof(BlueskySessionService.Handle))
            UpdateTrayIcon();
    }

    private void OnPollPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotificationPollService.UnreadCount) or nameof(NotificationPollService.IsOffline))
            UpdateTrayIcon();
    }

    private void OnColorValuesChanged(UISettings sender, object args) => UpdateTrayIcon();

    private void UpdateTrayIcon()
    {
        if (_uiDispatcherQueue is not null && !_uiDispatcherQueue.HasThreadAccess)
        {
            _uiDispatcherQueue.TryEnqueue(UpdateTrayIcon);
            return;
        }

        if (_trayIcon is null)
            return;

        BlueskySessionService session = BlueskySessionService.Instance;
        NotificationPollService poll = NotificationPollService.Instance;

        bool isDarkTaskbar = IsSystemInDarkMode();
        TrayIconVariant variant = UnreadBadgePolicy.Choose(session.IsSignedIn, poll.UnreadCount, poll.IsOffline, isDarkTaskbar);
        string iconPath = UnreadBadgePolicy.AssetPath(variant, isDarkTaskbar);
        string tooltip = UnreadBadgePolicy.Tooltip(session.IsSignedIn, session.Handle, poll.UnreadCount, poll.IsOffline);

        try
        {
            if (!string.Equals(iconPath, _currentIconPath, StringComparison.Ordinal))
            {
                _trayIcon.SetIcon(iconPath);
                _currentIconPath = iconPath;
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("Tray", $"SetIcon('{iconPath}') failed: {ex.Message}");
            try { _trayIcon.SetIcon("Assets/Wings-MonoBlack.ico"); } catch { }
        }

        // SetIcon can clear the native tooltip even when the text is unchanged, so always re-apply.
        _trayIcon.Tooltip = tooltip;
    }

    internal static bool IsSystemInDarkMode()
    {
        try
        {
            // The taskbar follows the *system* theme, not the app theme.
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("SystemUsesLightTheme");
            if (value is int intVal)
                return intVal == 0;
            return true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Apps (and our titlebar) follow a separate light/dark switch from the taskbar - a user can
    /// run light apps on a dark taskbar or vice versa, so this reads "AppsUseLightTheme" rather
    /// than <see cref="IsSystemInDarkMode"/>'s "SystemUsesLightTheme".
    /// </summary>
    internal static bool IsAppInDarkMode()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("AppsUseLightTheme");
            if (value is int intVal)
                return intVal == 0;
            return true;
        }
        catch
        {
            return true;
        }
    }

    // ---- Tray icon watchdog ----------------------------------------------------------------

    /// <summary>
    /// Explorer restarts drop every app's tray icon. Re-asserting visibility periodically is
    /// cheap and the only reliable fix (Traydio does the same).
    /// </summary>
    private void StartTrayWatchdog()
    {
        _trayWatchdogTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _trayWatchdogTimer.Interval = TimeSpan.FromMinutes(2);
        _trayWatchdogTimer.IsRepeating = true;
        _trayWatchdogTimer.Tick += (_, _) => _ = EnsureTrayIconVisibleAsync(showFlyout: false);
        _trayWatchdogTimer.Start();
    }

    private Task EnsureTrayIconVisibleAsync(bool showFlyout)
    {
        try
        {
            if (_trayIcon is null)
            {
                InitializeTrayIcon();
            }
            else
            {
                _trayIcon.IsVisible = false;
                _trayIcon.IsVisible = true;
            }

            _currentIconPath = string.Empty; // force SetIcon after the re-add
            UpdateTrayIcon();

            if (showFlyout)
                ShowFlyout(ShellDestination.Current);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Failed to restore tray icon: {ex}");
        }

        return Task.CompletedTask;
    }

    // ---- Lifecycle -------------------------------------------------------------------------

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogService.Error("App", "Unhandled exception", e.Exception);
    }

    /// <summary>
    /// The app's only real exit path (the Quit button and the duplicate-instance early-out both
    /// route here). Tears down the tray icon and native resources, then exits.
    /// </summary>
    internal void ShutdownAndExit()
    {
        if (_isShuttingDown)
            return;
        _isShuttingDown = true;

        LogService.Info("App", "Shutting down");

        try
        {
            _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
            _trayWatchdogTimer?.Stop();
            if (_servicesStarted)
            {
                NotificationPollService.Instance.Stop();
                ToastService.Unregister();
            }

            _trayPopupWindow?.Close();

            if (_trayIcon is not null)
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            if (_servicesStarted)
                BlueskySessionService.Instance.Agent.Dispose();

            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            _trayIconRestoreEvent?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Error during shutdown: {ex.Message}");
        }

        Exit();
    }
}
