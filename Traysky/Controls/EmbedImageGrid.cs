using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using Traysky.Services;
using Traysky.ViewModels.Items;
using Windows.Foundation;

namespace Traysky.Controls;

/// <summary>
/// An embed's pictures (or a video's thumbnail) in columns at least <see cref="MinItemWidth"/>
/// wide, laid out as the UniformGridLayout it replaces did. It keeps one
/// <see cref="EmbedImageView"/> per picture it has ever had to show - four at most for a Bluesky
/// post - and points them at the new pictures when the card is reused.
/// </summary>
/// <remarks>
/// Not an ItemsRepeater. Nested in the timeline's ListView, a repeater virtualized these few
/// pictures against the list's viewport, unrealizing and realizing them as a card scrolled near
/// the edge. Each realization built and decoded a new view while the recycled one stayed
/// parented off-screen, still decoded: a memory dump held 258 copies of one picture. Its
/// templates also had to be built in code, since an x:DataType'd template cast the inherited
/// PostItem DataContext and crashed.
/// </remarks>
public sealed partial class EmbedImageGrid : Panel
{
    // Typed object, like ItemsSource, so WinRT never has to name a generic list of a .NET type.
    public static readonly DependencyProperty ImagesProperty = DependencyProperty.Register(
        nameof(Images), typeof(object), typeof(EmbedImageGrid), new PropertyMetadata(null, OnContentChanged));

    /// <summary>Draws a play button over the (single) thumbnail.</summary>
    public static readonly DependencyProperty IsVideoProperty = DependencyProperty.Register(
        nameof(IsVideo), typeof(bool), typeof(EmbedImageGrid), new PropertyMetadata(false, OnContentChanged));

    private const double ItemMaxHeight = 220;

    // Mirrors of the two properties above, read on every layout pass without a trip through WinRT.
    private IReadOnlyList<EmbedImage> _images = [];
    private bool _isVideo;
    private int _columns = 1;
    private Size _cell;

    public EmbedImageGrid()
    {
        // Out of the tree (an embed unloaded by x:Load, a page navigated away from), let go of
        // the decoded pictures now instead of whenever a full GC collects this panel. Unloaded
        // can arrive after Loaded when an element is re-parented, hence the IsLoaded check.
        Loaded += (_, _) => Update();
        Unloaded += (_, _) =>
        {
            if (!IsLoaded)
                Release();
        };
    }

    public IReadOnlyList<EmbedImage>? Images
    {
        get => (IReadOnlyList<EmbedImage>?)GetValue(ImagesProperty);
        set => SetValue(ImagesProperty, value);
    }

    public bool IsVideo
    {
        get => (bool)GetValue(IsVideoProperty);
        set => SetValue(IsVideoProperty, value);
    }

    public double MinItemWidth { get; set; } = 140;

    public double Spacing { get; set; } = 4;

    public event EventHandler<EmbedImage>? ImageTapped;

    /// <summary>Raised by a video thumbnail's pop-out button.</summary>
    public event EventHandler? PopOutRequested;

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (EmbedImageGrid)d;
        grid._images = grid.Images ?? [];
        grid._isVideo = grid.IsVideo;
        grid.Update();
    }

    private void Update()
    {
        while (Children.Count < _images.Count)
            Children.Add(CreateView());

        for (int i = 0; i < Children.Count; i++)
        {
            var view = (EmbedImageView)Children[i];
            EmbedImage? image = i < _images.Count ? _images[i] : null;
            view.IsVideo = _isVideo;
            view.Item = image;
            string? altText = _isVideo ? null : image?.AltText;
            ToolTipService.SetToolTip(view, string.IsNullOrEmpty(altText) ? null : altText);
            view.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        }

        InvalidateMeasure();
    }

    private void Release()
    {
        foreach (UIElement child in Children)
            ((EmbedImageView)child).Item = null;
    }

    private EmbedImageView CreateView()
    {
        var view = new EmbedImageView { MaxHeight = ItemMaxHeight };
        view.Tapped += View_Tapped;
        view.PopOutRequested += View_PopOutRequested;
        return view;
    }

    private void View_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (((EmbedImageView)sender).Item is { } image)
            ImageTapped?.Invoke(this, image);
    }

    private void View_PopOutRequested(object? sender, EventArgs e) =>
        PopOutRequested?.Invoke(this, EventArgs.Empty);

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = _images.Count;
        if (count == 0)
            return new Size(0, 0);

        _columns = EmbedImageLayoutPolicy.Columns(availableSize.Width, MinItemWidth, Spacing, count);
        double cellWidth = EmbedImageLayoutPolicy.CellWidth(availableSize.Width, _columns, MinItemWidth, Spacing);

        // Every cell is as tall as the first picture wants to be at that width, as in UniformGridLayout.
        UIElement first = Children[0];
        first.Measure(new Size(cellWidth, availableSize.Height));
        _cell = new Size(cellWidth, first.DesiredSize.Height);
        for (int i = 1; i < count; i++)
            Children[i].Measure(_cell);

        int rows = (count + _columns - 1) / _columns;
        return new Size(_columns * (_cell.Width + Spacing) - Spacing, rows * (_cell.Height + Spacing) - Spacing);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (int i = 0; i < _images.Count; i++)
        {
            (int row, int column) = Math.DivRem(i, _columns);
            Children[i].Arrange(new Rect(column * (_cell.Width + Spacing), row * (_cell.Height + Spacing), _cell.Width, _cell.Height));
        }

        return finalSize;
    }
}
