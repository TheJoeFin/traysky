using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.Helpers;

/// <summary>The Pin / Unpin button on feed rows (Explore suggestions and a profile's Feeds tab).</summary>
public static class FeedPinning
{
    /// <summary>
    /// Flips the row's pinned state straight away and stores it; if the account can't be updated the row
    /// goes back and a dialog says so. The shell's tab row refreshes itself after a successful change.
    /// </summary>
    public static async Task ToggleAsync(FeedSuggestionItem feed, IDialogService dialogs)
    {
        if (!feed.CanPin)
            return;

        bool pin = !feed.IsPinned;
        feed.IsPinned = pin;

        var stored = pin
            ? await SavedFeedsService.PinAsync(SavedFeedPolicy.FeedType, feed.AtUri)
            : await SavedFeedsService.UnpinAsync(SavedFeedPolicy.FeedType, feed.AtUri);
        if (stored is not null)
            return;

        feed.IsPinned = !pin;
        await dialogs.ShowMessageAsync(
            pin ? "Couldn't pin the feed" : "Couldn't unpin the feed",
            "Your Bluesky settings couldn't be updated. Check your connection and try again.");
    }
}
