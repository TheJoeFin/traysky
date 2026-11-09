namespace Traysky.Services;

/// <summary>How much chrome the shell shows, which grows with the flyout's width (like bsky.app's breakpoints).</summary>
public enum ShellLayoutKind
{
    /// <summary>The default flyout: nav buttons in the title bar, feed tabs above the timeline.</summary>
    Compact,

    /// <summary>A left rail of icon buttons; the title bar keeps only back and quit.</summary>
    Medium,

    /// <summary>Icon rail plus a right column with search and the feed list.</summary>
    Wide,

    /// <summary>As Wide, with labels beside the rail's icons.</summary>
    VeryWide,
}

/// <summary>Pure width-to-layout decisions for the shell, so the breakpoints can be tested without a window.</summary>
public static class ShellLayoutPolicy
{
    /// <summary>Widths are in DIPs and are the lower bound (inclusive) of each layout.</summary>
    public const double MediumMinWidth = 640;
    public const double WideMinWidth = 960;
    public const double VeryWideMinWidth = 1280;

    /// <summary>The feed column stops growing here and is centred, as on bsky.app.</summary>
    public const double ContentMaxWidth = 600;

    public static ShellLayoutKind ForWidth(double width) => width switch
    {
        >= VeryWideMinWidth => ShellLayoutKind.VeryWide,
        >= WideMinWidth => ShellLayoutKind.Wide,
        >= MediumMinWidth => ShellLayoutKind.Medium,
        _ => ShellLayoutKind.Compact,
    };

    /// <summary>
    /// Once the flyout is a standard window it only goes back to a flyout this far below the
    /// threshold that made it one. Swapping the window's frame shifts its client width by a few
    /// pixels, which at the exact boundary would otherwise flip it back and forth.
    /// </summary>
    public const double WindowReturnHysteresis = 40;

    /// <summary>
    /// Whether the shell should be a standard window (title bar, taskbar button, no light-dismiss)
    /// rather than the tray flyout: from the first breakpoint up.
    /// </summary>
    public static bool ShouldBeWindow(double width, bool isWindow) =>
        width >= (isWindow ? MediumMinWidth - WindowReturnHysteresis : MediumMinWidth);

    public static bool ShowsRail(ShellLayoutKind kind) => kind != ShellLayoutKind.Compact;

    public static bool RailShowsLabels(ShellLayoutKind kind) => kind == ShellLayoutKind.VeryWide;

    public static bool ShowsSidebar(ShellLayoutKind kind) => kind is ShellLayoutKind.Wide or ShellLayoutKind.VeryWide;

    /// <summary>The widest the page column gets; unbounded in the compact flyout, where it fills the window.</summary>
    public static double MaxContentWidth(ShellLayoutKind kind) =>
        kind == ShellLayoutKind.Compact ? double.PositiveInfinity : ContentMaxWidth;
}
