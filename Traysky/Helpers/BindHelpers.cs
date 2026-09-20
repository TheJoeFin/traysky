using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

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

    public static bool Not(bool value) => !value;

    /// <summary>The post a thread page is about gets a faint highlight so it stands out from ancestors and replies.</summary>
    public static Brush MainPostBackground(bool isMain) => isMain
        ? BrushFromKey("LayerFillColorAltBrush")
        : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
}
