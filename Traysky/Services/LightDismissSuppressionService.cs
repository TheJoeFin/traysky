using System;
using System.Threading;

namespace Traysky.Services;

/// <summary>
/// Lets something deep in the tray popup (a file picker, say) tell
/// <see cref="Controls.TrayPopupWindow"/> "don't light-dismiss right now" without either side
/// needing a reference to the other — same decoupling trick as
/// <see cref="ImageViewerService"/>/<see cref="VideoViewerService"/>, just in the other
/// direction. Reference-counted so overlapping callers (unlikely today, but cheap to allow)
/// can't release each other's suppression early.
/// </summary>
public static class LightDismissSuppressionService
{
    private static int _count;

    public static bool IsSuppressed => Volatile.Read(ref _count) > 0;

    /// <summary>
    /// Suppresses light-dismiss until the returned scope is disposed. Wrap anything that hands
    /// focus to a separate top-level window the popup doesn't otherwise recognize as its own —
    /// a <c>FileOpenPicker</c>, for instance, whose broker window isn't a WinUI popup HWND and
    /// so isn't covered by <c>TrayPopupWindow</c>'s existing ComboBox/MenuFlyout carve-out.
    /// </summary>
    public static IDisposable Suppress()
    {
        Interlocked.Increment(ref _count);
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                Interlocked.Decrement(ref _count);
        }
    }
}
