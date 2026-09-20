using CommunityToolkit.Mvvm.ComponentModel;
using idunno.AtProto.Repo;
using System;
using System.Collections.Generic;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>
/// One post as the timeline, a thread or a notification shows it. Projected once from the
/// library's <c>PostView</c> so the XAML binds to plain properties, and observable for the
/// handful of things the user can change from the card (like / repost state and counts).
/// </summary>
public sealed partial class PostItem : ObservableObject
{
    public string AtUri { get; init; } = string.Empty;
    public string Cid { get; init; } = string.Empty;
    public StrongReference Reference { get; init; } = null!;

    public string AuthorDid { get; init; } = string.Empty;
    public string AuthorHandle { get; init; } = string.Empty;
    public string AuthorDisplayName { get; init; } = string.Empty;
    public Uri? AvatarUri { get; init; }

    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<TextSegment> Segments { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>"♲ Reposted by Sam" when the timeline shows this because someone reposted it.</summary>
    public string? RepostedBy { get; init; }

    /// <summary>The handle this post replies to, when it is a reply.</summary>
    public string? ReplyingTo { get; init; }

    public EmbedItem? Embed { get; init; }

    public string? WebUrl { get; init; }

    [ObservableProperty]
    public partial int LikeCount { get; set; }

    [ObservableProperty]
    public partial int RepostCount { get; set; }

    [ObservableProperty]
    public partial int ReplyCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LikeGlyph))]
    public partial bool IsLiked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepostGlyph))]
    public partial bool IsReposted { get; set; }

    /// <summary>AT URI of the viewer's like record; needed to undo the like.</summary>
    public string? LikeUri { get; set; }

    /// <summary>AT URI of the viewer's repost record; needed to undo the repost.</summary>
    public string? RepostUri { get; set; }

    /// <summary>Set while a like/repost request is in flight so a double-click cannot send two.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string TimeAgo => RelativeTimeFormatter.Format(CreatedAt, DateTimeOffset.UtcNow);

    public string AbsoluteTime => RelativeTimeFormatter.FormatAbsolute(CreatedAt);

    public string HandleDisplay => "@" + AuthorHandle;

    public bool HasRepostedBy => !string.IsNullOrEmpty(RepostedBy);

    public bool HasReplyingTo => !string.IsNullOrEmpty(ReplyingTo);

    public string ReplyingToDisplay => HasReplyingTo ? $"Replying to @{ReplyingTo}" : string.Empty;

    public bool HasEmbed => Embed is not null && Embed.Kind != EmbedKind.None;

    public bool HasText => !string.IsNullOrEmpty(Text);

    // Segoe Fluent Icons: filled heart / outline heart, and the repost arrows.
    public string LikeGlyph => IsLiked ? "" : "";

    public string RepostGlyph => "";

    public string LikeCountDisplay => LikeCount > 0 ? LikeCount.ToString() : string.Empty;
    public string RepostCountDisplay => RepostCount > 0 ? RepostCount.ToString() : string.Empty;
    public string ReplyCountDisplay => ReplyCount > 0 ? ReplyCount.ToString() : string.Empty;

    partial void OnLikeCountChanged(int value) => OnPropertyChanged(nameof(LikeCountDisplay));

    partial void OnRepostCountChanged(int value) => OnPropertyChanged(nameof(RepostCountDisplay));

    partial void OnReplyCountChanged(int value) => OnPropertyChanged(nameof(ReplyCountDisplay));

    /// <summary>Re-raises the relative timestamp so a list that has been open a while stays honest.</summary>
    public void RefreshTimeAgo() => OnPropertyChanged(nameof(TimeAgo));
}
