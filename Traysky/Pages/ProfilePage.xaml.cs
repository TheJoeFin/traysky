using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>Navigation parameter that puts <see cref="ProfilePage"/> in its contained mode, for <paramref name="Actor"/> (a DID or handle).</summary>
public sealed record ContainedProfileRequest(string Actor);

/// <summary>
/// An account's profile and recent posts. Navigation parameter is a DID or handle.
/// </summary>
public sealed partial class ProfilePage : Page
{
    /// <summary>
    /// True when the page is hosted in its own window's frame (see
    /// <see cref="Controls.ProfilePreviewWindow"/>) rather than the tray popup's. Set by
    /// navigating with a <see cref="ContainedProfileRequest"/>. A contained page must never reach
    /// the popup's <see cref="NavigationService"/>, the shared compose draft or the popup's
    /// overlays, so its post cards are contained too (see <see cref="Controls.PostCard.IsContained"/>),
    /// it hides the actions that would (messages, lists, settings, thread and follower pages,
    /// starter packs) and sends the rest to bsky.app or to its own frame.
    /// </summary>
    public static readonly DependencyProperty IsContainedProperty =
        DependencyProperty.Register(nameof(IsContained), typeof(bool), typeof(ProfilePage), new PropertyMetadata(false));

    public bool IsContained
    {
        get => (bool)GetValue(IsContainedProperty);
        private set => SetValue(IsContainedProperty, value);
    }

    /// <summary>x:Bind can't nest function calls, so this folds "show unless contained" into one.</summary>
    public Visibility VisibleUnlessContained(bool value, bool contained) =>
        value && !contained ? Visibility.Visible : Visibility.Collapsed;

    private ScrollViewer? _scrollViewer;

    public ProfileViewModel ViewModel { get; } = new();

    public ProfilePage()
    {
        ViewModel.Dialogs = new XamlDialogService(() => XamlRoot);
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private ProfileTab SelectedBarTab =>
        Tabs.SelectedItem == LikesTab ? ProfileTab.Likes
        : Tabs.SelectedItem == FeedsTab ? ProfileTab.Feeds
        : Tabs.SelectedItem == PacksTab ? ProfileTab.StarterPacks
        : Tabs.SelectedItem == ListsTab ? ProfileTab.Lists
        : ProfileTab.Posts;

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // IsSelected in XAML can raise SelectionChanged mid-InitializeComponent, before the list exists.
        if (PostList is null)
            return;

        _ = ViewModel.SelectTabAsync(SelectedBarTab);
    }

    /// <summary>Ticks only while a live status is on screen, to update "time left" and drop it at expiry.</summary>
    private DispatcherTimer? _liveTimer;

    private void UpdateLiveTimer()
    {
        if (ViewModel.IsLive && IsLoaded)
        {
            if (_liveTimer is not null)
                return;

            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _liveTimer.Tick += (_, _) => ViewModel.RefreshLive();
            _liveTimer.Start();
        }
        else if (_liveTimer is not null)
        {
            _liveTimer.Stop();
            _liveTimer = null;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => UpdateLiveTimer();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileViewModel.IsLive))
        {
            UpdateLiveTimer();
            return;
        }

        if (e.PropertyName != nameof(ProfileViewModel.SelectedTab))
            return;

        // Swapping the source resets the scroll position; put it back so the tab bar stays where it was.
        double offset = _scrollViewer?.VerticalOffset ?? 0;
        PostList.ItemsSource = ViewModel.ItemsFor(ViewModel.SelectedTab);
        if (_scrollViewer is not null && offset > 0)
        {
            PostList.UpdateLayout();
            _scrollViewer.ChangeView(null, offset, null, disableAnimation: true);
        }

