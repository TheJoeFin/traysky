using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

// idunno flags the app.bsky.unspecced.* endpoints as evaluation-only because the AT Protocol may
// change or drop them. Trends and suggested users only exist there, and a failure just shows the
// page's error bar, so the risk is acceptable.
#pragma warning disable BSKYUnspecced

/// <summary>
/// Discovery: what's trending, accounts worth following and feeds worth a look. Each list loads
/// the first time its tab is shown and reloads on Retry. These are all server-side
/// recommendations, so nothing here is paged.
/// </summary>
public sealed partial class ExploreViewModel : DialogViewModel
{
    /// <summary>Pin or unpin a suggested feed; the parameter is the row's <see cref="FeedSuggestionItem"/>.</summary>
    [RelayCommand]
    private Task PinFeed(FeedSuggestionItem? feed) =>
        feed is null ? Task.CompletedTask : Helpers.FeedPinning.ToggleAsync(feed, Dialogs);

    private bool _trendsLoaded;
    private bool _peopleLoaded;
    private bool _feedsLoaded;

    public ObservableCollection<TrendItem> Trends { get; } = [];
    public ObservableCollection<ActorItem> People { get; } = [];
    public ObservableCollection<FeedSuggestionItem> Feeds { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    private bool CanLoad => BlueskySessionService.Instance.IsSignedIn && !PreviewMode.IsEnabled;

    public Task LoadTrendsAsync() => _trendsLoaded ? Task.CompletedTask : RunAsync("trends", async () =>
    {
        var result = await BlueskySessionService.Instance.Agent.GetTrends(limit: 25, subscribedLabelers: LabelService.Instance.Subscribed);
        if (!result.Succeeded || result.Result is null)
            throw new InvalidOperationException($"GetTrends failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");

        Trends.Clear();
        foreach (var trend in result.Result)
        {
            string category = Convert.ToString(trend.Category) ?? string.Empty;
            string count = trend.PostCount is > 0 ? $"{trend.PostCount:N0} posts" : string.Empty;
            Trends.Add(new TrendItem
            {
                Topic = trend.Topic,
                DisplayName = string.IsNullOrWhiteSpace(trend.DisplayName) ? trend.Topic : trend.DisplayName,
                Detail = string.Join(" · ", new[] { category, count }.Where(s => s.Length > 0))
            });
        }
        _trendsLoaded = true;
    });

    public Task LoadPeopleAsync() => _peopleLoaded ? Task.CompletedTask : RunAsync("suggested users", async () =>
    {
        var result = await BlueskySessionService.Instance.Agent.GetSuggestedUsers(limit: 50, subscribedLabelers: LabelService.Instance.Subscribed);
        if (!result.Succeeded || result.Result is null)
            throw new InvalidOperationException($"GetSuggestedUsers failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");

        People.Clear();
        foreach (var profile in result.Result)
            People.Add(FeedMapper.ToActorItem(profile));
        _peopleLoaded = true;
    });

    public Task LoadFeedsAsync() => _feedsLoaded ? Task.CompletedTask : RunAsync("suggested feeds", async () =>
    {
        var result = await BlueskySessionService.Instance.Agent.GetSuggestedFeeds(LabelService.Instance.Subscribed);
        if (!result.Succeeded || result.Result is null)
            throw new InvalidOperationException($"GetSuggestedFeeds failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");

        Feeds.Clear();
        foreach (var feed in result.Result)
            Feeds.Add(FeedMapper.ToFeedSuggestionItem(feed));
        _feedsLoaded = true;
    });

    private async Task RunAsync(string what, Func<Task> load)
    {
        if (!CanLoad)
            return;

        Error = null;
        IsLoading = true;
        try
        {
            await LabelService.Instance.EnsureLoadedAsync();
            await load();
        }
        catch (Exception ex)
        {
            LogService.Warn("Explore", $"Loading {what} failed: {ex.Message}");
            Error = $"Couldn't load {what}.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Drops what's loaded so the next tab view fetches fresh recommendations.</summary>
    [RelayCommand]
    private async Task Retry()
    {
        _trendsLoaded = _peopleLoaded = _feedsLoaded = false;
        await LoadTrendsAsync();
        await LoadPeopleAsync();
        await LoadFeedsAsync();
    }
}
