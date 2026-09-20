using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.Bluesky.Feed;
using idunno.Bluesky.Notifications;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// The Notifications tab. Loads a page, groups the low-signal rows, hydrates the subject
/// posts in one batch, and marks everything seen once the user has actually looked.
/// </summary>
public sealed partial class NotificationsViewModel : ObservableObject
{
    private static readonly Lazy<NotificationsViewModel> _instance = new(() => new NotificationsViewModel());

    public static NotificationsViewModel Instance => _instance.Value;

    private const int PageSize = 40;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<NotificationInput> _inputs = [];
    private readonly Dictionary<string, Notification> _raw = [];
    private readonly Dictionary<string, PostView> _hydrated = [];
    private string? _cursor;
    private DateTimeOffset _loadedAtUtc = DateTimeOffset.MinValue;
    private DateTimeOffset? _fetchedAtUtc;

    private NotificationsViewModel()
    {
        BlueskySessionService.Instance.SignedOut += (_, _) => Clear();
    }

    public ObservableCollection<NotificationItem> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasMore => !string.IsNullOrEmpty(_cursor);

    /// <summary>Refresh when shown if the badge says there is something new or the list is old.</summary>
    public Task RefreshIfStaleAsync()
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return Task.CompletedTask;

        bool badgeSaysNew = NotificationPollService.Instance.UnreadCount > 0;
        if (Items.Count > 0 && !badgeSaysNew && DateTimeOffset.UtcNow - _loadedAtUtc < StaleAfter)
            return Task.CompletedTask;

        return RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return;

        if (!await _gate.WaitAsync(0))
            return;

        IsLoading = Items.Count == 0;
        Error = null;

        try
        {
            if (PreviewMode.IsEnabled)
            {
                Items.Clear();
                foreach (NotificationItem n in PreviewMode.SampleNotifications())
                    Items.Add(n);
                _loadedAtUtc = DateTimeOffset.UtcNow;
                _fetchedAtUtc = DateTimeOffset.UtcNow;
                IsEmpty = false;
                return;
            }

            // Captured before the request so marking seen later cannot swallow anything that
            // arrives while the list is on screen.
            DateTimeOffset fetchedAt = DateTimeOffset.UtcNow;

            AtProtoHttpResult<NotificationCollection> result = await BlueskySessionService.Instance.Agent.ListNotifications(limit: PageSize);
            if (!result.Succeeded || result.Result is null)
            {
                Error = result.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Your session expired. Sign in again.",
                    0 => "Couldn't reach Bluesky. Check your connection.",
                    _ => "Couldn't load notifications."
                };
                LogService.Warn("Notifications", $"Refresh failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return;
            }

            _inputs.Clear();
            _raw.Clear();
            _hydrated.Clear();

            Ingest(result.Result);
            await HydrateAsync(result.Result);
            Rebuild();

            _cursor = result.Result.Cursor;
            _loadedAtUtc = DateTimeOffset.UtcNow;
            _fetchedAtUtc = fetchedAt;
            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex)
        {
            LogService.Error("Notifications", "Refresh threw", ex);
            Error = "Couldn't load notifications.";
        }
        finally
        {
            IsLoading = false;
            _gate.Release();
        }
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (!HasMore || !BlueskySessionService.Instance.IsSignedIn)
            return;

        if (!await _gate.WaitAsync(0))
            return;

