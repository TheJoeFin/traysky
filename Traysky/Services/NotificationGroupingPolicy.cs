using System;
using System.Collections.Generic;

namespace Traysky.Services;

/// <summary>The notification reasons Traysky understands. Mirrors Bluesky's, minus the ones it does not show.</summary>
public enum NotificationKind
{
    Other,
    Follow,
    Like,
    Repost,
    Mention,
    Reply,
    Quote,
    StarterPackJoined,
    Verified,
    SubscribedPost
}

/// <summary>What the grouping policy needs to know about one raw notification.</summary>
public readonly record struct NotificationInput(
    string Id,
    NotificationKind Kind,
    string AuthorDisplayName,
    string? SubjectUri,
    bool IsRead,
    DateTimeOffset IndexedAt);

/// <summary>
/// One row in the Notifications list: either a single notification, or several likes /
/// reposts / follows collapsed into "A, B and 3 others liked your post".
/// </summary>
public sealed record NotificationGroup(
    NotificationKind Kind,
    IReadOnlyList<NotificationInput> Members,
    string? SubjectUri)
{
    public NotificationInput Newest => Members[0];

    /// <summary>Unread if any member is: one new like on an old pile still deserves the dot.</summary>
    public bool IsUnread
    {
        get
        {
            foreach (NotificationInput m in Members)
            {
                if (!m.IsRead)
                    return true;
            }
            return false;
        }
    }
}

/// <summary>
/// Collapses runs of low-signal notifications the way the official app does, so ten likes on
/// one post take one row and a mention never gets buried under them. Pure .NET on purpose.
/// </summary>
public static class NotificationGroupingPolicy
{
    /// <summary>Kinds that collapse when they share a subject (or, for follows, at all).</summary>
    public static bool IsGroupable(NotificationKind kind) =>
        kind is NotificationKind.Like or NotificationKind.Repost or NotificationKind.Follow;

    /// <summary>
    /// Groups consecutive groupable notifications of the same kind and subject. Only
    /// <em>consecutive</em> ones: the list is newest-first, and a like that arrived after an
    /// intervening mention stays in its own place in time rather than jumping up to join
    /// older likes.
    /// </summary>
    public static IReadOnlyList<NotificationGroup> Group(IEnumerable<NotificationInput> notifications)
    {
        List<NotificationGroup> groups = [];
        List<NotificationInput>? open = null;
        NotificationKind openKind = default;
        string? openSubject = null;

        void Close()
        {
            if (open is not null)
                groups.Add(new NotificationGroup(openKind, open, openSubject));
            open = null;
        }

        foreach (NotificationInput n in notifications)
        {
            bool groupable = IsGroupable(n.Kind);
            string? subject = n.Kind == NotificationKind.Follow ? null : n.SubjectUri;

            if (open is not null && groupable && n.Kind == openKind && string.Equals(subject, openSubject, StringComparison.Ordinal))
            {
                open.Add(n);
                continue;
            }

            Close();

            if (groupable)
            {
                open = [n];
                openKind = n.Kind;
                openSubject = subject;
            }
            else
            {
                groups.Add(new NotificationGroup(n.Kind, [n], n.SubjectUri));
            }
        }

        Close();
        return groups;
    }

    /// <summary>
    /// "Alice liked your post", "Alice and Bob reposted your post", "Alice, Bob and 3 others
    /// followed you". Names are the display names as given; the caller decides handle vs name.
    /// </summary>
    public static string Headline(NotificationGroup group)
    {
        string verb = group.Kind switch
        {
            NotificationKind.Like => "liked your post",
            NotificationKind.Repost => "reposted your post",
            NotificationKind.Follow => "followed you",
            NotificationKind.Mention => "mentioned you",
            NotificationKind.Reply => "replied to you",
            NotificationKind.Quote => "quoted your post",
            NotificationKind.StarterPackJoined => "joined via your starter pack",
            NotificationKind.Verified => "verified you",
            NotificationKind.SubscribedPost => "posted",
            _ => "interacted with you"
        };

        return $"{Names(group)} {verb}";
    }

    private static string Names(NotificationGroup group)
    {
        IReadOnlyList<NotificationInput> m = group.Members;
        return m.Count switch
        {
            1 => m[0].AuthorDisplayName,
            2 => $"{m[0].AuthorDisplayName} and {m[1].AuthorDisplayName}",
            3 => $"{m[0].AuthorDisplayName}, {m[1].AuthorDisplayName} and {m[2].AuthorDisplayName}",
            _ => $"{m[0].AuthorDisplayName}, {m[1].AuthorDisplayName} and {m.Count - 2} others"
        };
    }
}
