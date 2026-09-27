using System;

namespace Traysky.Services;

/// <summary>
/// Layout decisions for a conversation's message list. Pure .NET on purpose.
/// </summary>
public static class ChatThreadPolicy
{
    /// <summary>Messages from the same sender closer together than this read as one burst.</summary>
    public static readonly TimeSpan RunGap = TimeSpan.FromMinutes(5);

    /// <summary>The most characters one message may carry (chat.bsky.convo.defs#messageInput allows 1000 graphemes).</summary>
    public const int MaxMessageLength = 1000;

    /// <summary>
    /// True when a message starts a new visual run: the first message, a different sender, or a
    /// long enough pause. The first message of a run shows the timestamp (and, in a group, the
    /// sender's name); the rest stack tightly under it.
    /// </summary>
    public static bool StartsRun(string? previousSenderDid, DateTimeOffset? previousSentAt, string senderDid, DateTimeOffset sentAt) =>
        previousSenderDid is null
        || previousSentAt is null
        || !string.Equals(previousSenderDid, senderDid, StringComparison.Ordinal)
        || sentAt - previousSentAt.Value > RunGap;

    /// <summary>Whether the send button should be enabled for <paramref name="draft"/>.</summary>
    public static bool CanSend(string? draft) =>
        !string.IsNullOrWhiteSpace(draft) && draft.Trim().Length <= MaxMessageLength;
}
