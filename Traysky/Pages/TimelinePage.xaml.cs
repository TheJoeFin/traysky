using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Traysky.ViewModels;

namespace Traysky.Pages;

/// <summary>
/// Home: the timeline. Cached, so switching tabs keeps the scroll position; the shell
/// re-navigates here on every popup open, which is what triggers the stale check. A reply shows
/// inline under the post it's replying to (see PostCard); a new post or a quote goes to
/// <see cref="ComposePage"/> instead.
/// </summary>
public sealed partial class TimelinePage : Page
{
    private ScrollViewer? _scrollViewer;

    public TimelineViewModel ViewModel { get; } = TimelineViewModel.Instance;

    public TimelinePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshIfStaleAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer is not null)
            return;

        _scrollViewer = FindScrollViewer(PostList);
        if (_scrollViewer is not null)
            _scrollViewer.ViewChanged += OnViewChanged;
    }

    private void PostCard_ThreadRequested(object? sender, ViewModels.Items.PostItem post)
    {
        PostPage.Open(post);
    }

    /// <summary>Jumps back to the newest post. Used when the shell's feed row re-clicks the
    /// already-selected tab, alongside a refresh.</summary>
    public void ScrollToTop() => _scrollViewer?.ChangeView(null, 0, null, disableAnimation: true);

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scrollViewer is null || e.IsIntermediate)
            return;

        // Within two screens of the bottom: fetch the next page.
        double remaining = _scrollViewer.ScrollableHeight - _scrollViewer.VerticalOffset;
        if (remaining < _scrollViewer.ViewportHeight * 2 && ViewModel.HasMore)
            _ = ViewModel.LoadMoreAsync();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
            return sv;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            ScrollViewer? found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }
        return null;
    }
}
