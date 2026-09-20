namespace Traysky.Services;

/// <summary>Which of the shipped .ico files the tray should show.</summary>
public enum TrayIconVariant
{
    /// <summary>Signed in, nothing unread: the blue butterfly.</summary>
    Normal,

    /// <summary>Signed in with unread notifications: blue butterfly plus a red dot.</summary>
    Unread,

    /// <summary>Signed out; monochrome to match a light taskbar.</summary>
    SignedOutLight,

    /// <summary>Signed out; monochrome to match a dark taskbar.</summary>
    SignedOutDark,

    /// <summary>Signed in but the last poll failed or the machine is offline: grey.</summary>
    Offline
}

/// <summary>
/// Decides the tray icon from the app's state. Pure .NET on purpose.
/// </summary>
public static class UnreadBadgePolicy
{
    public static TrayIconVariant Choose(bool isSignedIn, int unreadCount, bool isOffline, bool isDarkTaskbar)
    {
        if (!isSignedIn)
            return isDarkTaskbar ? TrayIconVariant.SignedOutDark : TrayIconVariant.SignedOutLight;

        // An unread dot the user has already earned should not vanish because a later poll
        // failed - stale-but-true beats grey-and-silent.
        if (unreadCount > 0)
            return TrayIconVariant.Unread;

        if (isOffline)
            return TrayIconVariant.Offline;

        return TrayIconVariant.Normal;
    }

    /// <summary>
    /// The tray renders icons too small for the gradient-filled Wings-Dark/Light art to read, so
    /// the tray uses the flat Wings-MonoBlack/MonoWhite variants instead: MonoBlack reads on a
    /// light taskbar, MonoWhite on a dark one. <paramref name="isDarkTaskbar"/> only matters for
    /// the families that ship both (Normal, Unread) - Offline has a single grey rendering.
    /// </summary>
    public static string AssetPath(TrayIconVariant variant, bool isDarkTaskbar) => variant switch
    {
        TrayIconVariant.Unread => isDarkTaskbar ? "Assets/Wings-MonoWhite-Unread.ico" : "Assets/Wings-MonoBlack-Unread.ico",
        TrayIconVariant.SignedOutLight => "Assets/Wings-MonoBlack.ico",
        TrayIconVariant.SignedOutDark => "Assets/Wings-MonoWhite.ico",
        TrayIconVariant.Offline => "Assets/Wings-Offline.ico",
        _ => isDarkTaskbar ? "Assets/Wings-MonoWhite.ico" : "Assets/Wings-MonoBlack.ico"
    };

    /// <summary>The tooltip to go with the icon. Kept short: Windows truncates at 127 chars.</summary>
    public static string Tooltip(bool isSignedIn, string? handle, int unreadCount, bool isOffline)
    {
        if (!isSignedIn)
            return "Traysky — click to sign in";

        string who = string.IsNullOrEmpty(handle) ? "Traysky" : $"Traysky — @{handle}";

        if (unreadCount > 0)
            return $"{who}\n{unreadCount} unread notification{(unreadCount == 1 ? "" : "s")}";

        if (isOffline)
            return $"{who}\nOffline";

        return who;
    }
}