        // The view model may have moved off a tab the profile doesn't offer (Likes on someone else's).
        if (SelectedBarTab != ViewModel.SelectedTab)
        {
            Tabs.SelectedItem = ViewModel.SelectedTab switch
            {
                ProfileTab.Likes => LikesTab,
                ProfileTab.Feeds => FeedsTab,
                ProfileTab.StarterPacks => PacksTab,
                ProfileTab.Lists => ListsTab,
                _ => PostsTab
            };
        }
    }

    private void FeedRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FeedSuggestionItem feed })
            _ = RichTextBuilder.OpenAsync(feed.WebUrl);
    }

    // The button sits inside a row that opens the feed when tapped; pressing Pin shouldn't also do that.
    private void PinFeed_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    // Row templates can't reach the page's view model with x:Bind, so this only forwards the row.
    private void PinFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FeedSuggestionItem feed })
            ViewModel.PinFeedCommand.Execute(feed);
    }

    private void PackRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StarterPackItem pack })
            StarterPackPage.Open(pack);
    }

    private void ListRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ListItem list })
            ListPage.Open(list);
    }

    private void ContainedPost_ProfileRequested(object? sender, string did) =>
        Frame?.Navigate(typeof(ProfilePage), new ContainedProfileRequest(did));

    private void SimilarRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ActorItem actor })
            return;

        // A contained page keeps going inside its own frame, so its window gets a back stack.
        if (IsContained)
            Frame?.Navigate(typeof(ProfilePage), new ContainedProfileRequest(actor.ProfileKey.TrimStart('@')));
        else
            Open(actor.ProfileKey);
    }

    private void KnownFollowers_Click(object sender, RoutedEventArgs e) =>
        FollowListPage.OpenKnownFollowers(ViewModel.ActorKey, ViewModel.KnownFollowersCount);

    private void SimilarExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args) => _ = ViewModel.LoadSimilarAsync();

    /// <summary>Navigates to a profile. Shared by avatars, "View profile", mentions and follow notifications.</summary>
    public static void Open(string didOrHandle)
    {
        if (string.IsNullOrWhiteSpace(didOrHandle))
            return;
        NavigationService.Instance.Navigate(typeof(ProfilePage), didOrHandle.Trim().TrimStart('@'));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ContainedProfileRequest request)
        {
            IsContained = true;

            // Before anything loads, so no row is ever realized as a full PostCard.
            ((Helpers.ProfileRowTemplateSelector)Resources["RowSelector"]).PostTemplate = (DataTemplate)Resources["ContainedPostRowTemplate"];
            PostList.ItemTemplateSelector = (Helpers.ProfileRowTemplateSelector)Resources["RowSelector"];
            _ = ViewModel.LoadAsync(request.Actor);
        }
        else if (e.Parameter is string actor)
        {
            _ = ViewModel.LoadAsync(actor);
        }
    }

    public bool AnyLoading(bool a, bool b) => a || b;

    public Style FollowButtonStyle(bool following) =>
        (Style)Application.Current.Resources[following ? "DefaultButtonStyle" : "AccentButtonStyle"];

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => NavigationService.Instance.Navigate(typeof(SettingsPage));

    private void Bell_Opening(object? sender, object e)
    {
        BellOffItem.IsChecked = ViewModel.ActivityLevel == ActivitySubscriptionLevel.Off;
        BellPostsItem.IsChecked = ViewModel.ActivityLevel == ActivitySubscriptionLevel.Posts;
        BellRepliesItem.IsChecked = ViewModel.ActivityLevel == ActivitySubscriptionLevel.PostsAndReplies;
    }

    private void BookmarksButton_Click(object sender, RoutedEventArgs e) => BookmarksPage.Open();

    private void FollowersButton_Click(object sender, RoutedEventArgs e) => OpenFollowList(showFollowing: false);

    private void FollowingButton_Click(object sender, RoutedEventArgs e) => OpenFollowList(showFollowing: true);

    private void OpenFollowList(bool showFollowing)
    {
        if (IsContained)
        {
            _ = RichTextBuilder.OpenAsync($"https://bsky.app/profile/{ViewModel.ActorKey}/{(showFollowing ? "follows" : "followers")}");
            return;
        }

        FollowListPage.Open(ViewModel.ActorKey, showFollowing, ViewModel.FollowersCount, ViewModel.FollowsCount);
    }

    private void PostCard_ThreadRequested(object? sender, PostItem post) => PostPage.Open(post);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLiveTimer();
        if (_scrollViewer is not null)
            return;

        _scrollViewer = FindScrollViewer(PostList);
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
