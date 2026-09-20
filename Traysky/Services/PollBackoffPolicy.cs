using System;

namespace Traysky.Services;

/// <summary>
/// How long to wait before the next unread-count poll. Pure .NET on purpose.
/// </summary>
public static class PollBackoffPolicy
{
    public static readonly TimeSpan MaxInterval = TimeSpan.FromMinutes(10);

    /// <summary>The intervals Settings offers, in seconds.</summary>
    public static readonly int[] IntervalChoicesSeconds = [30, 60, 120, 300];

    public const int DefaultIntervalSeconds = 60;

    /// <summary>
    /// Doubles the configured interval per consecutive failure, capped at
    /// <see cref="MaxInterval"/>, so a server that is down gets a few polite retries and then
    /// one call every ten minutes instead of one every thirty seconds forever.
    /// </summary>
    public static TimeSpan NextInterval(TimeSpan configured, int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
            return configured;

        double factor = Math.Pow(2, Math.Min(consecutiveFailures, 10));
        TimeSpan backedOff = TimeSpan.FromTicks((long)(configured.Ticks * factor));
        return backedOff > MaxInterval ? MaxInterval : backedOff;
    }

    /// <summary>
    /// When the server says it is rate limiting us, wait until the limit resets (plus a little
    /// slack) rather than the usual backoff, but never longer than the cap.
    /// </summary>
    public static TimeSpan UntilRateLimitReset(DateTimeOffset resetAtUtc, DateTimeOffset nowUtc)
    {
        TimeSpan wait = resetAtUtc - nowUtc + TimeSpan.FromSeconds(5);
        if (wait < TimeSpan.FromSeconds(5))
            wait = TimeSpan.FromSeconds(5);
        return wait > MaxInterval ? MaxInterval : wait;
    }

    /// <summary>Clamps a stored interval to something on the menu, so a hand-edited setting cannot hammer the API.</summary>
    public static int SanitizeIntervalSeconds(int stored)
    {
        foreach (int choice in IntervalChoicesSeconds)
        {
            if (choice == stored)
                return stored;
        }
        return DefaultIntervalSeconds;
    }
}
