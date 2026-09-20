using System.Globalization;

namespace Traysky.Services;

/// <summary>
/// Bluesky's post length rules. The limit is counted in <em>graphemes</em> (what a person sees
/// as one character), so "👨‍👩‍👧" is one, not three or eleven. Pure .NET on purpose.
/// </summary>
public static class PostTextPolicy
{
    public const int MaxGraphemes = 300;

    /// <summary>The count at which the counter turns amber, warning the user before they hit the wall.</summary>
    public const int WarnAtGraphemes = 280;

    public static int CountGraphemes(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return new StringInfo(text).LengthInTextElements;
    }

    public static int Remaining(string? text) => MaxGraphemes - CountGraphemes(text);

    public static bool IsOverLimit(string? text) => CountGraphemes(text) > MaxGraphemes;

    public static bool IsNearLimit(string? text) => CountGraphemes(text) >= WarnAtGraphemes;

    /// <summary>Whether there is something worth sending: not empty, not whitespace, not over the limit.</summary>
    public static bool CanPost(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return !IsOverLimit(text);
    }
}
