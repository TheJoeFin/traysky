using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.Controls;

/// <summary>
/// Renders a post's embed: an image grid, a video thumbnail, a link card, or a quoted post.
/// A still image opens full size in the popup itself, and a video plays inline in the popup's
/// video overlay; link cards and quotes open in the browser. The hosting card supplies the post
/// URL as a fallback for media that has no URL of its own.
/// </summary>
public sealed partial class EmbedPresenter : UserControl
{
    public static readonly DependencyProperty EmbedProperty = DependencyProperty.Register(
        nameof(Embed), typeof(EmbedItem), typeof(EmbedPresenter), new PropertyMetadata(null));

    /// <summary>Where a click on media goes when the media itself has no link (video, image → the post).</summary>
    public static readonly DependencyProperty FallbackUrlProperty = DependencyProperty.Register(
        nameof(FallbackUrl), typeof(string), typeof(EmbedPresenter), new PropertyMetadata(null));

    public EmbedPresenter()
    {
        InitializeComponent();
    }

    public EmbedItem? Embed
    {
        get => (EmbedItem?)GetValue(EmbedProperty);
        set => SetValue(EmbedProperty, value);
    }

    public string? FallbackUrl
    {
        get => (string?)GetValue(FallbackUrlProperty);
        set => SetValue(FallbackUrlProperty, value);
    }

    public Visibility QuoteVisibility(EmbedItem? embed) => embed?.QuoteAtUri is not null ? Visibility.Visible : Visibility.Collapsed;

    public string AtHandle(string? handle) => string.IsNullOrEmpty(handle) ? string.Empty : "@" + handle;

    private void Image_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;

        // A video plays inline in the popup's overlay; fall back to the browser only if the
        // view somehow has no playlist (older cached data, unexpected embed shape).
        if (Embed?.IsVideo == true)
        {
            if (Embed.VideoPlaylistUri is not null)
                VideoViewerService.Show(Embed.VideoPlaylistUri, Embed.VideoThumbnail, Embed.VideoAspectRatio);
            else
                _ = RichTextBuilder.OpenAsync(FallbackUrl);
            return;
        }

        if (sender is Image { Tag: Uri full })
            ImageViewerService.Show(full);
        else
            _ = RichTextBuilder.OpenAsync(FallbackUrl);
    }

    private void External_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        _ = RichTextBuilder.OpenAsync(Embed?.ExternalUri?.ToString());
    }

    private void Quote_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        _ = RichTextBuilder.OpenAsync(Embed?.QuoteWebUrl);
    }
}
