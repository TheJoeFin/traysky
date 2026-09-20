using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Reflection;
using System.Threading.Tasks;
using Traysky.Models;
using Traysky.Services;
using Windows.ApplicationModel;

namespace Traysky.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private const string StartupTaskId = "TrayskyStartup";
    private StartupTask? _startupTask;
    private bool _startupInitDone;
    private bool _suppressStartupWrite;

    public SettingsViewModel()
    {
        Session = BlueskySessionService.Instance;
        _ = InitializeStartupTaskAsync();
    }

    public BlueskySessionService Session { get; }

    public string Version
    {
        get
        {
            try
            {
                PackageVersion v = Package.Current.Id.Version;
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch
            {
                return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev";
            }
        }
    }

    // ---- Account -------------------------------------------------------------------------

    public string AccountLine => Session.IsSignedIn
        ? $"Signed in as @{Session.Handle}"
        : "Not signed in";

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await Session.LogoutAsync();
        OnPropertyChanged(nameof(AccountLine));
    }

    // ---- Polling -------------------------------------------------------------------------

    /// <summary>Index into <see cref="PollBackoffPolicy.IntervalChoicesSeconds"/>.</summary>
    public int PollIntervalIndex
    {
        get => Math.Max(0, Array.IndexOf(PollBackoffPolicy.IntervalChoicesSeconds, SettingsService.PollIntervalSeconds));
        set
        {
            if (value < 0 || value >= PollBackoffPolicy.IntervalChoicesSeconds.Length)
                return;
            SettingsService.PollIntervalSeconds = PollBackoffPolicy.IntervalChoicesSeconds[value];
            OnPropertyChanged();
        }
    }

    // ---- Toasts --------------------------------------------------------------------------

    public bool ToastMentions
    {
        get => SettingsService.ToastMentions;
        set { SettingsService.ToastMentions = value; OnPropertyChanged(); }
    }

    public bool ToastReplies
    {
        get => SettingsService.ToastReplies;
        set { SettingsService.ToastReplies = value; OnPropertyChanged(); }
    }

    public bool ToastQuotes
    {
        get => SettingsService.ToastQuotes;
        set { SettingsService.ToastQuotes = value; OnPropertyChanged(); }
    }

    public bool ToastFollows
    {
        get => SettingsService.ToastFollows;
        set { SettingsService.ToastFollows = value; OnPropertyChanged(); }
    }

    public bool ToastLikesReposts
    {
        get => SettingsService.ToastLikesReposts;
        set { SettingsService.ToastLikesReposts = value; OnPropertyChanged(); }
    }

    // ---- Tray clicks ---------------------------------------------------------------------
    // ComboBox indices map straight onto TrayClickAction values (None=0 … ShowHome=4).

    public int LeftClickIndex
    {
        get => (int)SettingsService.TrayLeftClickAction;
        set => SetClick(TrayClickButton.Left, value);
    }

    public int RightClickIndex
    {
        get => (int)SettingsService.TrayRightClickAction;
        set => SetClick(TrayClickButton.Right, value);
    }

    public int LeftDoubleClickIndex
    {
        get => (int)SettingsService.TrayLeftDoubleClickAction;
        set => SetClick(TrayClickButton.LeftDouble, value);
    }

    public int RightDoubleClickIndex
    {
        get => (int)SettingsService.TrayRightDoubleClickAction;
        set => SetClick(TrayClickButton.RightDouble, value);
    }

    private void SetClick(TrayClickButton button, int index)
    {
        TrayClickAction action = TrayClickPolicy.Parse(index, TrayClickAction.None);
        switch (button)
        {
            case TrayClickButton.Left: SettingsService.TrayLeftClickAction = action; break;
            case TrayClickButton.Right: SettingsService.TrayRightClickAction = action; break;
            case TrayClickButton.LeftDouble: SettingsService.TrayLeftDoubleClickAction = action; break;
            case TrayClickButton.RightDouble: SettingsService.TrayRightDoubleClickAction = action; break;
        }

        // The reachability guard may have moved the flyout to another slot; refresh them all.
        OnPropertyChanged(nameof(LeftClickIndex));
        OnPropertyChanged(nameof(RightClickIndex));
        OnPropertyChanged(nameof(LeftDoubleClickIndex));
        OnPropertyChanged(nameof(RightDoubleClickIndex));
    }

    // ---- Startup -------------------------------------------------------------------------

    [ObservableProperty]
    public partial bool IsStartupEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsStartupToggleEnabled { get; private set; }

    [ObservableProperty]
    public partial string StartupHint { get; private set; } = string.Empty;

    partial void OnIsStartupEnabledChanged(bool value)
    {
        if (!_suppressStartupWrite)
            _ = ApplyStartupStateAsync(value);
    }

    private async Task InitializeStartupTaskAsync()
    {
        try
        {
            _startupTask = await StartupTask.GetAsync(StartupTaskId).AsTask();
            _startupInitDone = true;
            ReadStartupState();
        }
        catch
        {
            // Unpackaged run: no startup task registration to talk to.
            IsStartupToggleEnabled = false;
            StartupHint = "Available when installed from the Store or as an MSIX package.";
        }
    }

    private void ReadStartupState()
    {
        if (_startupTask is null)
            return;

        _suppressStartupWrite = true;
        try
        {
            switch (_startupTask.State)
            {
                case StartupTaskState.Enabled:
                    IsStartupToggleEnabled = true;
                    IsStartupEnabled = true;
                    StartupHint = string.Empty;
                    break;
                case StartupTaskState.Disabled:
                    IsStartupToggleEnabled = true;
                    IsStartupEnabled = false;
                    StartupHint = string.Empty;
                    break;
                case StartupTaskState.DisabledByUser:
                    IsStartupToggleEnabled = false;
                    IsStartupEnabled = false;
                    StartupHint = "Turned off in Windows Settings › Apps › Startup. Enable it there.";
                    break;
                default:
                    IsStartupToggleEnabled = false;
                    IsStartupEnabled = false;
                    StartupHint = "Disabled by policy.";
                    break;
            }
        }
        finally
        {
            _suppressStartupWrite = false;
        }
    }

    private async Task ApplyStartupStateAsync(bool enable)
    {
        if (!_startupInitDone || _startupTask is null)
            return;

        try
        {
            if (enable && _startupTask.State == StartupTaskState.Disabled)
                await _startupTask.RequestEnableAsync().AsTask();
            else if (!enable && _startupTask.State == StartupTaskState.Enabled)
                _startupTask.Disable();
        }
        catch (Exception ex)
        {
            LogService.Warn("Settings", $"Startup task change failed: {ex.Message}");
        }

        ReadStartupState();
    }

    // ---- Diagnostics ---------------------------------------------------------------------

    [RelayCommand]
    private void CopyDiagnostics()
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(LogService.ReadRecentText());
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            LogService.Warn("Settings", $"Copy diagnostics failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private Task OpenGitHub() => RichTextBuilder.OpenAsync("https://github.com/TheJoeFin/Traysky");
}
