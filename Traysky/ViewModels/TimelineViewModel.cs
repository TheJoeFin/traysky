using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.Bluesky;
using idunno.Bluesky.Feed;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// The timeline page's content: whichever feed is currently selected (the home timeline by
/// default, or one of the account's other pinned feeds - see <see cref="SelectFeed"/>), paged
/// by cursor. One instance for the app's life so reopening the flyout is instant; it refreshes
/// itself when its content is older than <see cref="StaleAfter"/>.
/// </summary>
public sealed partial class TimelineViewModel : ObservableObject
{
    private static readonly Lazy<TimelineViewModel> _instance = new(() => new TimelineViewModel());

    public static TimelineViewModel Instance => _instance.Value;

    private const int PageSize = 30;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cursor;
    private DateTimeOffset _loadedAtUtc = DateTimeOffset.MinValue;

    /// <summary>The feed currently shown; null means the account's home timeline ("Following").</summary>
    private FeedTabItem? _currentFeed;

    private TimelineViewModel()
    {
        BlueskySessionService.Instance.SignedOut += (_, _) => Clear();
        ComposeViewModel.Instance.Posted += (_, _) => _ = RefreshAsync();
    }

    public ObservableCollection<PostItem> Posts { get; } = [];

    /// <summary>The feed tab currently shown, or null for the home timeline.</summary>
    public FeedTabItem? CurrentFeed => _currentFeed;

    /// <summary>Switches which feed is shown and reloads it from the start. A no-op if it's already selected.</summary>
    public void SelectFeed(FeedTabItem? feed)
    {
        string newKey = feed?.Key ?? "timeline";
        if (newKey == (_currentFeed?.Key ?? "timeline"))
            return;

        _currentFeed = feed;
        Clear();
        _ = RefreshAsync();
    }

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

    /// <summary>Called when the flyout opens: refresh only if the list is empty or old.</summary>
    public Task RefreshIfStaleAsync()
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return Task.CompletedTask;

        if (Posts.Count > 0 && DateTimeOffset.UtcNow - _loadedAtUtc < StaleAfter)
        {
            foreach (PostItem p in Posts)
                p.RefreshTimeAgo();
            return Task.CompletedTask;
        }

        return RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return;

        if (!await _gate.WaitAsync(0))
            return;

        IsLoading = Posts.Count == 0;
        Error = null;

        try
        {
            if (PreviewMode.IsEnabled)
            {
                Posts.Clear();
                foreach (PostItem p in PreviewMode.SamplePosts())
                    Posts.Add(p);
                _loadedAtUtc = DateTimeOffset.UtcNow;
                IsEmpty = false;
                return;
            }

            FeedPage page = await FetchPageAsync(cursor: null);

            if (!page.Succeeded || page.Posts is null)
            {
                Error = Describe(page.StatusCode, page.ErrorMessage);
                LogService.Warn("Timeline", $"Refresh failed: {(int)page.StatusCode}");
                return;
            }

            List<PostItem> fresh = [];
            foreach (FeedViewPost fp in page.Posts)
            {
                if (fp.Post is null)
                    continue;
                fresh.Add(FeedMapper.ToPostItem(fp));
            }

            Posts.Clear();
            foreach (PostItem p in fresh)
                Posts.Add(p);

            _cursor = page.Cursor;
            _loadedAtUtc = DateTimeOffset.UtcNow;
            IsEmpty = Posts.Count == 0;
        }
        catch (Exception ex)
        {
            LogService.Error("Timeline", "Refresh threw", ex);
            Error = "Couldn't load your timeline.";
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
            FeedPage page = await FetchPageAsync(_cursor);
            if (!page.Succeeded || page.Posts is null)
            {
                LogService.Warn("Timeline", $"Load more failed: {(int)page.StatusCode}");
                return;
            }

            HashSet<string> seen = [];
            foreach (PostItem p in Posts)
                seen.Add(p.AtUri);

            foreach (FeedViewPost fp in page.Posts)
            {
                if (fp.Post is null)
                    continue;
                PostItem item = FeedMapper.ToPostItem(fp);
                // Reposts make the same post appear twice across page boundaries.
                if (seen.Add(item.AtUri))
                    Posts.Add(item);
            }

            _cursor = page.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Error("Timeline", "Load more threw", ex);
        }
        finally
        {
            IsLoadingMore = false;
            _gate.Release();
        }
    }

    [RelayCommand]
    private Task Like(PostItem post) => PostInteractions.ToggleLikeAsync(post);

    [RelayCommand]
    private Task Repost(PostItem post) => PostInteractions.ToggleRepostAsync(post);

    [RelayCommand]
    private void Reply(PostItem post) => ComposeViewModel.Instance.BeginReply(post);

    [RelayCommand]
    private void Quote(PostItem post) => ComposeViewModel.Instance.BeginQuote(post);

    [RelayCommand]
    private Task OpenInBrowser(PostItem post) => RichTextBuilder.OpenAsync(post.WebUrl);

    [RelayCommand]
    private void OpenProfile(PostItem post) => Pages.ProfilePage.Open(post.AuthorDid.Length > 0 ? post.AuthorDid : post.AuthorHandle);

    private void Clear()
    {
        Posts.Clear();
        _cursor = null;
        _loadedAtUtc = DateTimeOffset.MinValue;
        Error = null;
        IsEmpty = false;
    }

    /// <summary>Fetch results, flattened to one shape regardless of which endpoint served the currently
    /// selected feed - the timeline, a feed generator's feed, or a list's feed all page the same way.</summary>
    private readonly record struct FeedPage(bool Succeeded, IEnumerable<FeedViewPost>? Posts, string? Cursor, System.Net.HttpStatusCode StatusCode, string? ErrorMessage);

    private async Task<FeedPage> FetchPageAsync(string? cursor)
    {
        BlueskyAgent agent = BlueskySessionService.Instance.Agent;

        if (_currentFeed is null || _currentFeed.Kind == FeedTabKind.Timeline)
        {
            AtProtoHttpResult<Timeline> result = await agent.GetTimeline(limit: PageSize, cursor: cursor);
            return new FeedPage(result.Succeeded, result.Result, result.Result?.Cursor, result.StatusCode, result.AtErrorDetail?.Message);
        }

        if (_currentFeed.Uri is not AtUri feedUri)
            return new FeedPage(false, null, null, 0, "That feed has no address.");

        AtProtoHttpResult<PagedViewReadOnlyCollection<FeedViewPost>> pageResult = _currentFeed.Kind == FeedTabKind.List
            ? await agent.GetListFeed(feedUri, limit: PageSize, cursor: cursor)
            : await agent.GetFeed(feedUri, limit: PageSize, cursor: cursor);

        return new FeedPage(pageResult.Succeeded, pageResult.Result, pageResult.Result?.Cursor, pageResult.StatusCode, pageResult.AtErrorDetail?.Message);
    }

    private static string Describe(System.Net.HttpStatusCode status, string? message) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized => "Your session expired. Sign in again.",
        System.Net.HttpStatusCode.TooManyRequests => "Bluesky is rate limiting requests. Try again shortly.",
        0 => "Couldn't reach Bluesky. Check your connection.",
        _ => string.IsNullOrEmpty(message) ? "Couldn't load your timeline." : message
    };
}
