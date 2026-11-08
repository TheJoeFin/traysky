using System;
using System.Diagnostics.CodeAnalysis;

namespace Traysky.Services;

/// <summary>
/// An account's "live" status as the app shows it: where to watch, what it is called, and when it
/// ends (null when the record carries no expiry).
/// </summary>
public sealed record LiveStatusInfo(Uri? Link, string Title, DateTimeOffset? ExpiresAt);

/// <summary>
/// Decisions for live status (<c>app.bsky.actor.status</c> with the <c>#live</c> value): is a status
/// still live, how long is left, which links and durations are acceptable. Pure .NET on purpose.
/// </summary>
public static class LiveStatusPolicy
{
    /// <summary>The <c>app.bsky.actor.status</c> value that means "streaming now".</summary>
    public const string LiveValue = "app.bsky.actor.status#live";

    /// <summary>Duration choices in the Go live dialog, in minutes.</summary>
    public static readonly int[] PresetMinutes = [30, 60, 120, 240];

    public const int DefaultMinutes = 60;

    /// <summary>What "Extend" adds, in minutes.</summary>
    public const int ExtendMinutes = 30;

    public const int TitleMaxLength = 100;

    /// <summary>Longest a status may run; keeps an extend from growing without bound.</summary>
    public const int MaxMinutes = 24 * 60;

    public static string PresetLabel(int minutes) =>
        minutes % 60 == 0 ? (minutes == 60 ? "1 hour" : $"{minutes / 60} hours") : $"{minutes} minutes";

    /// <summary>
    /// True when the status is a live one that should still show. A missing expiry or a missing
    /// <paramref name="isActive"/> flag counts as live; the server's disabled / inactive flags and
    /// an expiry at or before <paramref name="now"/> end it.
    /// </summary>
    public static bool IsLive(string? status, DateTimeOffset? expiresAt, bool? isActive, bool? isDisabled, DateTimeOffset now)
    {
        if (!string.Equals(status, LiveValue, StringComparison.Ordinal))
            return false;
        if (isDisabled == true || isActive == false)
            return false;
        return expiresAt is null || expiresAt.Value > now;
    }

    /// <summary>"1 h 12 m left", "45 m left", "Under a minute left"; empty when there is no expiry or it has passed.</summary>
    public static string RemainingText(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt is not { } end)
            return string.Empty;

        TimeSpan left = end - now;
        if (left <= TimeSpan.Zero)
            return string.Empty;

        // Round up so "1 m left" doesn't become "Under a minute" while 59 s remain.
        int minutes = (int)Math.Ceiling(left.TotalMinutes);
        if (left < TimeSpan.FromMinutes(1))
            return "Under a minute left";

        int hours = minutes / 60;
        int rest = minutes % 60;
        return hours == 0 ? $"{rest} m left"
            : rest == 0 ? $"{hours} h left"
            : $"{hours} h {rest} m left";
    }

    /// <summary>
    /// A link the user typed, as an http(s) address with a real host. "twitch.tv/me" is taken as
    /// https; anything with another scheme (file:, javascript:, ms-appdata:) is refused.
    /// </summary>
    public static bool TryNormalizeLink(string? text, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        string trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return false;

        if (!trimmed.Contains("://", StringComparison.Ordinal) && !trimmed.Contains(':', StringComparison.Ordinal))
            trimmed = "https://" + trimmed;

        if (BlueskyLinks.TryParseWebUri(trimmed, out Uri? parsed) && parsed.Host.Contains('.', StringComparison.Ordinal))
        {
            uri = parsed;
            return true;
        }

        return false;
    }

    /// <summary>The host without "www.", used as the title when none is typed.</summary>
    public static string DefaultTitle(Uri link) =>
        link.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? link.Host[4..] : link.Host;

    /// <summary>The title to send: the typed one trimmed to the limit, else the link's host.</summary>
    public static string ResolveTitle(string? typed, Uri link)
    {
        string trimmed = typed?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return DefaultTitle(link);
        return trimmed.Length <= TitleMaxLength ? trimmed : trimmed[..TitleMaxLength];
    }

    /// <summary>Clamps a duration to what the app offers (at least a minute, at most a day).</summary>
    public static int ClampMinutes(int minutes) => Math.Clamp(minutes, 1, MaxMinutes);

    /// <summary>
    /// The duration to store after adding <paramref name="addMinutes"/> to a status created at
    /// <paramref name="createdAt"/> that runs <paramref name="currentMinutes"/>: counted from the
    /// creation time, because that is what the record's duration is relative to.
    /// A status already expired is extended from <paramref name="now"/> instead.
    /// </summary>
    public static int ExtendedDuration(DateTimeOffset createdAt, int currentMinutes, int addMinutes, DateTimeOffset now)
    {
        DateTimeOffset end = createdAt.AddMinutes(currentMinutes);
        DateTimeOffset newEnd = (end > now ? end : now).AddMinutes(addMinutes);
        int minutes = (int)Math.Ceiling((newEnd - createdAt).TotalMinutes);
        return ClampMinutes(minutes);
    }

    /// <summary>
    /// A badge/strip decision for a profile: the info to show, or null when the status isn't live.
    /// </summary>
    public static LiveStatusInfo? ToInfo(string? status, string? title, string? link, DateTimeOffset? expiresAt, bool? isActive, bool? isDisabled, DateTimeOffset now)
    {
        if (!IsLive(status, expiresAt, isActive, isDisabled, now))
            return null;

        TryNormalizeLink(link, out Uri? uri);
        string resolved = string.IsNullOrWhiteSpace(title) ? (uri is null ? "Live" : DefaultTitle(uri)) : title.Trim();
        return new LiveStatusInfo(uri, resolved, expiresAt);
    }
}
