using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using Traysky;
using Traysky.Models;
using Traysky.Pages;
using Traysky.Services;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;
using WinUIEx;

namespace Traysky.Controls;

/// <summary>
/// Borderless popup window shown near the tray icon / click position,
/// replacing the WinUIEx tray flyout. Light-dismisses when focus leaves.
/// </summary>
public sealed partial class TrayPopupWindow : WindowEx
{
    /// <summary>How long after the last resize/move event before the window settles: saves its geometry and, if a
    /// breakpoint was crossed, swaps between flyout and standard window.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(250);

    private const int VK_LBUTTON = 0x01;

    /// <summary>The standard window's size, in DIPs, the first time the shell becomes one without a saved position.</summary>
    private const int DefaultWindowWidth = 1000;
    private const int DefaultWindowHeight = 720;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA = 0x00000002;
    private const uint GA_ROOT = 2;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    // Same slide + fade entrance/exit feel as SongChangePopupWindow, driven by a
    // frame timer over window position and layered alpha rather than a XAML
    // storyboard: MicaBackdrop paints behind the XAML content and is unaffected
    // by UIElement.Opacity, so fading the surface requires per-window alpha.
    private const int ShowSlideDistance = 24;
    private const int HideSlideDistance = 12;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(15);
    private const double ShowDurationMs = 250;
    private const double HideDurationMs = 180;

    /// <summary>
    /// A tray click that arrives right after a light-dismiss hide is the same
    /// click that dismissed the popup — treat it as "close" instead of re-showing.
    /// </summary>
    private static readonly TimeSpan RecentDismissWindow = TimeSpan.FromMilliseconds(300);

    private readonly DispatcherQueueTimer _hideTimer;
    private readonly DispatcherQueueTimer _animationTimer;
    private readonly DispatcherQueueTimer _trimTimer;
    private readonly DispatcherQueueTimer _settleTimer;
    private readonly Stopwatch _animationClock = new();
    private readonly HWND _hwnd;
    private readonly UISettings _uiSettings = new();
    private ShellPage? _shellPage;
    private bool _isShowing;
    private bool _isPopupVisible;

    /// <summary>
    /// True while the shell is a standard window (title bar, taskbar/Alt-Tab entry, no light-dismiss,
    /// no slide animation) instead of the tray flyout. Entered by widening past the first breakpoint.
    /// </summary>
    private bool _isWindowMode;

    /// <summary>Only the app's exit path really closes the window; the title bar's close button hides it to the tray.</summary>
    private bool _allowClose;
    private DateTime _lastDismissedAtUtc = DateTime.MinValue;

    // Animation state, mirroring SongChangePopupWindow: _baseY is the popup's
    // resting Y; the slide animates an offset below it, in physical pixels
    // scaled to the target monitor.
    private bool _isHiding;
    private int _baseX;
    private int _baseY;
    private int _fromOffset;
    private int _toOffset;
    private double _fromOpacity;
    private double _toOpacity;
    private double _durationMs = ShowDurationMs;

    // Where the last rendered frame left the popup, so an animation that
    // interrupts another can start from the current state instead of snapping.
    private int _currentOffset;
    private double _currentOpacity;

