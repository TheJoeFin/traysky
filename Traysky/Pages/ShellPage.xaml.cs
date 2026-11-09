using Microsoft.UI.Xaml;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Numerics;
using System.Threading.Tasks;
using Traysky.Models;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.System;

namespace Traysky.Pages;

/// <summary>
/// The flyout's chrome: title bar (with the notifications, messages, profile-and-settings and quit buttons),
/// the account's feed tab row, and the frame the pages live in. One instance lives for the
/// app's life inside <see cref="Controls.TrayPopupWindow"/>.
/// </summary>
public sealed partial class ShellPage : Page
{
    private readonly NavigationService _navigation = NavigationService.Instance;
    private bool _suppressTabNavigation;

    /// <summary>The tab selected when the pointer went down, captured before a click can change
    /// selection - lets <see cref="Tabs_ItemClick"/> tell a reselect (same tab) from a switch
    /// (different tab, already handled by <see cref="Tabs_SelectionChanged"/>) regardless of
    /// which of the two events the platform happens to raise first.</summary>
    private FeedTabItem? _feedBeforePointerDown;

    /// <summary>A destination asked for before the frame existed (first show), applied on Loaded.</summary>
    private ShellDestination? _pendingDestination;
    private Action? _pendingThen;

    /// <summary>Counts <see cref="NavigateTo"/> calls, so <see cref="ResetNavigationAsync"/> can tell it was overtaken.</summary>
    private int _navigationRequests;

    /// <summary>The video playing in the overlay, so it can be handed to the pop-out window.</summary>
    private VideoViewerRequest? _currentVideo;

    public ShellViewModel ViewModel { get; }

    /// <summary>The bar the window adopts as its title bar when the shell is a standard window.</summary>
    public UIElement TitleBarElement => SimpleTitleBar;

    /// <summary>Raised when the title bar's "return to flyout" button is pressed (standard window only).</summary>
    public event EventHandler? ReturnToFlyoutRequested;

    /// <summary>
    /// In the standard window the title bar gains a return-to-flyout button and Quit takes the
    /// caption buttons' shape, so the pair reads as part of the window controls beside them.
    /// </summary>
    public void SetWindowMode(bool window)
    {
        _isWindowMode = window;
        ReturnToFlyoutButton.Visibility = window ? Visibility.Visible : Visibility.Collapsed;
        QuitButton.Style = (Style)(window
            ? Resources["CaptionLikeButtonStyle"]
            : Application.Current.Resources["TitleBarButtonStyle"]);
    }

    /// <summary>Raised when the flyout's title bar (or its icon) is double-tapped: the user wants the standard window.</summary>
    public event EventHandler? ExpandRequested;

    private bool _isWindowMode;

    private void TitleBar_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // As a window the caption belongs to the system (double-click maximizes); only the flyout expands.
        if (_isWindowMode)
            return;

        // Double-tapping one of the header buttons is not a double-tap on the bar.
        for (DependencyObject? node = e.OriginalSource as DependencyObject; node is not null && node != sender; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
                return;
        }

        e.Handled = true;
        ExpandRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ReturnToFlyoutButton_Click(object sender, RoutedEventArgs e) =>
        ReturnToFlyoutRequested?.Invoke(this, EventArgs.Empty);

