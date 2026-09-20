using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.AtProto.Repo;
using idunno.Bluesky;
using idunno.Bluesky.Actor;
using idunno.Bluesky.Feed;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// One account: header (banner, avatar, bio, counts, follow state) and their recent posts.
/// Opened for a DID or handle; everything is fetched.
/// </summary>
public sealed partial class ProfileViewModel : ObservableObject
{
    private const int PageSize = 25;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _actor;
    private Did? _did;
    private string? _cursor;

    public ObservableCollection<PostItem> Posts { get; } = [];

    [ObservableProperty]
    public partial string DisplayName { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HandleDisplay))]
    public partial string Handle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial Uri? AvatarUri { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBanner))]
    public partial Uri? BannerUri { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    public partial string? Description { get; private set; }

    [ObservableProperty]
    public partial int FollowersCount { get; private set; }

    [ObservableProperty]
    public partial int FollowsCount { get; private set; }

    [ObservableProperty]
    public partial int PostsCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowLabel))]
    public partial bool IsFollowing { get; private set; }

    /// <summary>They follow the signed-in account.</summary>
    [ObservableProperty]
    public partial bool FollowsYou { get; private set; }

    /// <summary>True for the signed-in account's own profile: no follow button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFollow))]
    public partial bool IsSelf { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingPosts { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFollow))]
    public partial bool IsFollowBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial bool HasLoaded { get; private set; }

    private string? _followUri;

    public string HandleDisplay => Handle.Length > 0 ? "@" + Handle : string.Empty;

    public bool HasBanner => BannerUri is not null;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool CanFollow => !IsSelf && !IsFollowBusy && HasLoaded;

    public string FollowLabel => IsFollowing ? "Following" : "Follow";

    public bool HasMore => !string.IsNullOrEmpty(_cursor);

    public string WebUrl => BlueskyLinks.ProfileUrl(Handle.Length > 0 ? Handle : _actor ?? string.Empty);

    public async Task LoadAsync(string didOrHandle)
    {
        _actor = didOrHandle;
        Error = null;
        Posts.Clear();
        _cursor = null;
        HasLoaded = false;

        // Show what we know before the network answers.
        if (didOrHandle.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
            DisplayName = string.Empty;
        else
        {
            Handle = didOrHandle;
            DisplayName = didOrHandle;
        }

        if (!BlueskySessionService.Instance.IsSignedIn || PreviewMode.IsEnabled)
        {
            HasLoaded = true;
            return;
        }

        IsLoading = true;
        try
        {
            AtIdentifier actor = didOrHandle.StartsWith("did:", StringComparison.OrdinalIgnoreCase)
                ? new Did(didOrHandle)
                : new idunno.AtProto.Handle(didOrHandle);

            AtProtoHttpResult<ProfileViewDetailed> result = await BlueskySessionService.Instance.Agent.GetProfile(actor);
            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Profile", $"GetProfile failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                Error = result.StatusCode switch
                {
                    System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound => "This account could not be found.",
                    0 => "Couldn't reach Bluesky. Check your connection.",
                    _ => "Couldn't load this profile."
                };
                return;
            }

            ProfileViewDetailed p = result.Result;
            _did = p.Did;
            Handle = p.Handle?.ToString() ?? string.Empty;
            DisplayName = FeedMapper.DisplayNameOf(p);
            AvatarUri = p.Avatar;
            BannerUri = p.Banner;
            Description = p.Description;
            FollowersCount = p.FollowersCount;
            FollowsCount = p.FollowsCount;
            PostsCount = p.PostsCount;
            _followUri = p.Viewer?.Following?.ToString();
            IsFollowing = _followUri is not null;
            FollowsYou = p.Viewer?.FollowedBy is not null;
            IsSelf = string.Equals(p.Did?.ToString(), BlueskySessionService.Instance.Did, StringComparison.Ordinal);
            HasLoaded = true;

            await LoadPostsAsync(reset: true);
        }
        catch (Exception ex)
        {
            LogService.Error("Profile", "GetProfile threw", ex);
            Error = "Couldn't load this profile.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task LoadMoreAsync() => HasMore ? LoadPostsAsync(reset: false) : Task.CompletedTask;

    private async Task LoadPostsAsync(bool reset)
    {
        if (_did is null || !await _gate.WaitAsync(0))
            return;

        IsLoadingPosts = true;
        try
        {
            AtProtoHttpResult<PagedViewReadOnlyCollection<FeedViewPost>> feed = await BlueskySessionService.Instance.Agent.GetAuthorFeed(
                _did,
                limit: PageSize,
                cursor: reset ? null : _cursor,
                filter: FeedFilter.PostsNoReplies);

            if (!feed.Succeeded || feed.Result is null)
            {
                LogService.Warn("Profile", $"GetAuthorFeed failed: {(int)feed.StatusCode} {feed.AtErrorDetail?.Error}");
                return;
            }

            if (reset)
                Posts.Clear();

            foreach (FeedViewPost fp in feed.Result)
            {
                if (fp.Post is not null)
                    Posts.Add(FeedMapper.ToPostItem(fp));
            }

            _cursor = feed.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Warn("Profile", $"GetAuthorFeed threw: {ex.Message}");
        }
        finally
        {
            IsLoadingPosts = false;
            _gate.Release();
        }
    }

    [RelayCommand]
    private async Task ToggleFollowAsync()
    {
        if (_did is null || IsSelf || IsFollowBusy)
            return;

        IsFollowBusy = true;
        bool wasFollowing = IsFollowing;
        IsFollowing = !wasFollowing;
        FollowersCount = Math.Max(0, FollowersCount + (wasFollowing ? -1 : 1));

        try
        {
            BlueskyAgent agent = BlueskySessionService.Instance.Agent;
            if (wasFollowing)
            {
                if (_followUri is null)
                    return;
                AtProtoHttpResult<Commit> result = await agent.DeleteFollow(new AtUri(_followUri));
                if (result.Succeeded)
                {
                    _followUri = null;
                    return;
                }
                LogService.Warn("Profile", $"Unfollow failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
            }
            else
            {
                AtProtoHttpResult<CreateRecordResult> result = await agent.Follow(_did);
                if (result.Succeeded && result.Result is not null)
                {
                    _followUri = result.Result.Uri.ToString();
                    return;
                }
                LogService.Warn("Profile", $"Follow failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
            }

            IsFollowing = wasFollowing;
            FollowersCount = Math.Max(0, FollowersCount + (wasFollowing ? 1 : -1));
        }
        catch (Exception ex)
        {
            LogService.Warn("Profile", $"Follow threw: {ex.Message}");
            IsFollowing = wasFollowing;
            FollowersCount = Math.Max(0, FollowersCount + (wasFollowing ? 1 : -1));
        }
        finally
        {
            IsFollowBusy = false;
        }
    }

    [RelayCommand]
    private Task Retry() => _actor is null ? Task.CompletedTask : LoadAsync(_actor);

    [RelayCommand]
    private Task OpenInBrowser() => RichTextBuilder.OpenAsync(WebUrl);
}
