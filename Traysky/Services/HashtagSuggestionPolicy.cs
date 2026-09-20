using System;
using System.Collections.Generic;

namespace Traysky.Services;

/// <summary>
/// Pure logic behind the RichSuggestBox '#' popup's suggestion list. Bluesky has no tag-search
/// API, so "recently used, filtered by what you're typing" is the whole suggestion source: MRU
/// ordering, case-insensitive de-dup, a cap so the settings value doesn't grow forever, and
/// prefix filtering as the user types after '#'.
/// </summary>
public static class HashtagSuggestionPolicy
{
    public const int MaxRecents = 20;

    /// <summary>Moves <paramref name="tag"/> to the front of <paramref name="recents"/>, de-duping case-insensitively and capping the list.</summary>
    public static IReadOnlyList<string> WithRecentTag(IReadOnlyList<string> recents, string? tag)
    {
        string normalized = Normalize(tag);
        if (normalized.Length == 0)
            return recents;

        var updated = new List<string>(recents.Count + 1) { normalized };
        foreach (string existing in recents)
        {
            if (!string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                updated.Add(existing);
        }

        if (updated.Count > MaxRecents)
            updated.RemoveRange(MaxRecents, updated.Count - MaxRecents);

        return updated;
    }

    /// <summary>Recent tags starting with <paramref name="query"/> (case-insensitive), most-recent first.</summary>
    public static IReadOnlyList<string> Filter(IReadOnlyList<string> recents, string? query)
    {
        if (string.IsNullOrEmpty(query))
            return recents;

        var matches = new List<string>();
        foreach (string tag in recents)
        {
            if (tag.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                matches.Add(tag);
        }
        return matches;
    }

    /// <summary>Strips a leading '#' and surrounding whitespace so recents and comparisons are consistent.</summary>
    public static string Normalize(string? tag) => (tag ?? string.Empty).Trim().TrimStart('#').Trim();
}
