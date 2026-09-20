using idunno.AtProto;
using System;

namespace Traysky.ViewModels.Items;

/// <summary>What kind of feed a <see cref="FeedTabItem"/> points at, and therefore which API fetches it.</summary>
public enum FeedTabKind
{
    /// <summary>The account's algorithmic home timeline ("Following"). <see cref="FeedTabItem.Uri"/> is null.</summary>
    Timeline,

    /// <summary>A feed generator, fetched with <c>GetFeed</c>.</summary>
    Feed,

    /// <summary>A user list shown as a feed, fetched with <c>GetListFeed</c>.</summary>
    List
}

/// <summary>
/// One tab in the shell's feed row: the account's own pinned feeds, in the order and with the
/// visibility <see cref="Services.FeedsService"/> read from their Bluesky preferences - the same
/// list and order bsky.app shows across the top of the following screen.
/// </summary>
public sealed class FeedTabItem
{
    public FeedTabKind Kind { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The feed generator or list's AT URI. Null for <see cref="FeedTabKind.Timeline"/>.</summary>
    public AtUri? Uri { get; init; }

    public Uri? AvatarUri { get; init; }

    public string IconGlyph => Kind switch
    {
        FeedTabKind.Timeline => "", // Home
        FeedTabKind.List => "",     // BulletedList
        _ => ""                     // Streams (closest Fluent glyph to a feed/RSS icon)
    };

    /// <summary>Stable identity for comparing tabs across a preferences refresh.</summary>
    public string Key => Uri?.ToString() ?? "timeline";

    public override bool Equals(object? obj) => obj is FeedTabItem other && Key == other.Key;

    public override int GetHashCode() => Key.GetHashCode();
}
