using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;

namespace Traysky.Helpers;

/// <summary>
/// Static x:Bind functions for use inside DataTemplates, where instance methods on the page
/// are out of reach.
/// </summary>
public static class BindHelpers
{
    public static Brush BrushFromKey(string key) => (Brush)Application.Current.Resources[key];

    public static Brush LikeBrush(bool liked) => BrushFromKey(liked ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush");

    public static Brush RepostBrush(bool reposted) => BrushFromKey(reposted ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush");

    /// <summary>Unread rows get the faint accent wash the official app uses.</summary>
    public static Brush UnreadBackground(bool unread) => unread
        ? BrushFromKey("SubtleFillColorSecondaryBrush")
        : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    /// <summary>PersonPicture wants an ImageSource; the models carry plain Uris.</summary>
    public static ImageSource? ImageFromUri(Uri? uri) => uri is null ? null : new BitmapImage(uri) { DecodePixelWidth = 96 };

    /// <summary>
    /// Decodes picture bytes the view model holds (an attachment, a chosen avatar) into an
    /// ImageSource, so the view model never creates a UI type. Null when there are no bytes.
    /// </summary>
    public static ImageSource? ImageFromBytes(byte[]? bytes, int decodePixelWidth = 0)
    {
        if (bytes is not { Length: > 0 })
            return null;

        BitmapImage image = new();
        if (decodePixelWidth > 0)
            image.DecodePixelWidth = decodePixelWidth;
        image.SetSource(new MemoryStream(bytes).AsRandomAccessStream());
        return image;
    }

    /// <summary>A compose attachment's thumbnail: videos have none (the view shows a glyph).</summary>
    public static ImageSource? AttachmentThumbnail(bool isVideo, byte[] bytes) => isVideo ? null : ImageFromBytes(bytes);

    /// <summary>The profile editor's preview: a chosen picture wins over the account's current one.</summary>
    public static ImageSource? ProfileImagePreview(Uri? currentUri, byte[]? chosenBytes) =>
        chosenBytes is not null ? ImageFromBytes(chosenBytes) : ImageFromUri(currentUri);

    /// <summary>Own chat messages sit on the right, others' on the left.</summary>
    public static HorizontalAlignment AlignEnd(bool isMine) => isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    /// <summary>A little more air above the first message of a run.</summary>
    public static Thickness RunMargin(bool startsRun) => startsRun ? new Thickness(0, 10, 0, 0) : new Thickness(0, 2, 0, 0);

    /// <summary>Thread replies indent 16px per level of depth.</summary>
    public static Thickness ThreadIndent(int depth) => new(depth * 16, 0, 0, 0);

    public static bool Not(bool value) => !value;

    /// <summary>InfoBadge.Value is an int; chat unread counts are longs.</summary>
    public static int BadgeValue(long count) => (int)Math.Min(count, 99);

    /// <summary>The post a thread page is about gets a faint highlight so it stands out from ancestors and replies.</summary>
    public static Brush MainPostBackground(bool isMain) => isMain
        ? BrushFromKey("LayerFillColorAltBrush")
        : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
}
