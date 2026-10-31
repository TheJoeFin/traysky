using System;

namespace Traysky.Services;

/// <summary>
/// Lays out an embed's pictures the way the UniformGridLayout it replaced did (as many columns of
/// at least a minimum width as fit, sharing the leftover width), and picks the size to decode each
/// picture at so it isn't held in memory any larger than it's drawn.
/// </summary>
public static class EmbedImageLayoutPolicy
{
    /// <summary>
    /// How many columns of at least <paramref name="minItemWidth"/> fit, by width alone: a lone
    /// picture still takes one column, not the whole row. With no width limit every picture
    /// shares one row.
    /// </summary>
    public static int Columns(double availableWidth, double minItemWidth, double spacing, int count) =>
        double.IsFinite(availableWidth)
            ? Math.Max(1, (int)(availableWidth / (minItemWidth + spacing)))
            : Math.Max(1, count);

    /// <summary>The columns' width once they share the leftover space, in whole pixels (UniformGridLayout's ItemsStretch.Fill).</summary>
    public static double CellWidth(double availableWidth, int columns, double minItemWidth, double spacing)
    {
        if (!double.IsFinite(availableWidth))
            return minItemWidth;

        double used = columns * (minItemWidth + spacing) - spacing;
        return minItemWidth + (int)(availableWidth - used) / columns;
    }

    /// <summary>
    /// How wide, in whole logical pixels, a picture is drawn in a cell this size: a tall picture is
    /// letterboxed narrower than its cell. With no known aspect ratio the cell's width is the safe
    /// bound.
    /// </summary>
    public static int DrawnWidth(double cellWidth, double cellHeight, double aspectRatio)
    {
        double drawn = aspectRatio > 0 ? Math.Min(cellWidth, cellHeight * aspectRatio) : cellWidth;
        return Math.Max(1, (int)Math.Ceiling(drawn));
    }

    /// <summary>
    /// The width to decode a picture at, given how wide it's drawn and what it was last decoded at
    /// (0 if not yet); returning <paramref name="decodedWidth"/> means leave it. The first decode
    /// is exactly the drawn width. Growing past it (the popup resized wider) decodes again with a
    /// quarter to spare, so dragging the edge doesn't re-decode at every step; shrinking keeps
    /// the larger decode.
    /// </summary>
    public static int DecodeWidth(int drawnWidth, int decodedWidth)
    {
        if (decodedWidth == 0)
            return drawnWidth;

        return drawnWidth > decodedWidth ? (int)Math.Ceiling(drawnWidth * 1.25) : decodedWidth;
    }
}
