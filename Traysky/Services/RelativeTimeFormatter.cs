using System;
using System.Globalization;

namespace Traysky.Services;

/// <summary>
/// The compact "2m" / "3h" / "Yesterday" timestamps social feeds use. Pure .NET on purpose.
/// </summary>
public static class RelativeTimeFormatter
{
    public static string Format(DateTimeOffset when, DateTimeOffset now, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        TimeSpan age = now - when;

        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero; // clock skew: a post from "the future" is simply new

        if (age < TimeSpan.FromMinutes(1))
            return "now";

        if (age < TimeSpan.FromHours(1))
            return $"{(int)age.TotalMinutes}m";

        if (age < TimeSpan.FromHours(24))
            return $"{(int)age.TotalHours}h";

        if (age < TimeSpan.FromDays(7))
            return $"{(int)age.TotalDays}d";

        DateTime local = when.ToLocalTime().DateTime;
        DateTime localNow = now.ToLocalTime().DateTime;

        return local.Year == localNow.Year
            ? local.ToString("MMM d", culture)
            : local.ToString("MMM d, yyyy", culture);
    }

    /// <summary>The full timestamp for a tooltip, in the user's locale.</summary>
    public static string FormatAbsolute(DateTimeOffset when, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return when.ToLocalTime().ToString("f", culture);
    }
}
