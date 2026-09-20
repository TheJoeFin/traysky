using System;

namespace Traysky.Services;

/// <summary>
/// Builds bsky.app URLs from AT URIs and tidies user-typed handles. Pure .NET on purpose.
/// </summary>
public static class BlueskyLinks
{
    public const string AppPasswordsUrl = "https://bsky.app/settings/app-passwords";

    /// <summary>
    /// The web URL for a post, given its AT URI (<c>at://did:plc:xyz/app.bsky.feed.post/3k2…</c>)
    /// and optionally the author's handle for a nicer link. Null when the URI is not a post.
    /// </summary>
    public static string? PostUrl(string? atUri, string? authorHandle = null)
    {
        if (!TryParseAtUri(atUri, out string authority, out string collection, out string rkey))
            return null;

        if (!string.Equals(collection, "app.bsky.feed.post", StringComparison.Ordinal))
            return null;

        string actor = string.IsNullOrWhiteSpace(authorHandle) ? authority : authorHandle;
        return $"https://bsky.app/profile/{actor}/post/{rkey}";
    }

    public static string ProfileUrl(string didOrHandle) => $"https://bsky.app/profile/{didOrHandle}";

    public static string HashtagUrl(string tag) => $"https://bsky.app/hashtag/{Uri.EscapeDataString(tag.TrimStart('#'))}";

    /// <summary>
    /// What the user typed into the handle box, made acceptable to the API: trimmed, without the
    /// leading @ people naturally include, and lower-cased since handles are case-insensitive.
    /// </summary>
    public static string NormalizeHandle(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return string.Empty;

        string handle = typed.Trim().TrimStart('@').Trim();

        // "alice" alone is almost always meant as alice.bsky.social.
        if (!handle.Contains('.') && !handle.StartsWith("did:", StringComparison.OrdinalIgnoreCase) && handle.Length > 0)
            handle += ".bsky.social";

        return handle.StartsWith("did:", StringComparison.OrdinalIgnoreCase) ? handle : handle.ToLowerInvariant();
    }

    public static bool TryParseAtUri(string? atUri, out string authority, out string collection, out string rkey)
    {
        authority = collection = rkey = string.Empty;

        if (string.IsNullOrEmpty(atUri) || !atUri.StartsWith("at://", StringComparison.Ordinal))
            return false;

        string[] parts = atUri[5..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1)
            return false;

        authority = parts[0];
        if (parts.Length >= 2)
            collection = parts[1];
        if (parts.Length >= 3)
            rkey = parts[2];

        return true;
    }
}
