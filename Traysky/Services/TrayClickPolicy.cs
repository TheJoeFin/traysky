using System;
using Traysky.Models;

namespace Traysky.Services;

/// <summary>Which of the tray icon's four click slots a setting is about.</summary>
public enum TrayClickButton
{
    Left,
    Right,
    LeftDouble,
    RightDouble
}

/// <summary>
/// Everything assigned to the tray icon at once, so rules that span the slots - "something
/// must open Traysky" - can look at all of them together.
/// </summary>
public readonly record struct TrayClickAssignments(
    TrayClickAction Left,
    TrayClickAction Right,
    TrayClickAction LeftDouble,
    TrayClickAction RightDouble)
{
    public TrayClickAction this[TrayClickButton button] => button switch
    {
        TrayClickButton.Left => Left,
        TrayClickButton.Right => Right,
        TrayClickButton.LeftDouble => LeftDouble,
        TrayClickButton.RightDouble => RightDouble,
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    public TrayClickAssignments With(TrayClickButton button, TrayClickAction action) => button switch
    {
        TrayClickButton.Left => this with { Left = action },
        TrayClickButton.Right => this with { Right = action },
        TrayClickButton.LeftDouble => this with { LeftDouble = action },
        TrayClickButton.RightDouble => this with { RightDouble = action },
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    /// <summary>Whether any slot, single or double, opens the flyout in some form.</summary>
    public bool OpensFlyout =>
        TrayClickPolicy.OpensFlyout(Left) ||
        TrayClickPolicy.OpensFlyout(Right) ||
        TrayClickPolicy.OpensFlyout(LeftDouble) ||
        TrayClickPolicy.OpensFlyout(RightDouble);
}

/// <summary>
/// Pure decision logic for the tray icon's configurable clicks. Kept free of WinRT/WinUI
/// dependencies so it can be unit tested directly (see Traysky.Tests). Ported from Traydio.
/// </summary>
public static class TrayClickPolicy
{
    /// <summary>Out-of-the-box assignment: left click opens the flyout, right click composes.</summary>
    public const TrayClickAction DefaultLeftAction = TrayClickAction.ShowFlyout;

    /// <inheritdoc cref="DefaultLeftAction"/>
    public const TrayClickAction DefaultRightAction = TrayClickAction.Compose;

    /// <summary>
    /// Double-clicks do nothing until assigned. Windows delivers the single click before the
    /// double-click, so honouring a double-click means holding the single click back for the
    /// double-click interval; that delay is only worth paying once the user has opted in.
    /// </summary>
    public const TrayClickAction DefaultDoubleClickAction = TrayClickAction.None;

    /// <summary>Every action other than <see cref="TrayClickAction.None"/> shows the flyout.</summary>
    public static bool OpensFlyout(TrayClickAction action) => action != TrayClickAction.None;

    /// <summary>
    /// Whether a button's single click has to wait for a possible double-click before acting.
    /// Only when the double-click slot is actually assigned; otherwise clicks stay instant.
    /// </summary>
    public static bool ShouldDeferSingleClick(TrayClickAction doubleClickAction) =>
        doubleClickAction != TrayClickAction.None;

    /// <summary>
    /// Reads a stored integer back as an action, falling back to <paramref name="fallback"/> for
    /// anything that is not a defined value.
    /// </summary>
    public static TrayClickAction Parse(int stored, TrayClickAction fallback) =>
        Enum.IsDefined(typeof(TrayClickAction), stored) ? (TrayClickAction)stored : fallback;

    /// <summary>
    /// Slots the guard may hand the flyout to, most preferred first. Single clicks come before
    /// double-clicks: a flyout that only opens on a double-click is hard to discover.
    /// </summary>
    private static readonly TrayClickButton[] FlyoutHomes =
    [
        TrayClickButton.Left,
        TrayClickButton.Right,
        TrayClickButton.LeftDouble,
        TrayClickButton.RightDouble
    ];

    /// <summary>
    /// Keeps the flyout reachable. Traysky lives entirely in the tray: if nothing opened it
    /// there would be no way back to Settings to undo the choice. When the slot the user just
    /// changed leaves no flyout anywhere, the flyout is moved to the most preferred
    /// <em>other</em> slot, so the user's own selection is always honoured.
    /// </summary>
    /// <returns>The set to store, which differs from the input only when the guard had to act.</returns>
    public static TrayClickAssignments EnsureFlyoutReachable(
        TrayClickButton changed,
        TrayClickAssignments assignments)
    {
        if (assignments.OpensFlyout)
            return assignments;

        foreach (TrayClickButton home in FlyoutHomes)
        {
            if (home != changed)
                return assignments.With(home, TrayClickAction.ShowFlyout);
        }

        return assignments;
    }

    /// <summary>
    /// The shell destination an action asks for. Everything that needs an account falls back
    /// to plain "show the flyout" while signed out, which lands on the login page.
    /// </summary>
    public static ShellDestination Resolve(TrayClickAction action, bool isSignedIn)
    {
        if (!isSignedIn)
            return ShellDestination.Current;

        return action switch
        {
            TrayClickAction.Compose => ShellDestination.Compose,
            TrayClickAction.ShowNotifications => ShellDestination.Notifications,
            TrayClickAction.ShowHome => ShellDestination.Home,
            _ => ShellDestination.Current
        };
    }
}
