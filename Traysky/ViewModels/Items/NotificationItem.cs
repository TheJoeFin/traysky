using System;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>
/// One row of the Notifications tab: a <see cref="NotificationGroup"/> made displayable.
/// </summary>
public sealed class NotificationItem
{
    public NotificationKind Kind { get; init; }

    /// <summary>"Alice and Bob liked your post".</summary>
    public string Headline { get; init; } = string.Empty;

    public Uri? AvatarUri { get; init; }

    public string AuthorHandle { get; init; } = string.Empty;

    public string AuthorDisplayName { get; init; } = string.Empty;

    public string AuthorDid { get; init; } = string.Empty;

    /// <summary>For mentions, replies and quotes: what they wrote. For likes/reposts: your post they reacted to.</summary>
    public string? BodyText { get; init; }

    public DateTimeOffset IndexedAt { get; init; }

    public bool IsUnread { get; init; }

    /// <summary>The post to open (theirs for replies/mentions/quotes, yours for likes/reposts), if any.</summary>
    public string? SubjectAtUri { get; init; }

    public string? WebUrl { get; init; }

    /// <summary>The full post, when the notification is itself a post (mention, reply, quote), so it can be replied to.</summary>
    public PostItem? Post { get; init; }

    /// <summary>
    /// The post a tap on the row opens: theirs for mentions/replies/quotes, yours for likes and
    /// reposts. Null when it could not be hydrated (deleted, blocked, or a follow).
    /// </summary>
    public PostItem? SubjectPost { get; init; }

    /// <summary>"Alice liked this post" - the headline rephrased for the post page it opens.</summary>
    public string ContextForPostPage => Kind switch
    {
        NotificationKind.Like => Headline.Replace(" liked your post", " liked this post"),
        NotificationKind.Repost => Headline.Replace(" reposted your post", " reposted this post"),
        NotificationKind.Reply => "Reply to you",
        NotificationKind.Mention => "Mentions you",
        NotificationKind.Quote => "Quotes your post",
        _ => Headline
    };

    public string TimeAgo => RelativeTimeFormatter.Format(IndexedAt, DateTimeOffset.UtcNow);

    public string AbsoluteTime => RelativeTimeFormatter.FormatAbsolute(IndexedAt);

    public bool HasBody => !string.IsNullOrWhiteSpace(BodyText);

    public bool HasPost => Post is not null;

    // Segoe Fluent Icons glyphs per kind.
    public string Glyph => Kind switch
    {
        NotificationKind.Like => "",
        NotificationKind.Repost => "",
        NotificationKind.Follow => "",
        NotificationKind.Mention => "",
        NotificationKind.Reply => "",
        NotificationKind.Quote => "",
        NotificationKind.StarterPackJoined => "",
        NotificationKind.Verified => "",
        NotificationKind.SubscribedPost => "",
        _ => ""
    };

    /// <summary>Likes read pink, reposts green, everything else the accent - same as the official app.</summary>
    public string GlyphBrushKey => Kind switch
    {
        NotificationKind.Like => "SystemFillColorCriticalBrush",
        NotificationKind.Repost => "SystemFillColorSuccessBrush",
        _ => "AccentTextFillColorPrimaryBrush"
    };
}
