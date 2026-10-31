using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using Traysky.Services;
using Traysky.ViewModels.Items;
using Windows.Foundation;

namespace Traysky.Controls;

/// <summary>
/// One embedded picture (or GIF, or video thumbnail). It takes its final size from the embed's
/// aspect ratio before anything has downloaded, and shows a placeholder until the image fades in
/// over it, so a card doesn't grow when its image arrives. The picture is decoded at the size it's
/// drawn at, not the thumbnail's full width.
/// </summary>
public sealed partial class EmbedImageView : UserControl
{
    private const double FallbackMaxHeight = 220;

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(EmbedImage), typeof(EmbedImageView), new PropertyMetadata(null, OnItemChanged));

    /// <summary>Overlays a play button, centered on the picture, for a video thumbnail.</summary>
    public static readonly DependencyProperty IsVideoProperty = DependencyProperty.Register(
        nameof(IsVideo), typeof(bool), typeof(EmbedImageView), new PropertyMetadata(false, OnIsVideoChanged));

    // The picture this view shows (loaded, or waiting for its first arrange), and the logical
    // width it was decoded at; 0 until it has been asked to decode.
    private Uri? _thumbnail;
    private int _decodeWidth;

    public EmbedImageView()
    {
        InitializeComponent();
    }

    public EmbedImage? Item
    {
        get => (EmbedImage?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public bool IsVideo
    {
        get => (bool)GetValue(IsVideoProperty);
        set => SetValue(IsVideoProperty, value);
    }

    /// <summary>Raised by a video thumbnail's pop-out button.</summary>
    public event EventHandler? PopOutRequested;

    private static void OnIsVideoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (EmbedImageView)d;
        Visibility visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        view.PlayGlyph.Visibility = visibility;
        view.PopOutButton.Visibility = visibility;
    }

    private void PopOutButton_Click(object sender, RoutedEventArgs e) =>
        PopOutRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Keeps the tap from also reaching the thumbnail and opening the inline player.</summary>
    private void PopOutButton_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (EmbedImageView)d;

        // The same picture again (a card rebound to its own post): keep it showing rather than
        // fading it out and back in. The size may still need updating.
        Uri? thumbnail = (e.NewValue as EmbedImage)?.Thumbnail;
        if (thumbnail is not null && thumbnail == view._thumbnail)
        {
            view.InvalidateMeasure();
            return;
        }

        // EmbedImageGrid reuses these, so a new item starts from the placeholder again rather
        // than briefly showing the previous post's picture. Clearing the source frees the old
        // picture's decoded pixels now; the new one is decoded on the next arrange, at the size
        // it's drawn at.
        ((Storyboard)view.Resources["FadeIn"]).Stop();
        view.Picture.Opacity = 0;
        view.Placeholder.Opacity = 1;
        view.Bitmap.UriSource = null;
        view._thumbnail = thumbnail;
        view._decodeWidth = 0;
        view.InvalidateMeasure();
        view.InvalidateArrange();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double aspect = Item?.AspectRatio is > 0 and var a ? a : 16.0 / 9.0;
        double maxHeight = double.IsFinite(MaxHeight) ? MaxHeight : FallbackMaxHeight;

        double width, height;
        if (double.IsFinite(availableSize.Width))
        {
            width = availableSize.Width;
            height = Math.Min(width / aspect, maxHeight);
        }
        else
        {
            height = maxHeight;
            width = height * aspect;
        }

        Size size = new(width, height);
        base.MeasureOverride(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // First arrange with a picture: decode it at the width it's drawn at. A slot that later
        // grows (the popup resized wider) decodes again, larger, instead of stretching a small
        // decode. Changing DecodePixelWidth re-decodes a loaded picture on its own.
        if (_thumbnail is not null && finalSize.Width > 0 && finalSize.Height > 0)
        {
            int drawn = EmbedImageLayoutPolicy.DrawnWidth(finalSize.Width, finalSize.Height, Item?.AspectRatio ?? 0);
            int width = EmbedImageLayoutPolicy.DecodeWidth(drawn, _decodeWidth);
            if (width != _decodeWidth)
            {
                bool firstDecode = _decodeWidth == 0;
                _decodeWidth = width;
                Bitmap.DecodePixelWidth = width;
                if (firstDecode)
                    Bitmap.UriSource = _thumbnail;
            }
        }

        return base.ArrangeOverride(finalSize);
    }

    private void Picture_ImageOpened(object sender, RoutedEventArgs e) =>
        ((Storyboard)Resources["FadeIn"]).Begin();

    // A picture that can't be fetched leaves the placeholder showing instead of an empty hole.
    private void Picture_ImageFailed(object sender, ExceptionRoutedEventArgs e) =>
        Placeholder.Opacity = 1;
}