    public ShellPage()
    {
        InitializeComponent();
        ViewModel = new ShellViewModel();
        DataContext = ViewModel;
        ConfigureHomeButtonAnimations();

        BlueskySessionService.Instance.PropertyChanged += OnSessionPropertyChanged;
        FeedsService.Instance.Loaded += OnFeedsLoaded;
        ImageViewerService.ImageRequested += OnImageRequested;
        VideoViewerService.VideoRequested += OnVideoRequested;

        KeyboardAccelerators.Add(Accelerator(VirtualKey.R, VirtualKeyModifiers.Control, (sender, e) => { e.Handled = true; _ = RefreshAsync(); }));
        KeyboardAccelerators.Add(Accelerator(VirtualKey.Number1, VirtualKeyModifiers.Control, (_, e) => { e.Handled = true; NavigateTo(ShellDestination.Home); }));
        KeyboardAccelerators.Add(Accelerator(VirtualKey.Number2, VirtualKeyModifiers.Control, (_, e) => { e.Handled = true; NavigateTo(ShellDestination.Notifications); }));
        KeyboardAccelerators.Add(Accelerator(VirtualKey.Number3, VirtualKeyModifiers.Control, (_, e) => { e.Handled = true; NavigateTo(ShellDestination.Messages); }));
        KeyboardAccelerators.Add(Accelerator(VirtualKey.N, VirtualKeyModifiers.Control, (_, e) => { e.Handled = true; NavigateTo(ShellDestination.Compose); }));

        // ListViewItem marks PointerPressed handled, so a plain XAML hookup never fires.
        Tabs.AddHandler(PointerPressedEvent, new PointerEventHandler(Tabs_PointerPressed), handledEventsToo: true);

        ActualThemeChanged += (_, _) => UpdateTitleBarIcon();

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.ContentMaxWidth))
                ContentColumn.MaxWidth = ViewModel.ContentMaxWidth;
        };
        _navigation.NavigationChanged += (_, _) => UpdateRailSelection();
    }

    /// <summary>Re-evaluates the breakpoint whenever the (resizable) flyout changes width.</summary>
    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => ViewModel.ApplyWidth(e.NewSize.Width);

    /// <summary>Marks the rail button for the page being shown, like bsky.app's bold current item.</summary>
    private void UpdateRailSelection()
    {
        object? page = ContentFrame.Content;
        Button? active = page switch
        {
            TimelinePage => RailHomeButton,
            ExplorePage or SearchPage => RailExploreButton,
            NotificationsPage => RailNotificationsButton,
            MessagesPage or ConversationPage => RailMessagesButton,
            ProfilePage { ViewModel.IsSelf: true } => RailProfileButton,
            SettingsPage => RailSettingsButton,
            _ => null,
        };

        foreach (Button button in new[] { RailHomeButton, RailExploreButton, RailNotificationsButton, RailMessagesButton, RailProfileButton, RailSettingsButton })
        {
            bool isActive = button == active;
            button.FontWeight = isActive ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
            if (isActive)
                button.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
            else
                button.ClearValue(Control.ForegroundProperty);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_navigation.Frame?.Content is not SettingsPage)
            NavigateTo(ShellDestination.Settings);
    }

    private void SidebarSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (SearchQueryPolicy.Normalize(args.QueryText).Length == 0)
            return;

        SearchPage.Open(args.QueryText);
        sender.Text = string.Empty;
    }

    /// <summary>The sidebar list is the same feeds as the tab row: picking one selects that tab.</summary>
    private void SidebarFeeds_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not FeedTabItem feed)
            return;

        if (!feed.Equals(Tabs.SelectedItem))
        {
            Tabs.SelectedItem = feed; // Tabs_SelectionChanged does the rest
            return;
        }

        // Already the selected feed: from another page this goes back to it, on the timeline it refreshes.
        if (_navigation.Frame?.Content is TimelinePage timeline)
        {
            timeline.ScrollToTop();
            _ = TimelineViewModel.Instance.RefreshAsync();
        }
        else
        {
            NavigateTo(ShellDestination.Home);
        }
    }

    private void SyncSidebarSelection() => SidebarFeeds.SelectedItem = Tabs.SelectedItem;

    /// <summary>
    /// The title bar icon needs the opposite tone from the app's theme to stay visible against
    /// the title bar background (light chrome in light theme, dark chrome in dark theme).
    /// </summary>
    private void UpdateTitleBarIcon()
    {
        string asset = ActualTheme == ElementTheme.Dark ? "Wings-Light" : "Wings-Dark";
        SimpleTitleBar.IconSource = new ImageIconSource
        {
            ImageSource = new BitmapImage(new Uri($"ms-appx:///Assets/{asset}.png")),
        };
    }

    private static KeyboardAccelerator Accelerator(VirtualKey key, VirtualKeyModifiers modifiers, TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
    {
        KeyboardAccelerator accelerator = new() { Key = key, Modifiers = modifiers };
        accelerator.Invoked += handler;
        return accelerator;
    }

    /// <summary>
    /// Pops the Home button in and out when its Visibility flips: a springy scale-up on show,
    /// a quick scale-down on hide. Composition keeps the element alive until the hide finishes.
    /// </summary>
    private void ConfigureHomeButtonAnimations()
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(HomeButton);
        Compositor compositor = visual.Compositor;

        // Scale about the button's centre, not its top-left corner.
        HomeButton.SizeChanged += (_, e) =>
            visual.CenterPoint = new Vector3((float)e.NewSize.Width / 2, (float)e.NewSize.Height / 2, 0);

        Vector3KeyFrameAnimation scaleIn = compositor.CreateVector3KeyFrameAnimation();
        scaleIn.Target = nameof(Visual.Scale);
        scaleIn.InsertKeyFrame(0f, Vector3.Zero);
        scaleIn.InsertKeyFrame(1f, Vector3.One, compositor.CreateCubicBezierEasingFunction(new Vector2(0.34f, 1.56f), new Vector2(0.64f, 1f)));
        scaleIn.Duration = TimeSpan.FromMilliseconds(300);

        ScalarKeyFrameAnimation fadeIn = compositor.CreateScalarKeyFrameAnimation();
        fadeIn.Target = nameof(Visual.Opacity);
        fadeIn.InsertKeyFrame(0f, 0f);
        fadeIn.InsertKeyFrame(1f, 1f);
        fadeIn.Duration = TimeSpan.FromMilliseconds(150);

        Vector3KeyFrameAnimation scaleOut = compositor.CreateVector3KeyFrameAnimation();
        scaleOut.Target = nameof(Visual.Scale);
        scaleOut.InsertKeyFrame(1f, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(new Vector2(0.36f, 0f), new Vector2(0.66f, -0.56f)));
        scaleOut.Duration = TimeSpan.FromMilliseconds(200);

        ScalarKeyFrameAnimation fadeOut = compositor.CreateScalarKeyFrameAnimation();
        fadeOut.Target = nameof(Visual.Opacity);
        fadeOut.InsertKeyFrame(1f, 0f);
        fadeOut.Duration = TimeSpan.FromMilliseconds(200);

        CompositionAnimationGroup show = compositor.CreateAnimationGroup();
        show.Add(scaleIn);
        show.Add(fadeIn);
        CompositionAnimationGroup hide = compositor.CreateAnimationGroup();
        hide.Add(scaleOut);
        hide.Add(fadeOut);

        ElementCompositionPreview.SetImplicitShowAnimation(HomeButton, show);
        ElementCompositionPreview.SetImplicitHideAnimation(HomeButton, hide);
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateTitleBarIcon();

        _navigation.Frame = ContentFrame;
        _ = ResetNavigationAsync();

        if (_pendingDestination is ShellDestination pending)
        {
            Action? then = _pendingThen;
            _pendingDestination = null;
            _pendingThen = null;
            NavigateTo(pending, then);
        }
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BlueskySessionService.IsSignedIn) or nameof(BlueskySessionService.IsRestoring))
            _ = ResetNavigationAsync();
    }

    /// <summary>
    /// Once the account's real pinned feeds arrive, selects the tab remembered from last time
    /// (or the first one, on a fresh sign-in) and points the timeline at it. Runs whichever page
    /// is showing - it only touches the tab strip and the timeline's own state, not navigation.
    /// </summary>
    private void OnFeedsLoaded(object? sender, EventArgs e)
    {
        ObservableCollection<FeedTabItem> feeds = FeedsService.Instance.Tabs;
        if (feeds.Count == 0)
            return;

        string lastKey = SettingsService.LastFeedKey;
        int index = 0;
        for (int i = 0; i < feeds.Count; i++)
        {
            if (feeds[i].Key == lastKey)
            {
                index = i;
                break;
            }
        }

        SelectTab(index);
        TimelineViewModel.Instance.SelectFeed(feeds[index].Kind == FeedTabKind.Timeline ? null : feeds[index]);
    }

    /// <summary>
    /// Puts the shell back at its root with an empty back stack: the login page while signed
    /// out, otherwise the timeline, on whatever feed was last selected. Called whenever the
    /// popup is shown, so it never reopens deep in Settings or Notifications.
    /// </summary>
    public async Task ResetNavigationAsync()
    {
        CloseImageOverlay();
        CloseVideoOverlay();

        if (_navigation.Frame is null)
            return;

        BlueskySessionService session = BlueskySessionService.Instance;

        if (session.IsRestoring)
        {
            _navigation.ResetTo(typeof(RestoringPage));
            return;
        }

        if (!session.IsSignedIn)
        {
            _navigation.ResetTo(typeof(LoginPage));
            return;
        }

        // Runs on the sign-in transition *and* on every later reopen (ResetNavigation is called
        // both ways) - the transition alone isn't enough, because a restored session flips
        // IsSignedIn during app startup, before the popup (and this page) exist to hear it.
        //
        // Awaited (rather than fire-and-forget) so the last-selected tab is chosen - via
        // OnFeedsLoaded, which this triggers - before the timeline navigation below can kick
        // off its own load. Otherwise the timeline would start fetching the main feed first,
        // and that fetch would land *after* OnFeedsLoaded had already switched feeds, clobbering
        // the correct content with the wrong feed's posts. Already-loaded reopens resolve this
        // synchronously, so normal reopen timing is unaffected.
        //
        // If something navigated while the feeds were loading (a deep link or share opening
        // compose on a cold start), that request is newer than this reset and must win.
        int requestsBefore = _navigationRequests;
        await FeedsService.Instance.LoadIfNeededAsync();
        if (_navigationRequests == requestsBefore)
            NavigateTo(ShellDestination.Home);

        // Same reason as the feeds above: a restored session signed in before this page could
        // hear it. Also keeps the Messages badge fresh on every reopen.
        _ = ChatService.Instance.CheckAsync();

        // NavigateTo(Home) is a no-op when the frame is already on TimelinePage - the common
        // case, since the popup is almost always hidden while showing Home - so
        // TimelinePage.OnNavigatedTo never fires to run its stale check. Run it here too so
        // every reopen gets one regardless of whether the frame actually navigated.
        _ = TimelineViewModel.Instance.RefreshIfStaleAsync();
    }

    /// <param name="then">
    /// Runs once the destination is showing, for a deep link that goes on to push a profile or
    /// post. Skipped when signed out, since there is nothing to push onto the login page.
    /// </param>
    public void NavigateTo(ShellDestination destination, Action? then = null)
    {
        if (_navigation.Frame is null)
        {
            _pendingDestination = destination;
            _pendingThen = then;
            return;
        }

        _navigationRequests++;

        if (!BlueskySessionService.Instance.IsSignedIn)
        {
            if (destination == ShellDestination.Settings)
                _navigation.Navigate(typeof(SettingsPage));
            return;
        }

        switch (destination)
        {
            case ShellDestination.Settings:
                _navigation.Navigate(typeof(SettingsPage));
                break;

            case ShellDestination.Notifications:
                // Pushed, not reset: notifications is a page you visit and back out of, like
                // Settings, not a feed tab - so it gets a back button and hides the feed row.
                _navigation.Navigate(typeof(NotificationsPage));
                break;

            case ShellDestination.Messages:
                // Pushed like Notifications. Without DM access there is nothing to show.
                if (ChatService.Instance.CanUseChat)
                    _navigation.Navigate(typeof(MessagesPage));
                break;

            case ShellDestination.Compose:
                ComposeViewModel.Instance.BeginNew();
                _navigation.Navigate(typeof(ComposePage));
                break;

            case ShellDestination.Home:
            case ShellDestination.Current:
            default:
                EnsureFeedSelected();
                _navigation.ResetTo(typeof(TimelinePage));
                break;
        }

        then?.Invoke();
    }

    /// <summary>Makes sure some feed tab is selected before the timeline is shown, for the moment
    /// on a fresh sign-in before <see cref="FeedsService.Instance"/>'s real preferences arrive.</summary>
    private void EnsureFeedSelected()
    {
        if (Tabs.SelectedIndex < 0 && FeedsService.Instance.Tabs.Count > 0)
            SelectTab(0);
    }

    private void SelectTab(int index)
    {
        if (Tabs.SelectedIndex == index)
        {
            SyncSidebarSelection();
            return;
        }

        _suppressTabNavigation = true;
        try
        {
            Tabs.SelectedIndex = index;
        }
        finally
        {
            _suppressTabNavigation = false;
        }

        SyncSidebarSelection();
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncSidebarSelection();

        if (_suppressTabNavigation || Tabs.SelectedItem is not FeedTabItem feed)
            return;

        TimelineViewModel.Instance.SelectFeed(feed.Kind == FeedTabKind.Timeline ? null : feed);
        SettingsService.LastFeedKey = feed.Key;
        NavigateTo(ShellDestination.Home);
    }

    private void Tabs_PointerPressed(object sender, PointerRoutedEventArgs e) => _feedBeforePointerDown = Tabs.SelectedItem as FeedTabItem;

    /// <summary>
    /// Clicking a feed tab always raises this, whether or not selection changed. A different tab
    /// is already handled by <see cref="Tabs_SelectionChanged"/>; here we only care about the
    /// already-selected tab being clicked again, which resets it to the top and refreshes.
    /// </summary>
    private void Tabs_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not FeedTabItem feed || !feed.Equals(_feedBeforePointerDown))
            return;

        if (_navigation.Frame?.Content is TimelinePage timeline)
            timeline.ScrollToTop();

        _ = TimelineViewModel.Instance.RefreshAsync();
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        _navigation.GoBack();
    }

    private async Task RefreshAsync()
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return;

        // After sleep the access token may have lapsed; renew it once up front rather than
        // letting every refresh below fail with AuthenticationRequiredException.
        if (!await BlueskySessionService.Instance.EnsureAuthenticatedAsync())
            return;

        _ = NotificationPollService.Instance.PollNowAsync();
        _ = ChatService.Instance.CheckAsync(force: true);

        _ = (_navigation.Frame?.Content) switch
        {
            NotificationsPage => NotificationsViewModel.Instance.RefreshAsync(),
            MessagesPage => MessagesViewModel.Instance.RefreshAsync(),
            ConversationPage conversation => conversation.ViewModel.PollAsync(),
            _ => TimelineViewModel.Instance.RefreshAsync(),
        };
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => NavigateTo(ShellDestination.Home);

    private void NewPostButton_Click(object sender, RoutedEventArgs e) => NavigateTo(ShellDestination.Compose);

    private void ExploreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_navigation.Frame?.Content is not ExplorePage)
            ExplorePage.Open();
    }

    private void NotificationsButton_Click(object sender, RoutedEventArgs e) => NavigateTo(ShellDestination.Notifications);

    private void MessagesButton_Click(object sender, RoutedEventArgs e) => NavigateTo(ShellDestination.Messages);

    /// <summary>
    /// Opens your own profile, which carries the Settings button. Signed out there is no
    /// profile, so it goes straight to Settings instead.
    /// </summary>
    private void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        BlueskySessionService session = BlueskySessionService.Instance;
        if (!session.IsSignedIn)
        {
            if (_navigation.Frame?.Content is not SettingsPage)
                _navigation.Navigate(typeof(SettingsPage));
            return;
        }

        if (_navigation.Frame?.Content is ProfilePage { ViewModel.IsSelf: true })
            return;
        ProfilePage.Open(session.Did ?? session.Handle ?? string.Empty);
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        App.Current.ShutdownAndExit();
    }

    private void OnImageRequested(object? sender, Uri imageUri)
    {
        OverlayImage.Source = new BitmapImage(imageUri);
        ImageOverlay.Visibility = Visibility.Visible;

        // A fresh image starts fit-to-window, not wherever the last one was left zoomed/panned.
        OverlayScrollViewer.ChangeView(0, 0, 1, disableAnimation: true);
    }

    /// <summary>Keeps the image sized to the viewport so Stretch="Uniform" fits it at zoom 1;
    /// left unconstrained, the ScrollViewer would size it to its native pixel dimensions.</summary>
    private void OverlayScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        OverlayImage.Width = e.NewSize.Width;
        OverlayImage.Height = e.NewSize.Height;
    }

    private void OverlayImage_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        float targetZoom = OverlayScrollViewer.ZoomFactor > 1 ? 1 : 2.5f;
        OverlayScrollViewer.ChangeView(0, 0, targetZoom);
    }

    private void ImageOverlay_Tapped(object sender, TappedRoutedEventArgs e) => CloseImageOverlay();

    private void ImageOverlayClose_Click(object sender, RoutedEventArgs e) => CloseImageOverlay();

    private void CloseImageOverlay()
    {
        ImageOverlay.Visibility = Visibility.Collapsed;
        OverlayImage.Source = null;
    }

    /// <summary>
    /// Closes the image overlay if one is open. Used by <see cref="Controls.TrayPopupWindow"/>'s
    /// Escape accelerator so Escape dismisses the image first and only falls through to hiding
    /// the whole popup once no image is showing.
    /// </summary>
    public bool TryCloseImageOverlay()
    {
        if (ImageOverlay.Visibility != Visibility.Visible)
            return false;

        CloseImageOverlay();
        return true;
    }

    private void OnVideoRequested(object? sender, VideoViewerRequest request)
    {
        // x:Load="False" until a video is actually opened: a MediaPlayerElement pulls in the
        // whole Media Foundation playback stack, which most sessions never need.
        if (VideoOverlay is null)
            FindName(nameof(VideoOverlay));

        _currentVideo = request;
        VideoPlayer.PosterSource = request.Thumbnail is null ? null : new BitmapImage(request.Thumbnail);
        VideoPlayer.Source = MediaSource.CreateFromUri(request.PlaylistUri);
        VideoOverlay!.Visibility = Visibility.Visible;
    }

    /// <summary>Swallows taps on the player itself so they toggle/move the transport controls
    /// instead of bubbling up to <see cref="VideoOverlay_Tapped"/> and dismissing the video.</summary>
    private void VideoPlayer_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void VideoOverlay_Tapped(object sender, TappedRoutedEventArgs e) => CloseVideoOverlay();

    private void VideoOverlayClose_Click(object sender, RoutedEventArgs e) => CloseVideoOverlay();

    /// <summary>Hands the video to the pop-out window, carrying on from where it had got to.</summary>
    private void VideoOverlayPopOut_Click(object sender, RoutedEventArgs e)
    {
        if (_currentVideo is not { } video)
            return;

        TimeSpan position = VideoPlayer.MediaPlayer?.PlaybackSession.Position ?? TimeSpan.Zero;
        CloseVideoOverlay();
        VideoViewerService.PopOut(video, position);
    }

    private void CloseVideoOverlay()
    {
        if (VideoOverlay is null)
            return;

        VideoPlayer.MediaPlayer?.Pause();
        VideoPlayer.Source = null;
        _currentVideo = null;

        // Tear the player down rather than just hiding it, so its MediaPlayer and decoder
        // resources go with it; the next video realizes a fresh one.
        UnloadObject(VideoOverlay);
    }

    /// <summary>
    /// Closes the video overlay if one is open, pausing playback. Used by
    /// <see cref="Controls.TrayPopupWindow"/>'s Escape accelerator and light-dismiss handling so
    /// a playing video is always stopped before the popup itself is dismissed.
    /// </summary>
    public bool TryCloseVideoOverlay()
    {
        if (VideoOverlay is null)
            return false;

        CloseVideoOverlay();
        return true;
    }

    /// <summary>
    /// Forces the title bar back to its activated (full colour) visuals. A window hidden
    /// while deactivated and shown again can come back without a fresh activation, leaving
    /// the control dimmed. Ported from Traydio.
    /// </summary>
    public void RefreshTitleBarActivation()
    {
        VisualStateManager.GoToState(SimpleTitleBar, ViewModel.CanGoBack ? "BackButtonVisible" : "BackButtonCollapsed", false);
        VisualStateManager.GoToState(SimpleTitleBar, "IconVisible", false);
        VisualStateManager.GoToState(SimpleTitleBar, "TitleTextVisible", false);
    }
}
