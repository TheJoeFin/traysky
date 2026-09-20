using idunno.AtProto;
using idunno.Bluesky;
using idunno.Bluesky.Actor;
using idunno.Bluesky.Feed;
using idunno.Bluesky.Graph;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>
/// The account's feed tab row: <c>app.bsky.actor.getPreferences</c>'s <c>savedFeedsPrefV2</c>,
/// filtered to the pinned entries and hydrated with each feed generator's or list's display
/// name and avatar. The order and visibility mirror what bsky.app shows across the top of the
/// Following screen, because that's the same preference record both apps read.
/// </summary>
public sealed class FeedsService
{
    private static readonly Lazy<FeedsService> _instance = new(() => new FeedsService());

    public static FeedsService Instance => _instance.Value;

    private static FeedTabItem FollowingFallback => new() { Kind = FeedTabKind.Timeline, DisplayName = "Following" };

    private bool _loaded;

    private FeedsService()
    {
        Tabs.Add(FollowingFallback);
        BlueskySessionService.Instance.SignedOut += (_, _) => Reset();
    }

    /// <summary>Pinned feeds in profile order. Always has at least the home timeline.</summary>
    public ObservableCollection<FeedTabItem> Tabs { get; } = [];

    /// <summary>Raised on the UI thread once <see cref="Tabs"/> reflects the account's real preferences.</summary>
    public event EventHandler? Loaded;

    /// <summary>Loads once per sign-in; call <see cref="RefreshAsync"/> directly to pick up changes made on bsky.app.</summary>
    public Task LoadIfNeededAsync() => _loaded ? Task.CompletedTask : RefreshAsync();

    public async Task RefreshAsync()
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return;

        if (PreviewMode.IsEnabled)
        {
            ReplaceTabs(PreviewMode.SampleFeedTabs());
            _loaded = true;
            Loaded?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            BlueskyAgent agent = BlueskySessionService.Instance.Agent;
            AtProtoHttpResult<Preferences> result = await agent.GetPreferences(includeBlueskyModerationLabeler: false);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Feeds", $"GetPreferences failed: {(int)result.StatusCode}");
                return;
            }

            IReadOnlyList<SavedFeed> pinned = PinnedFeedsOf(result.Result);

            List<AtUri> feedUris = [];
            List<AtUri> listUris = [];
            foreach (SavedFeed saved in pinned)
            {
                if (saved.Type == SavedFeedPreferenceType.Feed && TryParseUri(saved.Value, out AtUri feedUri))
                    feedUris.Add(feedUri);
                else if (saved.Type == SavedFeedPreferenceType.List && TryParseUri(saved.Value, out AtUri listUri))
                    listUris.Add(listUri);
            }

            Dictionary<string, GeneratorView> generators = await LoadGeneratorsAsync(agent, feedUris);
            Dictionary<string, ListView> lists = await LoadListsAsync(agent, listUris);

            List<FeedTabItem> tabs = [];
            foreach (SavedFeed saved in pinned)
            {
                switch (saved.Type)
                {
                    case SavedFeedPreferenceType.Timeline:
                        tabs.Add(new FeedTabItem { Kind = FeedTabKind.Timeline, DisplayName = "Following" });
                        break;

                    case SavedFeedPreferenceType.Feed when TryParseUri(saved.Value, out AtUri feedUri):
                        generators.TryGetValue(feedUri.ToString(), out GeneratorView? generator);
                        tabs.Add(new FeedTabItem
                        {
                            Kind = FeedTabKind.Feed,
                            Uri = feedUri,
                            DisplayName = generator?.DisplayName ?? "Feed",
                            AvatarUri = generator?.AvatarUri
                        });
                        break;

                    case SavedFeedPreferenceType.List when TryParseUri(saved.Value, out AtUri listUri):
                        lists.TryGetValue(listUri.ToString(), out ListView? list);
                        tabs.Add(new FeedTabItem
                        {
                            Kind = FeedTabKind.List,
                            Uri = listUri,
                            DisplayName = list?.Name ?? "List",
                            AvatarUri = list?.Avatar
                        });
                        break;
                }
            }

            if (tabs.Count == 0)
                tabs.Add(FollowingFallback);

            ReplaceTabs(tabs);
            _loaded = true;
        }
        catch (Exception ex)
        {
            LogService.Error("Feeds", "Refresh threw", ex);
        }
        finally
        {
            Loaded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The account's pinned feeds in display order. Prefers the modern v2 preference (what
    /// bsky.app's feed row actually reads); falls back to the legacy schema, treated as pinned
    /// feed generators, for the rare account that hasn't been migrated to v2.
    /// </summary>
    private static IReadOnlyList<SavedFeed> PinnedFeedsOf(Preferences preferences)
    {
        if (preferences.SavedFeedsPreferenceV2 is { Count: > 0 } itemsV2)
            return [.. itemsV2.Where(i => i.Pinned)];

        if (preferences.SavedFeedsPreference?.Pinned is { Count: > 0 } legacyPinned)
        {
            return
            [
                .. legacyPinned.Select((uri, i) =>
                    new SavedFeed(i.ToString(), SavedFeedPreferenceType.Feed, uri.ToString(), pinned: true))
            ];
        }

        return [];
    }

    private static async Task<Dictionary<string, GeneratorView>> LoadGeneratorsAsync(BlueskyAgent agent, IReadOnlyCollection<AtUri> uris)
    {
        Dictionary<string, GeneratorView> map = [];
        if (uris.Count == 0)
            return map;

        try
        {
            AtProtoHttpResult<IReadOnlyCollection<GeneratorView>> result = await agent.GetFeedGenerators(uris);
            if (result.Succeeded && result.Result is not null)
            {
                foreach (GeneratorView view in result.Result)
                    map[view.Uri.ToString()] = view;
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("Feeds", $"GetFeedGenerators failed: {ex.Message}");
        }
        return map;
    }

    /// <summary>Pinned lists are rare (usually zero or one), so these are fetched one at a time rather than batched.</summary>
    private static async Task<Dictionary<string, ListView>> LoadListsAsync(BlueskyAgent agent, IReadOnlyCollection<AtUri> uris)
    {
        Dictionary<string, ListView> map = [];
        foreach (AtUri uri in uris)
        {
            try
            {
                AtProtoHttpResult<ListViewWithItems> result = await agent.GetList(uri, limit: 1);
                if (result.Succeeded && result.Result is not null)
                    map[uri.ToString()] = result.Result.List;
            }
            catch (Exception ex)
            {
                LogService.Warn("Feeds", $"GetList failed for {uri}: {ex.Message}");
            }
        }
        return map;
    }

    private static bool TryParseUri(string? value, out AtUri uri)
    {
        if (!string.IsNullOrEmpty(value))
        {
            try
            {
                uri = new AtUri(value);
                return true;
            }
            catch (Exception)
            {
            }
        }
        uri = null!;
        return false;
    }

    private void ReplaceTabs(IReadOnlyList<FeedTabItem> tabs)
    {
        Tabs.Clear();
        foreach (FeedTabItem tab in tabs)
            Tabs.Add(tab);
    }

    private void Reset()
    {
        _loaded = false;
        ReplaceTabs([FollowingFallback]);
    }
}
