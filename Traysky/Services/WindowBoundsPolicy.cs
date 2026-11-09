using System;

namespace Traysky.Services;

/// <summary>Keeps a remembered window rectangle usable after monitors or scaling change.</summary>
public static class WindowBoundsPolicy
{
    /// <summary>
    /// <paramref name="bounds"/> shrunk to fit <paramref name="workArea"/> and moved fully inside
    /// it. A window saved on a monitor that is no longer there is pulled onto one that is.
    /// </summary>
    public static PixelRect Fit(PixelRect bounds, PixelRect workArea)
    {
        int width = Math.Min(Math.Max(bounds.Width, 1), workArea.Width);
        int height = Math.Min(Math.Max(bounds.Height, 1), workArea.Height);
        int x = Math.Clamp(bounds.X, workArea.X, workArea.Right - width);
        int y = Math.Clamp(bounds.Y, workArea.Y, workArea.Bottom - height);
        return new PixelRect(x, y, width, height);
    }

    /// <summary><paramref name="size"/> centred in <paramref name="workArea"/> (shrunk if it does not fit).</summary>
    public static PixelRect Centered(int width, int height, PixelRect workArea) =>
        Fit(new PixelRect(
            workArea.X + ((workArea.Width - width) / 2),
            workArea.Y + ((workArea.Height - height) / 2),
            width,
            height), workArea);
}
