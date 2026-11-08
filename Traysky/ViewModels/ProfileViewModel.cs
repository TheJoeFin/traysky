using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.AtProto.Repo;
using idunno.Bluesky;
using idunno.Bluesky.Actor;
using idunno.Bluesky.Chat;
using idunno.Bluesky.Feed;
using idunno.Bluesky.Graph;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Traysky.Pages;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// One account: header (banner, avatar, bio, counts, follow state) and tabs for their posts, likes
/// (own profile only), feeds and starter packs. Opened for a DID or handle; everything is fetched,
/// and each tab loads the first time it is shown.
/// </summary>
public sealed partial class ProfileViewModel : DialogViewModel
{
    private const int PageSize = 15;

    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, Enum.GetValues<ProfileTab>().Length).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly Dictionary<ProfileTab, string?> _cursors = [];
    private readonly HashSet<ProfileTab> _loadedTabs = [];
    private string? _actor;
    private Did? _did;
    private bool _similarLoaded;

    public ObservableCollection<PostItem> Posts { get; } = [];
    public ObservableCollection<PostItem> Likes { get; } = [];
    public ObservableCollection<FeedSuggestionItem> Feeds { get; } = [];
    public ObservableCollection<StarterPackItem> StarterPacks { get; } = [];
    public ObservableCollection<ListItem> Lists { get; } = [];

    /// <summary>Accounts like this one ("Similar accounts" expander), loaded on first expand.</summary>
    public ObservableCollection<ActorItem> Similar { get; } = [];

    /// <summary>A few of the people you follow who follow this account, for the avatar stack.</summary>
    public ObservableCollection<ActorItem> KnownFollowers { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKnownFollowers))]
    public partial string KnownFollowersText { get; private set; } = string.Empty;

    /// <summary>How many of the people you follow also follow this account (the server's total, not just the sample).</summary>
    public int KnownFollowersCount { get; private set; }

    public bool HasKnownFollowers => KnownFollowersText.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    public partial ProfileTab SelectedTab { get; private set; } = ProfileTab.Posts;

    [ObservableProperty]
    public partial bool IsLoadingSimilar { get; private set; }

    public bool CanShowSimilar => HasLoaded && !IsSelf;

    public string EmptyText => ProfileTabPolicy.EmptyText(SelectedTab);

    /// <summary>The tab has loaded, came back with nothing, and nothing is in flight.</summary>
    public bool ShowEmpty => HasLoaded && !IsLoadingPosts && _loadedTabs.Contains(SelectedTab) && ItemsFor(SelectedTab).Count == 0;

    /// <summary>The list behind a tab, for the page to bind the list to.</summary>
    public IList ItemsFor(ProfileTab tab) => tab switch
    {
        ProfileTab.Likes => Likes,
        ProfileTab.Feeds => Feeds,
        ProfileTab.StarterPacks => StarterPacks,
        ProfileTab.Lists => Lists,
        _ => Posts
    };

    [ObservableProperty]
    public partial string DisplayName { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HandleDisplay))]
    public partial string Handle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial Uri? AvatarUri { get; private set; }

    /// <summary>Community labels on this account, alerts first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLabels))]
    public partial IReadOnlyList<LabelDisplay> Labels { get; private set; } = [];

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
    [NotifyPropertyChangedFor(nameof(ShowBell))]
    public partial bool IsFollowing { get; private set; }

    /// <summary>They follow the signed-in account.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBell))]
    public partial bool FollowsYou { get; private set; }

    /// <summary>What the signed-in account is notified about from this profile (the bell).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBell))]
    [NotifyPropertyChangedFor(nameof(BellTooltip))]
    [NotifyPropertyChangedFor(nameof(IsBellOn))]
    public partial ActivitySubscriptionLevel ActivityLevel { get; private set; }

    /// <summary>The profile's <c>allowSubscriptions</c> setting, as the server sent it.</summary>
    private string? _allowSubscriptions;

    public bool ShowBell => ActivitySubscriptionPolicy.ShowBell(_allowSubscriptions, IsSelf, IsFollowing, FollowsYou, ActivityLevel);

    public string BellTooltip => ActivitySubscriptionPolicy.BellTooltip(ActivityLevel);

    public bool IsBellOn => ActivityLevel != ActivitySubscriptionLevel.Off;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteLabel))]
    public partial bool IsMuted { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BlockLabel))]
    public partial bool IsBlocking { get; private set; }

    public string MuteLabel => IsMuted ? "Unmute account" : "Mute account";

    public string BlockLabel => IsBlocking ? "Unblock account" : "Block account";

    /// <summary>True for the signed-in account's own profile: no follow button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFollow))]
    [NotifyPropertyChangedFor(nameof(CanShowSimilar))]
    [NotifyPropertyChangedFor(nameof(CanGoLive))]
    public partial bool IsSelf { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>The selected tab's list is being fetched (the property predates the tabs).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    public partial bool IsLoadingPosts { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFollow))]
    public partial bool IsFollowBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFollow))]
    [NotifyPropertyChangedFor(nameof(CanShowSimilar))]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    [NotifyPropertyChangedFor(nameof(CanGoLive))]
    public partial bool HasLoaded { get; private set; }

    /// <summary>Show "Message": this session has DM access and their chat settings would let us in.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MessageCommand))]
    public partial bool CanMessage { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MessageCommand))]
    public partial bool IsOpeningConversation { get; private set; }

    private string? _followUri;
    private string? _blockUri;
    private LiveStatusInfo? _live;

    /// <summary>The account has a live status that hasn't run out. The page ticks <see cref="RefreshLive"/> while this is true.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveText))]
    [NotifyPropertyChangedFor(nameof(CanGoLive))]
    [NotifyPropertyChangedFor(nameof(CanWatchLive))]
    public partial bool IsLive { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoLive))]
    public partial bool IsLiveBusy { get; private set; }

    /// <summary>"Title · 1 h 12 m left" for the LIVE strip.</summary>
    public string LiveText
    {
        get
        {
            if (_live is not { } live)
                return string.Empty;
            string left = LiveStatusPolicy.RemainingText(live.ExpiresAt, DateTimeOffset.UtcNow);
            return left.Length == 0 ? live.Title : $"{live.Title} · {left}";
        }
    }

    /// <summary>Own profile, not live yet.</summary>
    public bool CanGoLive => IsSelf && !IsLive && !IsLiveBusy && HasLoaded;

    /// <summary>There is a safe link to open the stream with.</summary>
    public bool CanWatchLive => IsLive && _live?.Link is not null;

    public string HandleDisplay => Handle.Length > 0 ? "@" + Handle : string.Empty;

    public bool HasBanner => BannerUri is not null;

    public bool HasLabels => Labels.Count > 0;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool CanFollow => !IsSelf && !IsFollowBusy && HasLoaded;

    public string FollowLabel => IsFollowing ? "Following" : "Follow";

    /// <summary>The DID once loaded, else whatever the page was opened with (DID or handle).</summary>
    public string ActorKey => _did?.ToString() ?? _actor ?? string.Empty;

    public bool HasMore => _cursors.TryGetValue(SelectedTab, out string? cursor) && !string.IsNullOrEmpty(cursor);

    public string WebUrl => BlueskyLinks.ProfileUrl(Handle.Length > 0 ? Handle : _actor ?? string.Empty);

    public async Task LoadAsync(string didOrHandle)
    {
        _actor = didOrHandle;
        Error = null;
        Posts.Clear();
        Likes.Clear();
        Feeds.Clear();
        StarterPacks.Clear();
        Lists.Clear();
        Similar.Clear();
        KnownFollowers.Clear();
        KnownFollowersText = string.Empty;
        KnownFollowersCount = 0;
        _similarLoaded = false;
        _cursors.Clear();
        _loadedTabs.Clear();
        HasLoaded = false;
        Labels = [];
        SetLive(null);

        // Show what we know before the network answers.
        if (didOrHandle.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
            DisplayName = string.Empty;
        else
        {
            Handle = didOrHandle;
            DisplayName = didOrHandle;
        }

        if (PreviewMode.IsEnabled)
        {
            LoadPreview();
            return;
        }

        if (!BlueskySessionService.Instance.IsSignedIn)
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

            await LabelService.Instance.EnsureLoadedAsync();
            AtProtoHttpResult<ProfileViewDetailed> result = await BlueskySessionService.Instance.Agent.GetProfile(actor, LabelService.Instance.Subscribed);
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
            Labels = LabelService.Instance.ForAccount(p);
            BannerUri = p.Banner;
            Description = p.Description;
            FollowersCount = p.FollowersCount;
            FollowsCount = p.FollowsCount;
            PostsCount = p.PostsCount;
            _followUri = p.Viewer?.Following?.ToString();
            IsFollowing = _followUri is not null;
            FollowsYou = p.Viewer?.FollowedBy is not null;
            IsMuted = p.Viewer?.Muted == true;
            _blockUri = p.Viewer?.Blocking?.ToString();
            IsBlocking = _blockUri is not null;
            IsSelf = string.Equals(p.Did?.ToString(), BlueskySessionService.Instance.Did, StringComparison.Ordinal);
            _allowSubscriptions = p.Associated?.ActivitySubscription?.AllowSubscriptions;
            ActivityLevel = p.Viewer?.ActivitySubscription is { } sub
                ? ActivitySubscriptionPolicy.FromFlags(sub.Post, sub.Reply)
                : ActivitySubscriptionLevel.Off;
            OnPropertyChanged(nameof(ShowBell));
            SetLive(LiveStatusService.FromView(p.Status));
            CanMessage =ChatService.Instance.CanUseChat
                && ChatAccessPolicy.CanOfferMessage(IsSelf, ToIncomingChatSetting(p.Associated?.Chat?.AllowIncoming), FollowsYou);

            if (!IsSelf && p.Viewer?.KnownFollowers is { Count: > 0 } known)
            {
                foreach (ProfileViewBasic follower in known.Followers)
                    KnownFollowers.Add(FeedMapper.ToActorItem(follower));
                KnownFollowersCount = known.Count;
                KnownFollowersText = KnownFollowersPolicy.Summary(KnownFollowers.Select(a => a.NameOrHandle).ToList(), known.Count);
            }

            SelectedTab = ProfileTabPolicy.Normalize(SelectedTab, IsSelf);
            HasLoaded = true;

            await LoadTabAsync(SelectedTab, reset: true);
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

    public Task LoadMoreAsync() => HasMore ? LoadTabAsync(SelectedTab, reset: false) : Task.CompletedTask;

    /// <summary>Switches the list under the header, fetching it the first time it is shown.</summary>
    public Task SelectTabAsync(ProfileTab tab)
    {
        tab = ProfileTabPolicy.Normalize(tab, IsSelf);
        if (tab == SelectedTab)
            return Task.CompletedTask;

        SelectedTab = tab;
        // Until the profile has loaded LoadAsync fetches whichever tab is selected by then.
        return _did is not null && !_loadedTabs.Contains(tab) ? LoadTabAsync(tab, reset: true) : Task.CompletedTask;
    }

    private async Task LoadTabAsync(ProfileTab tab, bool reset)
    {
        if (_did is null || !await _gates[(int)tab].WaitAsync(0))
            return;

        IsLoadingPosts = true;
        try
        {
            string? cursor = reset ? null : _cursors.GetValueOrDefault(tab);
            string? next = await FetchTabPageAsync(tab, cursor, reset);
            if (next is not null)
                _cursors[tab] = next;
        }
        catch (Exception ex)
        {
            LogService.Warn("Profile", $"Loading {tab} threw: {ex.Message}");
        }
        finally
        {
            IsLoadingPosts = false;
            _gates[(int)tab].Release();
            OnPropertyChanged(nameof(ShowEmpty));
            OnPropertyChanged(nameof(HasMore));
        }
    }

    /// <summary>Fetches one page into the tab's list. Returns the next cursor ("" at the end), or null when the call failed.</summary>
    private async Task<string?> FetchTabPageAsync(ProfileTab tab, string? cursor, bool reset)
    {
        BlueskyAgent agent = BlueskySessionService.Instance.Agent;
        var labelers = LabelService.Instance.Subscribed;

        switch (tab)
        {
            case ProfileTab.Posts:
            {
                var feed = await agent.GetAuthorFeed(_did!, limit: PageSize, cursor: cursor, filter: FeedFilter.PostsNoReplies, subscribedLabelers: labelers);
                if (!feed.Succeeded || feed.Result is null)
                    return Failed("GetAuthorFeed", feed.StatusCode, feed.AtErrorDetail?.Error);

                Fill(Posts, reset, feed.Result.Where(fp => fp.Post is not null).Select(fp => FeedMapper.ToPostItem(fp)));
                return Done(tab, feed.Result.Cursor);
            }
            case ProfileTab.Likes:
            {
                // The API takes no actor: it is always the signed-in account's own likes.
                var feed = await agent.GetActorLikes(limit: PageSize, cursor: cursor, subscribedLabelers: labelers);
                if (!feed.Succeeded || feed.Result is null)
                    return Failed("GetActorLikes", feed.StatusCode, feed.AtErrorDetail?.Error);

                Fill(Likes, reset, feed.Result.Where(fp => fp.Post is not null).Select(fp => FeedMapper.ToPostItem(fp)));
                return Done(tab, feed.Result.Cursor);
            }
            case ProfileTab.Feeds:
            {
                var feeds = await agent.GetActorFeeds(_did!, limit: PageSize, cursor: cursor, subscribedLabelers: labelers);
                if (!feeds.Succeeded || feeds.Result is null)
                    return Failed("GetActorFeeds", feeds.StatusCode, feeds.AtErrorDetail?.Error);

                Fill(Feeds, reset, feeds.Result.Select(FeedMapper.ToFeedSuggestionItem));
                return Done(tab, feeds.Result.Cursor);
            }
            case ProfileTab.Lists:
            {
                var lists = await agent.GetLists(_did!, limit: PageSize, cursor: cursor, subscribedLabelers: labelers);
                if (!lists.Succeeded || lists.Result is null)
                    return Failed("GetLists", lists.StatusCode, lists.AtErrorDetail?.Error);

                // Reference lists back starter packs, which have their own tab.
                Fill(Lists, reset, lists.Result.Select(FeedMapper.ToListItem).Where(l => ListPolicy.IsShown(l.Kind)));
                return Done(tab, lists.Result.Cursor);
            }
            default:
            {
                var packs = await agent.GetActorStarterPacks(_did!, limit: PageSize, cursor: cursor);
                if (!packs.Succeeded || packs.Result is null)
                    return Failed("GetActorStarterPacks", packs.StatusCode, packs.AtErrorDetail?.Error);

                Fill(StarterPacks, reset, packs.Result.Select(FeedMapper.ToStarterPackItem));
                return Done(tab, packs.Result.Cursor);
            }
        }
    }

    private static string? Failed(string call, System.Net.HttpStatusCode status, string? error)
    {
        LogService.Warn("Profile", $"{call} failed: {(int)status} {error}");
        return null;
    }

    private string Done(ProfileTab tab, string? cursor)
    {
        _loadedTabs.Add(tab);
        return cursor ?? string.Empty;
    }

    private static void Fill<T>(ObservableCollection<T> list, bool reset, IEnumerable<T> items)
    {
        if (reset)
            list.Clear();
        foreach (T item in items)
            list.Add(item);
    }

    /// <summary>Loads the "Similar accounts" suggestions once, when the expander is first opened.</summary>
    public async Task LoadSimilarAsync()
    {
        if (_did is null || _similarLoaded || IsLoadingSimilar || IsSelf)
            return;

        IsLoadingSimilar = true;
        try
        {
            var result = await BlueskySessionService.Instance.Agent.GetSuggestedFollowsByActor(_did, LabelService.Instance.Subscribed);
            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Profile", $"GetSuggestedFollowsByActor failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return;
            }

            foreach (ProfileView profile in result.Result.Suggestions.Where(s => s.Did.ToString() != _did.ToString()))
                Similar.Add(FeedMapper.ToActorItem(profile));
            _similarLoaded = true;
        }
        catch (Exception ex)
        {
            LogService.Warn("Profile", $"GetSuggestedFollowsByActor threw: {ex.Message}");
        }
        finally
        {
            IsLoadingSimilar = false;
        }
    }

    private void LoadPreview()
    {
        foreach (PostItem post in PreviewMode.SamplePosts())
        {
            Posts.Add(post);
            Likes.Add(post);
        }
        foreach (FeedSuggestionItem feed in PreviewMode.SampleFeedSuggestions())
            Feeds.Add(feed);
        foreach (StarterPackItem pack in PreviewMode.SampleStarterPacks())
            StarterPacks.Add(pack);
        foreach (ListItem list in PreviewMode.SampleLists())
            Lists.Add(list);
        foreach (ActorItem actor in PreviewMode.SampleSearchActors())
        {
            KnownFollowers.Add(actor);
            Similar.Add(actor);
        }

        KnownFollowersCount = 5;
        KnownFollowersText = KnownFollowersPolicy.Summary(KnownFollowers.Select(a => a.NameOrHandle).ToList(), KnownFollowersCount);
        _similarLoaded = true;
        foreach (ProfileTab tab in Enum.GetValues<ProfileTab>())
            _loadedTabs.Add(tab);
        HasLoaded = true;
    }

    private static IncomingChatSetting ToIncomingChatSetting(AllowIncomingChat? allow) => allow switch
    {
        AllowIncomingChat.All => IncomingChatSetting.All,
        AllowIncomingChat.Following => IncomingChatSetting.Following,
        AllowIncomingChat.None => IncomingChatSetting.None,
        _ => IncomingChatSetting.NotDeclared
    };

    private bool CanOpenConversation() => CanMessage && !IsOpeningConversation;

    /// <summary>Opens the 1:1 conversation with this account, creating it if there isn't one yet.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenConversation))]
    private async Task MessageAsync()
    {
        if (_did is null)
            return;

        IsOpeningConversation = true;
        try
        {
            AtProtoHttpResult<ConversationView> result = await BlueskySessionService.Instance.Agent.GetConversationForMembers([_did]);
            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Profile", $"GetConversationForMembers failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                Error = result.AtErrorDetail?.Message is { Length: > 0 } message ? message : "Couldn't start a conversation with this account.";
                return;
            }

            ConversationItem conversation = ChatMapper.ToConversationItem(result.Result, BlueskySessionService.Instance.Did);
            ConversationPage.Open(conversation);
        }
        catch (Exception ex)
        {
            LogService.Error("Profile", "Opening a conversation threw", ex);
            Error = "Couldn't start a conversation with this account.";
        }
        finally
        {
            IsOpeningConversation = false;
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
                AtProtoHttpResult<DeleteResult> result = await agent.DeleteFollow(new AtUri(_followUri));
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

    /// <summary>Makes a list on your own profile and shows it at the top of the Lists tab. Null when the server refused.</summary>
    public async Task<ListItem?> CreateListAsync(string name, string description, ListKind kind)
    {
        ListItem? list = PreviewMode.IsEnabled
            ? new ListItem { Uri = "at://did:plc:preview-self/app.bsky.graph.list/" + Guid.NewGuid().ToString("N")[..8], Name = name, Description = description, Kind = kind, CreatorDid = PreviewMode.SelfDid, CreatorHandle = PreviewMode.Handle }
            : await ListsService.CreateAsync(name, description, kind);
        if (list is null)
            return null;

        // If the tab hasn't loaded yet its first fetch replaces this and includes the new list anyway.
        Lists.Insert(0, list);
        OnPropertyChanged(nameof(ShowEmpty));
        return list;
    }

    /// <summary>Changes what this account's activity notifies you about. Returns false when the server refused.</summary>
    public async Task<bool> SetActivityAsync(ActivitySubscriptionLevel level)
    {
        if (_did is null || IsSelf)
            return false;

        if (level == ActivityLevel)
            return true;

        if (!await ActivitySubscriptionService.SetAsync(_did.ToString(), level))
            return false;

        ActivityLevel = level;
        return true;
    }

    /// <summary>Mutes or unmutes this account. Returns false when the server refused.</summary>
    public async Task<bool> ToggleMuteAsync()
    {
        if (_did is null || IsSelf)
            return false;

        bool mute = !IsMuted;
        if (!await ModerationService.SetMutedAsync(_did.ToString(), mute))
            return false;

        IsMuted = mute;
        return true;
    }

    /// <summary>Blocks or unblocks this account (the caller confirms a block first). Returns false when the server refused.</summary>
    public async Task<bool> ToggleBlockAsync()
    {
        if (_did is null || IsSelf)
            return false;

        if (IsBlocking)
        {
            if (_blockUri is null || !await ModerationService.UnblockAsync(_blockUri))
                return false;

            _blockUri = null;
            IsBlocking = false;
            return true;
        }

        string? uri = await ModerationService.BlockAsync(_did.ToString());
        if (uri is null)
            return false;

        _blockUri = uri;
        IsBlocking = true;
        // Blocking removes any follow relationship in both directions.
        IsFollowing = false;
        _followUri = null;
        return true;
    }

    public Task<bool> ReportAsync(idunno.Bluesky.Moderation.ReportType reason, string details) =>
        _did is null ? Task.FromResult(false) : ModerationService.ReportAccountAsync(_did.ToString(), reason, details);

    private const string TryAgainText = "That didn't go through. Try again in a moment.";

    /// <summary>Pin or unpin a feed from the Feeds tab; the parameter is the row's <see cref="FeedSuggestionItem"/>.</summary>
    [RelayCommand]
    private Task PinFeed(FeedSuggestionItem? feed) =>
        feed is null ? Task.CompletedTask : Helpers.FeedPinning.ToggleAsync(feed, Dialogs);

    /// <summary>Asks for a name, makes the list on your own profile and opens it.</summary>
    [RelayCommand]
    private async Task NewList()
    {
        if (await Dialogs.AskListAsync(existing: null) is not { } created)
            return;

        ListItem? list = await CreateListAsync(created.Name, created.Description, created.Kind);
        if (list is null)
        {
            await Dialogs.ShowMessageAsync("Couldn't create the list", TryAgainText);
            return;
        }

        ListPage.Open(list);
    }

    [RelayCommand]
    private Task AddToList() => Dialogs.ShowListMembershipAsync(ActorKey, Handle);

    /// <summary>Sets what this account's activity notifies you about; the parameter is an <see cref="ActivitySubscriptionLevel"/> name.</summary>
    [RelayCommand]
    private async Task SetActivityLevel(string? level)
    {
        if (!Enum.TryParse(level, out ActivitySubscriptionLevel parsed))
            return;

        if (!await SetActivityAsync(parsed))
            await Dialogs.ShowMessageAsync("Couldn't update notifications", TryAgainText);
    }

    [RelayCommand]
    private async Task UpdateMute()
    {
        if (!await ToggleMuteAsync())
            await Dialogs.ShowMessageAsync("Couldn't update mute", TryAgainText);
    }

    [RelayCommand]
    private async Task UpdateBlock()
    {
        // Unblocking needs no confirmation.
        if (!IsBlocking && !await Dialogs.ConfirmBlockAsync(Handle))
            return;

        if (!await ToggleBlockAsync())
            await Dialogs.ShowMessageAsync("Couldn't update block", TryAgainText);
    }

    [RelayCommand]
    private async Task ReportAccount()
    {
        if (await Dialogs.AskReportAsync("account") is not { } report)
            return;

        bool sent = await ReportAsync(report.Reason, report.Details);
        await Dialogs.ShowMessageAsync(
            sent ? "Report sent" : "Couldn't send report",
            sent ? "Thanks. Bluesky's moderators will take a look." : "The report didn't go through. Try again in a moment.");
    }

    private void SetLive(LiveStatusInfo? live)
    {
        _live = live;
        IsLive = live is not null;
        OnPropertyChanged(nameof(LiveText));
        OnPropertyChanged(nameof(CanWatchLive));
    }

    /// <summary>Re-reads the clock: updates the time left and drops the status once it has run out.</summary>
    public void RefreshLive()
    {
        if (_live is { } live && !LiveStatusPolicy.IsLive(LiveStatusPolicy.LiveValue, live.ExpiresAt, null, null, DateTimeOffset.UtcNow))
            SetLive(null);
        else
            OnPropertyChanged(nameof(LiveText));
    }

    [RelayCommand]
    private async Task GoLive()
    {
        if (!CanGoLive || await Dialogs.AskGoLiveAsync() is not { } ask)
            return;

        IsLiveBusy = true;
        try
        {
            (LiveStatusInfo? info, string? error) = await LiveStatusService.GoLiveAsync(ask.Link, ask.Title, ask.Minutes);
            if (info is null)
                await Dialogs.ShowMessageAsync("Couldn't go live", error ?? TryAgainText);
            else
                SetLive(info);
        }
        finally
        {
            IsLiveBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExtendLive()
    {
        if (_live is not { } live || !IsSelf || IsLiveBusy)
            return;

        IsLiveBusy = true;
        try
        {
            (LiveStatusInfo? info, string? error) = await LiveStatusService.ExtendAsync(live, LiveStatusPolicy.ExtendMinutes);
            if (info is null)
                await Dialogs.ShowMessageAsync("Couldn't extend", error ?? TryAgainText);
            else
                SetLive(info);
        }
        finally
        {
            IsLiveBusy = false;
        }
    }

    [RelayCommand]
    private async Task EndLive()
    {
        if (_live is null || !IsSelf || IsLiveBusy)
            return;

        IsLiveBusy = true;
        try
        {
            if (await LiveStatusService.EndAsync())
                SetLive(null);
            else
                await Dialogs.ShowMessageAsync("Couldn't end the stream", TryAgainText);
        }
        finally
        {
            IsLiveBusy = false;
        }
    }

    /// <summary>Opens the stream in the default browser (http(s) links only; see <see cref="LiveStatusPolicy.TryNormalizeLink"/>).</summary>
    [RelayCommand]
    private Task WatchLive() =>
        _live?.Link is { } link ? RichTextBuilder.OpenAsync(link.ToString()) : Task.CompletedTask;

    [RelayCommand]
    private Task Retry() => _actor is null ? Task.CompletedTask : LoadAsync(_actor);

    [RelayCommand]
    private Task OpenInBrowser() => RichTextBuilder.OpenAsync(WebUrl);
}
