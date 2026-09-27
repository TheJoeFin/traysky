namespace Traysky.Services;

/// <summary>What a chat probe says about whether this session can read direct messages.</summary>
public enum ChatAccess
{
    /// <summary>Not checked yet, or the check failed for a reason that says nothing about permission (offline, 5xx, rate limited).</summary>
    Unknown,

    Allowed,

    /// <summary>The session can't reach chat: an app password made without "Allow access to your direct messages",
    /// an OAuth grant without the chat scope, or a PDS that doesn't proxy chat at all.</summary>
    Denied
}

/// <summary>Who a profile accepts new conversations from (its chat.bsky.actor.declaration).</summary>
public enum IncomingChatSetting
{
    /// <summary>No declaration record. Bluesky treats this as <see cref="Following"/>.</summary>
    NotDeclared,
    All,
    Following,
    None
}

/// <summary>
/// Decides from the outcome of a chat call whether direct messages are available to this
/// session, and whether a profile can be messaged. Pure .NET on purpose.
/// </summary>
public static class ChatAccessPolicy
{
    /// <summary>
    /// Reads a chat.bsky.convo.listConvos result. <paramref name="statusCode"/> is 0 when the
    /// request never got a response.
    /// </summary>
    public static ChatAccess FromProbe(bool succeeded, int statusCode, string? error)
    {
        if (succeeded)
            return ChatAccess.Allowed;

        // An expired access token isn't a scope problem; the next check after a refresh decides.
        if (string.Equals(error, "ExpiredToken", System.StringComparison.OrdinalIgnoreCase))
            return ChatAccess.Unknown;

        return statusCode switch
        {
            // Request timeout and rate limiting are transient.
            408 or 429 => ChatAccess.Unknown,

            // "Bad token scope" (app password without DM access) is a 400 InvalidToken, a missing
            // OAuth scope is 401/403, and a PDS without a chat proxy answers 404/501.
            >= 400 and < 500 or 501 => ChatAccess.Denied,

            _ => ChatAccess.Unknown
        };
    }

    /// <summary>
    /// Whether to offer "Message" on someone else's profile. The server has the final say
    /// (blocks, their chat being disabled); this only hides the button when it would certainly fail.
    /// </summary>
    public static bool CanOfferMessage(bool isSelf, IncomingChatSetting theirSetting, bool theyFollowYou) =>
        !isSelf && theirSetting switch
        {
            IncomingChatSetting.All => true,
            IncomingChatSetting.None => false,
            _ => theyFollowYou
        };
}