        IsLoadingMore = true;
        try
        {
            AtProtoHttpResult<NotificationCollection> result = await BlueskySessionService.Instance.Agent.ListNotifications(limit: PageSize, cursor: _cursor);
            if (!result.Succeeded || result.Result is null)
                return;

            Ingest(result.Result);
            await HydrateAsync(result.Result);
            Rebuild();
            _cursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Error("Notifications", "Load more threw", ex);
        }
        finally
        {
            IsLoadingMore = false;
            _gate.Release();
        }
    }

    /// <summary>
    /// Tells Bluesky the list has been seen, as of when it was fetched, and drops the badge.
    /// The page calls this after the tab has been visible for a moment.
    /// </summary>
    public async Task MarkSeenAsync()
    {
        if (!BlueskySessionService.Instance.IsSignedIn || _fetchedAtUtc is null)
            return;

        DateTimeOffset seenAt = _fetchedAtUtc.Value;
        NotificationPollService.Instance.MarkSeenLocally();
        ToastService.ClearAll();

        if (PreviewMode.IsEnabled)
            return;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.UpdateNotificationSeenAt(seenAt);
            if (!result.Succeeded)
                LogService.Warn("Notifications", $"UpdateSeenAt failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Notifications", $"UpdateSeenAt threw: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenProfile(NotificationItem item) => Pages.ProfilePage.Open(item.AuthorDid.Length > 0 ? item.AuthorDid : item.AuthorHandle);

    [RelayCommand]
    private void Reply(NotificationItem item)
    {
        if (item.Post is not null)
            ComposeViewModel.Instance.BeginReply(item.Post);
    }

    [RelayCommand]
    private Task Like(NotificationItem item) => item.Post is null ? Task.CompletedTask : PostInteractions.ToggleLikeAsync(item.Post);

    private void Ingest(NotificationCollection page)
    {
        foreach (Notification n in page)
        {
            if (n.Author is null)
                continue;
            string id = n.Uri.ToString();
            if (_raw.ContainsKey(id))
                continue;
            _raw[id] = n;
            _inputs.Add(FeedMapper.ToInput(n));
        }
    }

    /// <summary>
    /// One GetPosts for every subject on the page: the poster's own post for likes/reposts,
    /// and the notifying post itself for mentions/replies/quotes (for counts + viewer state).
    /// </summary>
    private async Task HydrateAsync(NotificationCollection page)
    {
        List<AtUri> wanted = [];
        HashSet<string> seen = [];

        foreach (Notification n in page)
        {
            string? uri = FeedMapper.ToKind(n.Reason) switch
            {
                NotificationKind.Like or NotificationKind.Repost => FeedMapper.SubjectUriOf(n),
                NotificationKind.Mention or NotificationKind.Reply or NotificationKind.Quote => n.Uri.ToString(),
                _ => null
            };

            if (uri is null || _hydrated.ContainsKey(uri) || !seen.Add(uri))
                continue;

            wanted.Add(new AtUri(uri));
        }

        // getPosts takes at most 25 URIs per call.
        for (int i = 0; i < wanted.Count; i += 25)
        {
            List<AtUri> batch = wanted.Skip(i).Take(25).ToList();
            try
            {
                AtProtoHttpResult<IReadOnlyCollection<PostView>> posts = await BlueskySessionService.Instance.Agent.GetPosts(batch);
                if (!posts.Succeeded || posts.Result is null)
                {
                    LogService.Warn("Notifications", $"GetPosts failed: {(int)posts.StatusCode} {posts.AtErrorDetail?.Error}");
                    continue;
                }
                foreach (PostView pv in posts.Result)
                    _hydrated[pv.Uri.ToString()] = pv;
            }
            catch (Exception ex)
            {
                LogService.Warn("Notifications", $"GetPosts threw: {ex.Message}");
            }
        }
    }

    private void Rebuild()
    {
        IReadOnlyList<NotificationGroup> groups = NotificationGroupingPolicy.Group(_inputs);

        Items.Clear();
        foreach (NotificationGroup g in groups)
            Items.Add(FeedMapper.ToNotificationItem(g, _raw, _hydrated));
    }

    private void Clear()
    {
        Items.Clear();
        _inputs.Clear();
        _raw.Clear();
        _hydrated.Clear();
        _cursor = null;
        _loadedAtUtc = DateTimeOffset.MinValue;
        _fetchedAtUtc = null;
        Error = null;
        IsEmpty = false;
    }
}
