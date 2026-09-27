using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using Traysky.ViewModels.Items;
using Windows.Foundation;

namespace Traysky.Controls;

/// <summary>
/// One embedded picture (or GIF, or video thumbnail). It takes its final size from the embed's
/// aspect ratio before anything has downloaded, and shows a placeholder until the image fades in
/// over it, so a card doesn't grow when its image arrives.
/// </summary>
public sealed partial class EmbedImageView : UserControl
{
    private const double FallbackMaxHeight = 220;

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(EmbedImage), typeof(EmbedImageView), new PropertyMetadata(null, OnItemChanged));

    public EmbedImageView()
    {
        InitializeComponent();
    }

    public EmbedImage? Item
    {
        get => (EmbedImage?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (EmbedImageView)d;

        // ItemsRepeater recycles these, so a new item starts from the placeholder again rather
        // than briefly showing the previous post's picture.
        ((Storyboard)view.Resources["FadeIn"]).Stop();
        view.Picture.Opacity = 0;
        view.Placeholder.Opacity = 1;
        view.Bitmap.UriSource = (e.NewValue as EmbedImage)?.Thumbnail;
        view.InvalidateMeasure();
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

    private void Picture_ImageOpened(object sender, RoutedEventArgs e) =>
        ((Storyboard)Resources["FadeIn"]).Begin();

    // A picture that can't be fetched leaves the placeholder showing instead of an empty hole.
    private void Picture_ImageFailed(object sender, ExceptionRoutedEventArgs e) =>
        Placeholder.Opacity = 1;
}
