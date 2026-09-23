using System;
using System.Runtime;
using Windows.Win32;

namespace Traysky.Services;

/// <summary>
/// Gives memory back while the popup is hidden - the state a tray app spends nearly all its
/// time in. <see cref="Controls.TrayPopupWindow"/> calls <see cref="Trim"/> a while after
/// hiding, so a quick close-and-reopen never pays for it.
/// </summary>
public static class MemoryTrimService
{
    /// <summary>How long the popup stays hidden before trimming.</summary>
    public static readonly TimeSpan HiddenDelay = TimeSpan.FromSeconds(30);

    public static void Trim()
    {
        // Aggressive mode (.NET 7+) compacts every generation, including the LOH, and then
        // decommits the GC's free space back to the OS instead of keeping it around for reuse.
        // The managed heap here is only a few MB, so the full blocking collection is cheap.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        // Ask Windows to page out whatever this process isn't touching. Most of a WinUI app's
        // working set is native (XAML, composition, image and font caches, loaded DLLs) that
        // nothing reads while the window is hidden; it's paged back in cheaply from the
        // standby list the next time the popup opens.
        PInvoke.SetProcessWorkingSetSize(PInvoke.GetCurrentProcess(), nuint.MaxValue, nuint.MaxValue);
    }
}
