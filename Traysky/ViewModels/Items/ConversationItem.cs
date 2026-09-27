using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>
/// One row of the Messages list, projected from the library's <c>ConversationView</c> (see
/// <see cref="ChatMapper.ToConversationItem"/>). Observable for what changes while the list is
/// on screen: the last message, the unread count and accepting a request.
/// </summary>
public sealed partial class ConversationItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;

    /// <summary>The other person's name in a 1:1 conversation, or the group's name.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>"@handle" for a 1:1 conversation, "N members" for a group.</summary>
    public string Subtitle { get; init; } = string.Empty;

    public Uri? AvatarUri { get; init; }

    /// <summary>The other person in a 1:1 conversation (their DID, else handle); empty for a group.</summary>
    public string OtherProfileKey { get; init; } = string.Empty;

    public bool IsGroup { get; init; }

    public bool IsMuted { get; init; }

    /// <summary>Everyone in the conversation, by DID, so a message can show who sent it.</summary>
    public IReadOnlyDictionary<string, ActorItem> Members { get; init; } = new Dictionary<string, ActorItem>();

    [ObservableProperty]
    public partial string LastMessagePreview { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeAgo), nameof(AbsoluteTime))]
    public partial DateTimeOffset? LastMessageAt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnread))]
    public partial long UnreadCount { get; set; }

    /// <summary>Someone who isn't followed started this conversation and it hasn't been accepted yet.</summary>
    [ObservableProperty]
    public partial bool IsRequest { get; set; }

    public bool IsUnread => UnreadCount > 0;

    public string TimeAgo => LastMessageAt is { } at ? RelativeTimeFormatter.Format(at, DateTimeOffset.UtcNow) : string.Empty;

    public string AbsoluteTime => LastMessageAt is { } at ? RelativeTimeFormatter.FormatAbsolute(at) : string.Empty;
}
