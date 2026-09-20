namespace Traysky.Models;

/// <summary>
/// What a click on the tray icon does. Each of the four click slots (left, right, and their
/// double-clicks) gets its own, chosen in Settings.
/// <para>Values are explicit because this is persisted as an integer.</para>
/// </summary>
public enum TrayClickAction
{
    /// <summary>The click is ignored.</summary>
    None = 0,

    /// <summary>Opens (or closes) the Traysky flyout on whatever tab it was last on.</summary>
    ShowFlyout = 1,

    /// <summary>Opens the flyout with the compose box focused.</summary>
    Compose = 2,

    /// <summary>Opens the flyout on the Notifications tab.</summary>
    ShowNotifications = 3,

    /// <summary>Opens the flyout on the Home timeline.</summary>
    ShowHome = 4
}
