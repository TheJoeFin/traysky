using idunno.Bluesky.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using Traysky.ViewModels.Items;
using ChatProfile = idunno.Bluesky.Chat.Actor.ProfileViewBasic;

namespace Traysky.Services;

/// <summary>
/// Projects chat.bsky views into the plain item models the Messages pages bind to.
/// </summary>
public static class ChatMapper
{
    public static ActorItem ToActorItem(ChatProfile profile) => new()
    {
        Did = profile.Did.ToString(),
        Handle = profile.Handle?.ToString() ?? string.Empty,
        DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? string.Empty : profile.DisplayName.Trim(),
        AvatarUri = profile.Avatar
    };

    public static ConversationItem ToConversationItem(ConversationView view, string? selfDid)
    {
        Dictionary<string, ActorItem> members = new(StringComparer.Ordinal);
        foreach (ChatProfile member in view.Members ?? [])
            members[member.Did.ToString()] = ToActorItem(member);

        List<ActorItem> others = members.Values.Where(m => !string.Equals(m.Did, selfDid, StringComparison.Ordinal)).ToList();
        ActorItem? other = others.FirstOrDefault();

        string title;
        string subtitle;
        bool isGroup = view.Kind is GroupConversation;
        if (view.Kind is GroupConversation group)
        {
            title = !string.IsNullOrWhiteSpace(group.Name)
                ? group.Name.Trim()
                : string.Join(", ", others.Select(o => o.NameOrHandle));
            subtitle = $"{group.MemberCount} members";
        }
        else
        {
            title = other?.NameOrHandle ?? "Conversation";
            subtitle = other?.HandleDisplay ?? string.Empty;
        }

        (string preview, DateTimeOffset? at) = Preview(view.LastMessage, selfDid, members, isGroup);

        return new ConversationItem
        {
            Id = view.Id,
            Title = title,
            Subtitle = subtitle,
            AvatarUri = isGroup ? null : other?.AvatarUri,
            OtherProfileKey = isGroup ? string.Empty : other?.ProfileKey ?? string.Empty,
            IsGroup = isGroup,
            IsMuted = view.Muted,
            Members = members,
            LastMessagePreview = preview,
            LastMessageAt = at,
            UnreadCount = view.UnreadCount,
            IsRequest = string.Equals(view.Status, ConversationStatus.Requested, StringComparison.Ordinal)
        };
    }

    /// <summary>
    /// A message in the thread, or null for anything shown nowhere (group system events,
    /// message kinds this version of the app doesn't know).
    /// </summary>
    public static MessageItem? ToMessageItem(MessageViewBase message, string? selfDid, IReadOnlyDictionary<string, ActorItem> members, bool isGroup)
    {
        switch (message)
        {
            case MessageView m:
            {
                string sender = m.Sender?.Did?.ToString() ?? string.Empty;
                members.TryGetValue(sender, out ActorItem? who);
                string text = m.Text ?? string.Empty;
                return new MessageItem
                {
                    Id = m.Id,
                    SenderDid = sender,
                    SenderName = who?.NameOrHandle ?? string.Empty,
                    SenderAvatarUri = who?.AvatarUri,
                    IsMine = string.Equals(sender, selfDid, StringComparison.Ordinal),
                    IsInGroup = isGroup,
                    Text = text,
                    Segments = FacetSegmenter.Segment(text, FeedMapper.ToFacetInputs(m.Facets)),
                    Embed = FeedMapper.ToEmbedItem(m.Embed),
                    Reactions = (m.Reactions ?? []).Where(r => !string.IsNullOrEmpty(r.Value))
                        .Select(r => (r.Sender?.Did?.ToString() ?? string.Empty, r.Value!)).ToList(),
                    ReactionsText = SummarizeReactions(m.Reactions?.Select(r => r.Value)),
                    SentAt = m.SentAt
                };
            }

            case DeletedMessageView d:
            {
                string sender = d.Sender?.Did?.ToString() ?? string.Empty;
                members.TryGetValue(sender, out ActorItem? who);
                return new MessageItem
                {
                    Id = d.Id,
                    SenderDid = sender,
                    SenderName = who?.NameOrHandle ?? string.Empty,
                    IsMine = string.Equals(sender, selfDid, StringComparison.Ordinal),
                    IsInGroup = isGroup,
                    IsDeleted = true,
                    SentAt = d.SentAt
                };
            }

            default:
                return null;
        }
    }

    /// <summary>"❤️ 2  😂" - each distinct reaction once, with a count when more than one person used it.</summary>
    public static string SummarizeReactions(IEnumerable<string?>? values)
    {
        if (values is null)
            return string.Empty;

        return string.Join("  ", values
            .Where(v => !string.IsNullOrEmpty(v))
            .GroupBy(v => v, StringComparer.Ordinal)
            .Select(g => g.Count() > 1 ? $"{g.Key} {g.Count()}" : g.Key));
    }

    private static (string Preview, DateTimeOffset? At) Preview(MessageViewBase? last, string? selfDid, IReadOnlyDictionary<string, ActorItem> members, bool isGroup)
    {
        switch (last)
        {
            case MessageView m:
            {
                string sender = m.Sender?.Did?.ToString() ?? string.Empty;
                string body = !string.IsNullOrWhiteSpace(m.Text) ? m.Text.ReplaceLineEndings(" ").Trim()
                    : m.Embed is not null ? "Shared a post"
                    : string.Empty;
                return (PrefixSender(body, sender, selfDid, members, isGroup), m.SentAt);
            }

            case DeletedMessageView d:
                return ("Message deleted", d.SentAt);

            default:
                return (string.Empty, null);
        }
    }

    private static string PrefixSender(string body, string sender, string? selfDid, IReadOnlyDictionary<string, ActorItem> members, bool isGroup)
    {
        if (string.Equals(sender, selfDid, StringComparison.Ordinal))
            return "You: " + body;

        if (isGroup && members.TryGetValue(sender, out ActorItem? who))
            return who.NameOrHandle + ": " + body;

        return body;
    }
}