    public TrayPopupWindow()
    {
        InitializeComponent();

        _hwnd = (HWND)WindowNative.GetWindowHandle(this);

        UpdateWindowIcon();
        ConfigurePresenter();
        // ConfigureToolWindow();

        Activated += OnWindowActivated;
        Closed += (_, _) => _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(150);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => HideIfFocusLeftWindow();

        _animationTimer = DispatcherQueue.CreateTimer();
        _animationTimer.Interval = FrameInterval;
        _animationTimer.IsRepeating = true;
        _animationTimer.Tick += AnimationTimer_Tick;

        // Armed now as well as on every hide: the app starts in the tray, and startup leaves
        // plenty behind (JIT, first-request HTTP/JSON setup) that the idle app won't touch.
        _trimTimer = DispatcherQueue.CreateTimer();
        _trimTimer.Interval = MemoryTrimService.HiddenDelay;
        _trimTimer.IsRepeating = false;
        _trimTimer.Tick += (_, _) =>
        {
            if (!_isPopupVisible)
                MemoryTrimService.Trim();
        };
        _trimTimer.Start();

        // Debounced: a drag raises many events. Once it settles, remember the geometry and decide
        // whether the width now calls for a different kind of window.
        _settleTimer = DispatcherQueue.CreateTimer();
        _settleTimer.Interval = SettleDelay;
        _settleTimer.IsRepeating = false;
        _settleTimer.Tick += (_, _) => OnGeometrySettled();
        AppWindow.Changed += (_, args) =>
        {
            // Position changes only matter for the standard window; the flyout's own slide animation moves it constantly.
            if (_isPopupVisible && (args.DidSizeChange || (_isWindowMode && args.DidPositionChange)))
            {
                _settleTimer.Stop();
                _settleTimer.Start();
            }
        };

        // The title bar's close button (and Alt+F4) send the window back to the tray rather than ending the app.
        AppWindow.Closing += (_, args) =>
        {
            if (_allowClose)
                return;

            args.Cancel = true;
            HidePopup();
        };
    }

    /// <summary>Closes for real. Used only when the app exits.</summary>
    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    private double ClientWidthInDips()
    {
        uint dpi = PInvoke.GetDpiForWindow(_hwnd);
        if (dpi == 0)
            dpi = 96;
        return AppWindow.ClientSize.Width * 96.0 / dpi;
    }

    private OverlappedPresenter Presenter => (OverlappedPresenter)AppWindow.Presenter;

    private void OnGeometrySettled()
    {
        if (!_isPopupVisible || Presenter.State == OverlappedPresenterState.Minimized)
            return;

        // Swapping the window frame mid-drag would pull the rug out from under the user's mouse.
        if (PInvoke.GetAsyncKeyState(VK_LBUTTON) < 0)
        {
            _settleTimer.Start();
            return;
        }

        bool shouldBeWindow = ShellLayoutPolicy.ShouldBeWindow(ClientWidthInDips(), _isWindowMode);
        if (shouldBeWindow && !_isWindowMode)
        {
            BecomeWindow();
            return;
        }

        if (!shouldBeWindow && _isWindowMode)
        {
            BecomeFlyout(fromResize: true);
            return;
        }

        SaveGeometry();
    }

