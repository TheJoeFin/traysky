using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>
/// The Notifications tab. Marks everything seen once the page has been on screen for a
/// moment, so a stray click through the tab does not silently clear the badge.
/// </summary>
public sealed partial class NotificationsPage : Page
{
    private static readonly TimeSpan SeenDwell = TimeSpan.FromMilliseconds(1500);

    private readonly DispatcherQueueTimer _seenTimer;
    private ScrollViewer? _scrollViewer;
    private NotificationItem? _avatarPressedItem;

    public NotificationsViewModel ViewModel { get; } = NotificationsViewModel.Instance;

    public NotificationsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        _seenTimer = DispatcherQueue.CreateTimer();
        _seenTimer.Interval = SeenDwell;
        _seenTimer.IsRepeating = false;
        _seenTimer.Tick += (_, _) => _ = ViewModel.MarkSeenAsync();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshIfStaleAsync();
        _seenTimer.Stop();
        _seenTimer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _seenTimer.Stop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer is not null)
            return;

        _scrollViewer = FindScrollViewer(NotificationList);
        if (_scrollViewer is not null)
            _scrollViewer.ViewChanged += OnViewChanged;
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scrollViewer is null || e.IsIntermediate)
            return;

        double remaining = _scrollViewer.ScrollableHeight - _scrollViewer.VerticalOffset;
        if (remaining < _scrollViewer.ViewportHeight * 2 && ViewModel.HasMore)
            _ = ViewModel.LoadMoreAsync();
    }

    /// <summary>
    /// ListView raises ItemClick for a click anywhere in the row, and a child marking Tapped
    /// handled doesn't stop it - so the avatar just notes which row it was pressed on, and
    /// <see cref="NotificationList_ItemClick"/> opens that author's profile instead of the post.
    /// </summary>
    private void Avatar_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        _avatarPressedItem = (sender as FrameworkElement)?.DataContext as NotificationItem;

    private void NotificationList_ItemClick(object sender, ItemClickEventArgs e)
    {
        bool onAvatar = ReferenceEquals(e.ClickedItem, _avatarPressedItem);
        _avatarPressedItem = null;

        if (e.ClickedItem is not NotificationItem item)
            return;

        if (onAvatar)
        {
            ProfilePage.Open(item.AuthorDid.Length > 0 ? item.AuthorDid : item.AuthorHandle);
            return;
        }

        // Everything opens in-app: a follow shows the follower's profile, everything else the
        // post - hydrated if we have it, fetched by AT URI if not.
        if (item.Kind == NotificationKind.Follow)
        {
            ProfilePage.Open(item.AuthorDid.Length > 0 ? item.AuthorDid : item.AuthorHandle);
            return;
        }

        if (item.SubjectPost is not null)
            PostPage.Open(item.SubjectPost, item.ContextForPostPage);
        else if (item.SubjectAtUri is not null)
            PostPage.Open(item.SubjectAtUri, item.ContextForPostPage);
        else
        {
            // The subject post could not be identified (e.g. deleted) - falls back to the
            // author's profile rather than a dead end.
            LogService.Warn("Notifications", $"No subject post for {item.Kind} - opening profile instead. Headline='{item.Headline}' AuthorHandle='{item.AuthorHandle}'");
            ProfilePage.Open(item.AuthorDid.Length > 0 ? item.AuthorDid : item.AuthorHandle);
        }
    }

    private void Reply_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is NotificationItem { Post: { } post })
        {
            // The subject post isn't shown anywhere on screen here (no card to pop an inline
            // reply box under - see PostCard), so this one goes through the standalone page.
            ComposeViewModel.Instance.BeginReply(post);
            NavigationService.Instance.Navigate(typeof(ComposePage));
        }
    }

    private void Like_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is NotificationItem { Post: { } post })
            _ = PostInteractions.ToggleLikeAsync(post);
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
