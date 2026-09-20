namespace Traysky.Models;

/// <summary>
/// Where the shell should land when the flyout is opened by something that knows what the
/// user wants to see: a tray click assigned to a destination, or a toast about a notification.
/// </summary>
public enum ShellDestination
{
    /// <summary>Whatever the shell was last showing.</summary>
    Current,

    /// <summary>The timeline, on whatever feed tab was last selected.</summary>
    Home,
    Notifications,

    /// <summary>The standalone compose page, for a fresh post.</summary>
    Compose,
    Settings
}
