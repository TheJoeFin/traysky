using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Traysky.Pages;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// State the shell chrome binds to: back button, feed tab strip, and the unread badge.
/// Navigation itself lives in <see cref="NavigationService"/>; the page drives it.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly BlueskySessionService _session = BlueskySessionService.Instance;
    private readonly NotificationPollService _poll = NotificationPollService.Instance;
    private readonly FeedsService _feeds = FeedsService.Instance;
    private readonly ChatService _chat = ChatService.Instance;

    public ShellViewModel() : this(NavigationService.Instance)
    {
    }

    public ShellViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        _navigation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(INavigationService.CanGoBack))
            {
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(ShowTabs));
            }
        };
        _navigation.NavigationChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowTabs));
            OnPropertyChanged(nameof(ShowHome));
        };
        _session.PropertyChanged += OnSessionChanged;
        _poll.PropertyChanged += OnPollChanged;
        _chat.PropertyChanged += OnChatChanged;
    }

    public bool CanGoBack => _navigation.CanGoBack;

    public bool IsSignedIn => _session.IsSignedIn;

    private ShellLayoutKind _layout = ShellLayoutKind.Compact;

    /// <summary>Which breakpoint the flyout's current width falls in.</summary>
    public ShellLayoutKind Layout => _layout;

    /// <summary>Feeds the page's width in; raises the layout-derived properties only when a breakpoint is crossed.</summary>
    public void ApplyWidth(double width)
    {
        ShellLayoutKind kind = ShellLayoutPolicy.ForWidth(width);
        if (kind == _layout)
            return;

        _layout = kind;
        OnPropertyChanged(nameof(Layout));
        OnLayoutDependentChanged();
    }

    /// <summary>The left nav rail. Needs an account: signed out, the title bar's Settings button is all there is to show.</summary>
    public bool ShowRail => _session.IsSignedIn && ShellLayoutPolicy.ShowsRail(_layout);

    /// <summary>The title bar's nav buttons, which the rail replaces.</summary>
    public bool ShowHeaderNav => !ShowRail;

    public bool ShowRailLabels => ShellLayoutPolicy.RailShowsLabels(_layout);

    /// <summary>The right column (search and feed list).</summary>
    public bool ShowSidebar => _session.IsSignedIn && ShellLayoutPolicy.ShowsSidebar(_layout);

    public double ContentMaxWidth => ShellLayoutPolicy.MaxContentWidth(_layout);

    private void OnLayoutDependentChanged()
    {
        OnPropertyChanged(nameof(ShowRail));
        OnPropertyChanged(nameof(ShowHeaderNav));
        OnPropertyChanged(nameof(ShowRailLabels));
        OnPropertyChanged(nameof(ShowSidebar));
        OnPropertyChanged(nameof(ContentMaxWidth));
    }

    /// <summary>The account's pinned feeds, in profile order - what the feed tab row shows.</summary>
    public ObservableCollection<FeedTabItem> Feeds => _feeds.Tabs;

    /// <summary>The feed row shows on the timeline only; Settings and Notifications get the back button instead.</summary>
    public bool ShowTabs => _session.IsSignedIn && !_navigation.CanGoBack && IsRootPage(_navigation.CurrentPageType);

    /// <summary>The Home button shows whenever you are signed in and away from the timeline.</summary>
    public bool ShowHome => _session.IsSignedIn
        && _navigation.CurrentPageType is { } pageType
        && !IsRootPage(pageType);

    public int UnreadCount => _poll.UnreadCount;

    public bool HasUnread => _poll.UnreadCount > 0;

    /// <summary>The Messages button only appears once a chat call has worked for this session.</summary>
    public bool ShowMessages => _session.IsSignedIn && _chat.CanUseChat;

    public int UnreadMessageCount => _chat.UnreadCount;

    public bool HasUnreadMessages => _chat.HasUnread;

    private static bool IsRootPage(Type? pageType) => pageType == typeof(TimelinePage);

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BlueskySessionService.IsSignedIn))
        {
            OnPropertyChanged(nameof(IsSignedIn));
            OnPropertyChanged(nameof(ShowTabs));
            OnPropertyChanged(nameof(ShowHome));
            OnPropertyChanged(nameof(ShowMessages));
            OnLayoutDependentChanged();
        }
    }

    private void OnChatChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatService.CanUseChat))
            OnPropertyChanged(nameof(ShowMessages));
        else if (e.PropertyName == nameof(ChatService.UnreadCount))
        {
            OnPropertyChanged(nameof(UnreadMessageCount));
            OnPropertyChanged(nameof(HasUnreadMessages));
        }
    }

    private void OnPollChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationPollService.UnreadCount))
        {
            OnPropertyChanged(nameof(UnreadCount));
            OnPropertyChanged(nameof(HasUnread));
        }
    }
}
