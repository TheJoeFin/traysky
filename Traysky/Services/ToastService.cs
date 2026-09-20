using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using Traysky.Models;

namespace Traysky.Services;

/// <summary>
/// Windows toasts for new notifications, via the Windows App SDK's AppNotifications so they
/// land in Action Center and respect Focus Assist. Clicking one brings up the flyout on the
/// Notifications tab, whether the app is running or has to be launched for it.
/// </summary>
public static class ToastService
{
    private const string DestinationKey = "dest";
    private static Action<ShellDestination>? _onActivated;
    private static DispatcherQueue? _dispatcher;
    private static bool _registered;

    /// <summary>Call once on the UI thread during launch, before any toast is shown.</summary>
    public static void Register(Action<ShellDestination> onActivated)
    {
        if (_registered)
            return;

        _onActivated = onActivated;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        try
        {
            AppNotificationManager manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnNotificationInvoked;
            manager.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            // Unpackaged debug runs and some restricted environments cannot register; the app
            // works without toasts.
            LogService.Warn("Toast", $"AppNotificationManager.Register failed: {ex.Message}");
        }
    }

    public static void Unregister()
    {
        if (!_registered)
            return;

        try
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception ex)
        {
            LogService.Warn("Toast", $"Unregister failed: {ex.Message}");
        }
        _registered = false;
    }

    /// <summary>
    /// Handles a toast that launched the app (rather than being clicked while it ran). Returns
    /// the destination it asked for, or null when the launch was not from a toast.
    /// </summary>
    public static ShellDestination? DestinationFromLaunch(AppNotificationActivatedEventArgs? args)
    {
        if (args is null)
            return null;
        return ParseDestination(args);
    }

    public static void Show(string title, string body, ShellDestination destination = ShellDestination.Notifications, Uri? avatar = null)
    {
        if (!_registered)
            return;

        try
        {
            AppNotificationBuilder builder = new AppNotificationBuilder()
                .AddText(Truncate(title, 120))
                .AddArgument(DestinationKey, destination.ToString());

            if (!string.IsNullOrWhiteSpace(body))
                builder.AddText(Truncate(body, 300));

            if (avatar is not null && avatar.Scheme == Uri.UriSchemeHttps)
                builder.SetAppLogoOverride(avatar, AppNotificationImageCrop.Circle);

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            LogService.Warn("Toast", $"Show failed: {ex.Message}");
        }
    }

    /// <summary>Clears everything Traysky has in Action Center, e.g. after the user reads the tab.</summary>
    public static void ClearAll()
    {
        if (!_registered)
            return;

        try
        {
            _ = AppNotificationManager.Default.RemoveAllAsync();
        }
        catch (Exception ex)
        {
            LogService.Warn("Toast", $"RemoveAll failed: {ex.Message}");
        }
    }

    private static void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        ShellDestination destination = ParseDestination(args);
        if (_dispatcher is null)
        {
            _onActivated?.Invoke(destination);
            return;
        }

        _dispatcher.TryEnqueue(() => _onActivated?.Invoke(destination));
    }

    private static ShellDestination ParseDestination(AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue(DestinationKey, out string? value)
            && Enum.TryParse(value, out ShellDestination destination))
        {
            return destination;
        }
        return ShellDestination.Notifications;
    }

    private static string Truncate(string text, int max)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
