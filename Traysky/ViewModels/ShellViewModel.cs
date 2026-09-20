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
    private readonly NavigationService _navigation = NavigationService.Instance;
    private readonly BlueskySessionService _session = BlueskySessionService.Instance;
    private readonly NotificationPollService _poll = NotificationPollService.Instance;
    private readonly FeedsService _feeds = FeedsService.Instance;

    public ShellViewModel()
    {
        _navigation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NavigationService.CanGoBack))
            {
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(ShowTabs));
            }
        };
        _navigation.NavigationChanged += (_, _) => OnPropertyChanged(nameof(ShowTabs));
        _session.PropertyChanged += OnSessionChanged;
        _poll.PropertyChanged += OnPollChanged;
    }

    public bool CanGoBack => _navigation.CanGoBack;

    public bool IsSignedIn => _session.IsSignedIn;

    /// <summary>The account's pinned feeds, in profile order - what the feed tab row shows.</summary>
    public ObservableCollection<FeedTabItem> Feeds => _feeds.Tabs;

    /// <summary>The feed row shows on the timeline only; Settings and Notifications get the back button instead.</summary>
    public bool ShowTabs => _session.IsSignedIn && !_navigation.CanGoBack && IsRootPage(_navigation.Frame?.Content?.GetType());

    public int UnreadCount => _poll.UnreadCount;

    public bool HasUnread => _poll.UnreadCount > 0;

    private static bool IsRootPage(Type? pageType) => pageType == typeof(TimelinePage);

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BlueskySessionService.IsSignedIn))
        {
            OnPropertyChanged(nameof(IsSignedIn));
            OnPropertyChanged(nameof(ShowTabs));
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