    /// <summary>Remembers the window's bounds (standard window) or the flyout's size in DIPs (flyout).</summary>
    private void SaveGeometry()
    {
        if (_isWindowMode)
        {
            // A maximized or minimized window's bounds are not the size the user chose.
            if (Presenter.State == OverlappedPresenterState.Restored)
                SettingsService.WindowBounds = new PixelRect(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
            return;
        }

        uint dpi = PInvoke.GetDpiForWindow(_hwnd);
        if (dpi == 0)
            dpi = 96;

        SettingsService.PopupSize = (
            (int)Math.Round(AppWindow.Size.Width * 96.0 / dpi),
            (int)Math.Round(AppWindow.Size.Height * 96.0 / dpi));
    }

    /// <summary>Flyout to standard window, in place: the window keeps the size and position the user dragged it to.</summary>
    private void BecomeWindow()
    {
        _animationTimer.Stop();
        _animationClock.Reset();
        _isHiding = false;
        ApplyMode(window: true);
        SettingsService.PopupIsWindow = true;
        SaveGeometry();
        Activate();
    }

    /// <summary>
    /// The flyout's title bar was double-tapped: become the standard window at its remembered
    /// bounds (or the default size, centered), the same form that widening past the breakpoint gives.
    /// </summary>
    private void ExpandToWindow()
    {
        _settleTimer.Stop();
        _animationTimer.Stop();
        _animationClock.Reset();
        _isHiding = false;

        SaveGeometry(); // the flyout's size, while it is still one
        SettingsService.PopupIsWindow = true;

        // Resize first, then swap the frame: the title bar reads the caption buttons' inset when the
        // mode changes, and it has to see the final width or the header buttons end up beneath them.
        PlaceWindow();
        ApplyMode(window: true);
        BringToFront();

        // Once the new frame has been laid out, hand the bar over again so it re-reads the inset.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isWindowMode && _shellPage is not null)
                SetTitleBar(_shellPage.TitleBarElement);
        });
    }

    /// <summary>
    /// Standard window back to the tray flyout, next to the tray icon. <paramref name="fromResize"/>:
    /// the user narrowed the window, so the size they chose becomes the flyout's; otherwise (the
    /// title bar button) the flyout keeps its remembered compact size.
    /// </summary>
    private void BecomeFlyout(bool fromResize)
    {
        // A maximized window has to be restored before it can be placed as a flyout.
        if (Presenter.State != OverlappedPresenterState.Restored)
            Presenter.Restore();

        SaveGeometry(); // the window's bounds, while it is still one
        ApplyMode(window: false);
        SettingsService.PopupIsWindow = false;

        if (fromResize)
        {
            uint dpi = PInvoke.GetDpiForWindow(_hwnd);
            if (dpi == 0)
                dpi = 96;
            SettingsService.PopupSize = (
                (int)Math.Round(AppWindow.Size.Width * 96.0 / dpi),
                (int)Math.Round(AppWindow.Size.Height * 96.0 / dpi));
        }

        WindowPlacementService.ClearPointerAnchor();
        _isShowing = true;
        PlaceFlyout();
        Activate();
        PInvoke.SetForegroundWindow(_hwnd);
    }

    /// <summary>
    /// Switches between the borderless tray flyout and a standard window: a real title bar (the
    /// shell's WinUI <c>TitleBar</c> becomes it), minimize/maximize, a taskbar and Alt-Tab entry,
    /// not always-on-top, and none of the flyout's per-window alpha fade.
    /// </summary>
    private void ApplyMode(bool window)
    {
        _isWindowMode = window;
        _shellPage?.SetWindowMode(window);
        OverlappedPresenter presenter = Presenter;

        if (window)
        {
            presenter.IsAlwaysOnTop = false;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            presenter.SetBorderAndTitleBar(true, true);
            AppWindow.IsShownInSwitchers = true;

            ExtendsContentIntoTitleBar = true;
            if (_shellPage is not null)
                SetTitleBar(_shellPage.TitleBarElement);

            SetLayered(false);
        }
        else
        {
            ExtendsContentIntoTitleBar = false;
            SetTitleBar(null);

            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(true, false);
            AppWindow.IsShownInSwitchers = false;

            SetLayered(true);
        }
    }

    /// <summary>WS_EX_LAYERED enables the per-window alpha the flyout's fade drives; a standard window is opaque and does without.</summary>
    private void SetLayered(bool layered)
    {
        int exStyle = PInvoke.GetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        int updated = layered ? exStyle | WS_EX_LAYERED : exStyle & ~WS_EX_LAYERED;
        if (updated != exStyle)
            _ = PInvoke.SetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, updated);
    }

    /// <summary>Brings an already-visible standard window to the front, restoring it if minimized.</summary>
    private void BringToFront()
    {
        if (Presenter.State == OverlappedPresenterState.Minimized)
            Presenter.Restore();

        Activate();
        PInvoke.SetForegroundWindow(_hwnd);
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        if (DispatcherQueue.HasThreadAccess)
            UpdateWindowIcon();
        else
            DispatcherQueue.TryEnqueue(UpdateWindowIcon);
    }

    private void UpdateWindowIcon() =>
        AppWindow.SetIcon(App.IsAppInDarkMode() ? "Assets\\Wings-Light.ico" : "Assets\\Wings-Dark.ico");

    public bool IsPopupVisible => _isPopupVisible;

    /// <summary>Raised each time the popup is shown, after it has been positioned.</summary>
    public event EventHandler? PopupShown;

    /// <summary>Raised when the popup starts hiding (light-dismiss, Escape or a tray click).</summary>
    public event EventHandler? PopupHidden;

    /// <summary>
    /// Shows the popup (if hidden) and puts the shell on <paramref name="destination"/>. Used by
    /// tray actions and toast activation, which know where the user wants to land.
    /// </summary>
    public void ShowAt(ShellDestination destination, DateTime? clickedAtUtc = null, Action? then = null)
    {
        if (!_isPopupVisible)
        {
            if ((clickedAtUtc ?? DateTime.UtcNow) - _lastDismissedAtUtc < RecentDismissWindow)
                return;

            ShowNearAnchor();
        }
        else if (_isWindowMode)
        {
            BringToFront();
        }

        _shellPage?.NavigateTo(destination, then);
    }

    /// <summary>
    /// Shows the popup near the last captured pointer anchor, or hides it when
    /// it is already visible (or was just light-dismissed by the same click).
    /// </summary>
    /// <param name="clickedAtUtc">
    /// When the click that asked for this happened. Usually now, but a tray click that had to
    /// wait out the double-click interval is older than that, and the "same click that
    /// dismissed us" check has to be made against the click, not against when it was acted on.
    /// </param>
    public void ToggleNearAnchor(DateTime? clickedAtUtc = null)
    {
        if (_isPopupVisible)
        {
            // A standard window doesn't go away on a tray click (it doesn't auto-hide); the click brings it forward.
            if (_isWindowMode)
                BringToFront();
            else
                HidePopup();
            return;
        }

        if ((clickedAtUtc ?? DateTime.UtcNow) - _lastDismissedAtUtc < RecentDismissWindow)
            return;

        ShowNearAnchor();
    }

    /// <summary>Positions the flyout next to the tray icon at its remembered size and plays the slide-in.</summary>
    private void PlaceFlyout()
    {
        (int width, int height) = SettingsService.PopupSize;
        WindowPlacementService.PositionWindowNearAnchor(this, width, height);
        _baseX = AppWindow.Position.X;
        _baseY = AppWindow.Position.Y;

        uint dpi = PInvoke.GetDpiForWindow(_hwnd);
        if (dpi == 0)
            dpi = 96;
        PlayShowAnimation(dpi);
    }

    /// <summary>Puts the standard window where it was last left, pulled onto a monitor that still exists.</summary>
    private void PlaceWindow()
    {
        uint dpi = PInvoke.GetDpiForWindow(_hwnd);
        if (dpi == 0)
            dpi = 96;

        PixelRect wanted = SettingsService.WindowBounds
            ?? new PixelRect(0, 0, (int)Math.Ceiling(DefaultWindowWidth * dpi / 96.0), (int)Math.Ceiling(DefaultWindowHeight * dpi / 96.0));

        DisplayArea display = DisplayArea.GetFromRect(new RectInt32(wanted.X, wanted.Y, wanted.Width, wanted.Height), DisplayAreaFallback.Nearest);
        RectInt32 work = display.WorkArea;
        PixelRect workArea = new(work.X, work.Y, work.Width, work.Height);

        PixelRect fitted = SettingsService.WindowBounds is null
            ? WindowBoundsPolicy.Centered(wanted.Width, wanted.Height, workArea)
            : WindowBoundsPolicy.Fit(wanted, workArea);

        AppWindow.MoveAndResize(new RectInt32(fitted.X, fitted.Y, fitted.Width, fitted.Height));
    }

    public void ShowNearAnchor()
    {
        EnsureShellPage();

        // Back to the root on each open, so the popup never reopens deep in a thread.
        _ = _shellPage?.ResetNavigationAsync();

        // Reopen in whichever form it was left in.
        bool asWindow = SettingsService.PopupIsWindow;
        if (asWindow != _isWindowMode)
            ApplyMode(asWindow);

        // Position before showing so the window never flashes at a stale location.
        if (asWindow)
            PlaceWindow();
        else
            WindowPlacementService.PositionWindowNearAnchor(this, SettingsService.PopupSize.Width, SettingsService.PopupSize.Height);

        _baseX = AppWindow.Position.X;
        _baseY = AppWindow.Position.Y;

        _hideTimer.Stop();
        _trimTimer.Stop();
        _isShowing = true;
        _isPopupVisible = true;

        if (!asWindow)
        {
            uint dpi = PInvoke.GetDpiForWindow(_hwnd);
            if (dpi == 0)
                dpi = 96;
            PlayShowAnimation(dpi);
        }

        this.Show();
        if (Presenter.State == OverlappedPresenterState.Minimized)
            Presenter.Restore();
        Activate();
        PInvoke.SetForegroundWindow(_hwnd);

        // Pages stay loaded while hidden; tell them there is a window to refresh into.
        PopupShown?.Invoke(this, EventArgs.Empty);

        // Once the window is back up, undo any deactivated (dimmed) title bar
        // visuals left over from the light-dismiss that hid it.
        DispatcherQueue.TryEnqueue(() => _shellPage?.RefreshTitleBarActivation());
    }

    /// <summary>Where the popup rests once its slide animation settles, in physical pixels.</summary>
    public PixelRect RestingBounds => _isWindowMode
        ? new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height)
        : new(_baseX, _baseY, AppWindow.Size.Width, AppWindow.Size.Height);

    public void HidePopup()
    {
        _hideTimer.Stop();
        _isShowing = false;

        // A video playing behind a hidden window would keep making noise, so stop it
        // regardless of whether the popup was actually visible.
        _shellPage?.TryCloseVideoOverlay();

        if (!_isPopupVisible)
            return;

        _settleTimer.Stop();

        if (_isWindowMode)
        {
            // No slide-out for a standard window: remember where it was, and go.
            SaveGeometry();
            _isPopupVisible = false;
            _lastDismissedAtUtc = DateTime.UtcNow;
            PopupHidden?.Invoke(this, EventArgs.Empty);
            this.Hide();
            _trimTimer.Start();
            return;
        }

        _isPopupVisible = false;
        _lastDismissedAtUtc = DateTime.UtcNow;
        PopupHidden?.Invoke(this, EventArgs.Empty);
        PlayHideAnimation();
    }

    private void PlayShowAnimation(uint dpi)
    {
        _isHiding = false;
        _durationMs = ShowDurationMs;
        _fromOffset = ScaleForDpi(ShowSlideDistance, dpi);
        _toOffset = 0;
        _fromOpacity = 0;
        _toOpacity = 1;

        // Deterministic starting state before every show, so replays are clean
        // regardless of where a previous hide animation left off.
        ApplyFrame(_fromOffset, 0);

        _animationClock.Restart();
        _animationTimer.Start();
    }

    private void PlayHideAnimation()
    {
        uint dpi = PInvoke.GetDpiForWindow(_hwnd);
        if (dpi == 0)
            dpi = 96;

        // Start from wherever the popup currently is: HidePopup can land while a
        // show animation is still running, and snapping to the resting state
        // first would read as a visible jump.
        _isHiding = true;
        _durationMs = HideDurationMs;
        _fromOffset = _currentOffset;
        _toOffset = ScaleForDpi(HideSlideDistance, dpi);
        _fromOpacity = _currentOpacity;
        _toOpacity = 0;

        _animationClock.Restart();
        _animationTimer.Start();
    }

    private void AnimationTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        double progress = _animationClock.Elapsed.TotalMilliseconds / _durationMs;
        bool finished = progress >= 1;
        if (finished)
            progress = 1;

        // Cubic ease on the slide (out on the way in, in on the way out) with a
        // linear fade, matching SongChangePopupWindow's feel.
        double eased = _isHiding
            ? progress * progress * progress
            : 1 - Math.Pow(1 - progress, 3);

        int offset = _fromOffset + (int)Math.Round((_toOffset - _fromOffset) * eased);
        double opacity = _fromOpacity + ((_toOpacity - _fromOpacity) * progress);

        ApplyFrame(offset, opacity);

        if (!finished)
            return;

        _animationTimer.Stop();
        _animationClock.Reset();

        if (_isHiding)
        {
            this.Hide();
            _trimTimer.Start();
        }
    }

    private void ApplyFrame(int offset, double opacity)
    {
        if (_hwnd == 0)
            return;

        _currentOffset = offset;
        _currentOpacity = opacity;

        SetAlpha(opacity);
        AppWindow.Move(new PointInt32(_baseX, _baseY + offset));
    }

    private void SetAlpha(double opacity)
    {
        byte alpha = (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);
        COLORREF cOLORREF = new(0); // Color is ignored when using LWA_ALPHA
        _ = PInvoke.SetLayeredWindowAttributes(_hwnd, cOLORREF, alpha, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
    }

    private static int ScaleForDpi(int logical, uint dpi) => (int)Math.Round(logical * dpi / 96.0);

    /// <summary>The root dialogs opened from outside a page (a deep link) attach to; null before the shell exists.</summary>
    public Microsoft.UI.Xaml.XamlRoot? ContentRoot => _shellPage?.XamlRoot;

    private void EnsureShellPage()
    {
        // One ShellPage kept alive across show/hide: reopening is instant and the
        // XamlRoot stays valid for ContentDialogs opened from hosted pages.
        if (_shellPage is null)
        {
            _shellPage = new ShellPage();
            _shellPage.SetWindowMode(_isWindowMode);
            _shellPage.ExpandRequested += (_, _) =>
            {
                if (!_isWindowMode && _isPopupVisible)
                    ExpandToWindow();
            };
            _shellPage.ReturnToFlyoutRequested += (_, _) =>
            {
                if (_isWindowMode && _isPopupVisible)
                    BecomeFlyout(fromResize: false);
            };
            ShellHost.Content = _shellPage;
        }
    }

    private void ConfigurePresenter()
    {
        OverlappedPresenter presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = true;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        unsafe
        {
            uint cornerPreference = DWMWCP_ROUND;
            _ = PInvoke.DwmSetWindowAttribute(_hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &cornerPreference, sizeof(int));
        }

        // WS_EX_LAYERED enables the per-window alpha the entrance/exit fade drives.
        int exStyle = PInvoke.GetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        _ = PInvoke.SetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
    }

    private void ConfigureToolWindow()
    {
        // No taskbar button, no Alt-Tab entry.
        HWND hwnd = (HWND)WindowNative.GetWindowHandle(this);
        int exStyle = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        _ = PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

        // The presenter's SetBorderAndTitleBar(false, false) can leave the
        // caption/resize-frame styles behind, which DWM renders as a visible
        // frame around the popup. Strip them and the 1px DWM border directly.
        int style = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        _ = PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE, style & ~(WS_CAPTION | WS_THICKFRAME));

        unsafe
        {
            uint cornerPreference = DWMWCP_ROUND;
            _ = PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &cornerPreference, sizeof(int));
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _isShowing = false;
            _hideTimer.Stop();
            return;
        }

        // A standard window stays up behind other windows; only the flyout light-dismisses.
        if (_isWindowMode || _isShowing || !_isPopupVisible)
            return;

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideIfFocusLeftWindow()
    {
        _hideTimer.Stop();

        if (!_isPopupVisible)
            return;

        // A FileOpenPicker (or anything else that hands focus to a broker window we don't
        // recognize as our own) shouldn't dismiss the popup out from under it.
        if (LightDismissSuppressionService.IsSuppressed)
            return;

        HWND foreground = PInvoke.GetForegroundWindow();
        if (ShouldRemainVisible(foreground))
            return;

        HidePopup();
    }

    private bool ShouldRemainVisible(HWND foregroundWindow)
    {
        if (foregroundWindow == 0)
            return false;

        nint windowHandle = WindowNative.GetWindowHandle(this);
        if (foregroundWindow == windowHandle)
            return true;

        if (PInvoke.GetAncestor(foregroundWindow, GET_ANCESTOR_FLAGS.GA_ROOT) == windowHandle)
            return true;

        // WinUI MenuFlyouts/ComboBox dropdowns open in separate popup HWNDs;
        // classic context menus use the #32768 class. Neither should dismiss us.
        return IsTransientPopupWindow(foregroundWindow);
    }

    private static bool IsTransientPopupWindow(HWND hwnd)
    {
        System.Span<Char> className = [];
        if (PInvoke.GetClassName(hwnd, className) == 0)
            return false;

        string name = className.ToString();
        return string.Equals(name, "#32768", StringComparison.Ordinal)
            || string.Equals(name, "Xaml_WindowedPopupClass", StringComparison.Ordinal);
    }

    private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (_shellPage?.TryCloseVideoOverlay() == true)
            return;

        if (_shellPage?.TryCloseImageOverlay() == true)
            return;

        // Escape dismisses the flyout, but doesn't close a standard window out from under you.
        if (!_isWindowMode)
            HidePopup();
    }
}
