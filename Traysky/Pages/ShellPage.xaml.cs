using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Traysky.Models;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.System;

namespace Traysky.Pages;

/// <summary>
/// The flyout's chrome: title bar (with the notifications, refresh, settings and quit buttons),
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

    public ShellViewModel ViewModel { get; }

    public ShellPage()
    {
        InitializeComponent();
        ViewModel = new ShellViewModel();
        DataContext = ViewModel;

        BlueskySessionService.Instance.PropertyChanged += OnSessionPropertyChanged;
        FeedsService.Instance.Loaded += OnFeedsLoaded;
        ImageViewerService.ImageRequested += OnImageRequested;
        VideoViewerService.VideoRequested += OnVideoRequested;

        KeyboardAccelerators.Add(Accelerator(VirtualKey.R, VirtualKeyModifiers.Control, (_, e) => { e.Handled = true; Refresh(); }));
        KeyboardAccelerators.Add(Accelerator(VirtualKey.Number1, VirtualKeyModifiers.Control, (_, e) => { e.Handled = true; NavigateTo(ShellDestination.Home); }));
        KeyboardAccelerators.Add(Accelerator(VirtualKey.Number2, VirtualKeyModifiers.Control, (_, e) => { e.Handled = true; NavigateTo(ShellDestination.Notifications); }));
        KeyboardAccelerators.Add(Accelerator(VirtualKey.N, VirtualKeyModifiers.Control, (_, e) => { e.Handled = true; NavigateTo(ShellDestination.Compose); }));

        ActualThemeChanged += (_, _) => UpdateTitleBarIcon();
    }

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

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateTitleBarIcon();

        _navigation.Frame = ContentFrame;
        ResetNavigation();

        if (_pendingDestination is ShellDestination pending)
        {
            _pendingDestination = null;
            NavigateTo(pending);
        }
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BlueskySessionService.IsSignedIn) or nameof(BlueskySessionService.IsRestoring))
            ResetNavigation();
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
    public async void ResetNavigation()
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
        await FeedsService.Instance.LoadIfNeededAsync();
        NavigateTo(ShellDestination.Home);

        // NavigateTo(Home) is a no-op when the frame is already on TimelinePage - the common
        // case, since the popup is almost always hidden while showing Home - so
        // TimelinePage.OnNavigatedTo never fires to run its stale check. Run it here too so
        // every reopen gets one regardless of whether the frame actually navigated.
        _ = TimelineViewModel.Instance.RefreshIfStaleAsync();
    }

    public void NavigateTo(ShellDestination destination)
    {
        if (_navigation.Frame is null)
        {
            _pendingDestination = destination;
            return;
        }

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
            return;

        _suppressTabNavigation = true;
        try
        {
            Tabs.SelectedIndex = index;
        }
        finally
        {
            _suppressTabNavigation = false;
        }
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
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

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return;

        _ = NotificationPollService.Instance.PollNowAsync();

        _ = (_navigation.Frame?.Content) switch
        {
            NotificationsPage => NotificationsViewModel.Instance.RefreshAsync(),
            _ => TimelineViewModel.Instance.RefreshAsync(),
        };
    }

    private void NewPostButton_Click(object sender, RoutedEventArgs e) => NavigateTo(ShellDestination.Compose);

    private void NotificationsButton_Click(object sender, RoutedEventArgs e) => NavigateTo(ShellDestination.Notifications);

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_navigation.Frame?.Content is SettingsPage)
            return;
        _navigation.Navigate(typeof(SettingsPage));
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
        VideoPlayer.PosterSource = request.Thumbnail is null ? null : new BitmapImage(request.Thumbnail);
        VideoPlayer.Source = MediaSource.CreateFromUri(request.PlaylistUri);
        VideoOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>Swallows taps on the player itself so they toggle/move the transport controls
    /// instead of bubbling up to <see cref="VideoOverlay_Tapped"/> and dismissing the video.</summary>
    private void VideoPlayer_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void VideoOverlay_Tapped(object sender, TappedRoutedEventArgs e) => CloseVideoOverlay();

    private void VideoOverlayClose_Click(object sender, RoutedEventArgs e) => CloseVideoOverlay();

    private void CloseVideoOverlay()
    {
        VideoOverlay.Visibility = Visibility.Collapsed;
        VideoPlayer.MediaPlayer?.Pause();
        VideoPlayer.Source = null;
    }

    /// <summary>
    /// Closes the video overlay if one is open, pausing playback. Used by
    /// <see cref="Controls.TrayPopupWindow"/>'s Escape accelerator and light-dismiss handling so
    /// a playing video is always stopped before the popup itself is dismissed.
    /// </summary>
    public bool TryCloseVideoOverlay()
    {
        if (VideoOverlay.Visibility != Visibility.Visible)
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
