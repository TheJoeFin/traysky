using idunno.Bluesky.Actor;
using idunno.Bluesky.Embed;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Traysky.Services;

/// <summary>
/// The signed-in account's "live" status (<c>app.bsky.actor.status</c>): go live with a link and a
/// duration, extend it, end it. Nothing is cached or polled here; the profile page holds the state
/// it shows and runs a timer only while a live status is on screen. Each call returns null / false
/// (with a message where there is one) and logs the reason on failure.
/// </summary>
public static class LiveStatusService
{
    /// <summary>Maps the status on a profile view to what the profile page shows; null when it isn't a live one.</summary>
    public static LiveStatusInfo? FromView(StatusView? view)
    {
        if (view is null)
            return null;

        string? link = null, title = null;
        if (view.Embed is EmbeddedExternalView { External: { } external })
        {
            link = external.Uri;
            title = external.Title;
        }

        return LiveStatusPolicy.ToInfo(view.Status, title, link, view.ExpiresAt, view.IsActive, view.IsDisabled, DateTimeOffset.UtcNow);
    }

    public static async Task<(LiveStatusInfo? Info, string? Error)> GoLiveAsync(Uri link, string title, int minutes)
    {
        minutes = LiveStatusPolicy.ClampMinutes(minutes);
        string resolved = LiveStatusPolicy.ResolveTitle(title, link);
        LiveStatusInfo info = new(link, resolved, DateTimeOffset.UtcNow.AddMinutes(minutes));

        if (PreviewMode.IsEnabled)
            return (info, null);

        if (!BlueskySessionService.Instance.IsSignedIn)
            return (null, "You're signed out.");

        try
        {
            var result = await BlueskySessionService.Instance.Agent.CreateLiveStatus(link, resolved, string.Empty, null, minutes, CancellationToken.None);
            if (result.Succeeded)
                return (info, null);

            LogService.Warn("LiveStatus", $"CreateLiveStatus failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error} {result.AtErrorDetail?.Message}");
            return (null, Failure(result.StatusCode, result.AtErrorDetail?.Message));
        }
        catch (Exception ex)
        {
            LogService.Warn("LiveStatus", $"CreateLiveStatus threw: {ex.Message}");
            return (null, "Couldn't go live.");
        }
    }

    /// <summary>Adds <paramref name="addMinutes"/> to the running status. The returned info keeps <paramref name="current"/>'s link and title.</summary>
    public static async Task<(LiveStatusInfo? Info, string? Error)> ExtendAsync(LiveStatusInfo current, int addMinutes)
    {
        if (PreviewMode.IsEnabled)
        {
            DateTimeOffset from = current.ExpiresAt is { } e && e > DateTimeOffset.UtcNow ? e : DateTimeOffset.UtcNow;
            return (current with { ExpiresAt = from.AddMinutes(addMinutes) }, null);
        }

        if (!BlueskySessionService.Instance.IsSignedIn)
            return (null, "You're signed out.");

        try
        {
            var agent = BlueskySessionService.Instance.Agent;
            var existing = await agent.GetStatus(CancellationToken.None);
            if (!existing.Succeeded || existing.Result is null)
            {
                LogService.Warn("LiveStatus", $"GetStatus failed: {(int)existing.StatusCode} {existing.AtErrorDetail?.Error}");
                return (null, Failure(existing.StatusCode, null));
            }

            var record = existing.Result;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            int currentMinutes = record.Value.DurationMinutes ?? 0;
            int minutes = LiveStatusPolicy.ExtendedDuration(record.Value.CreatedAt, currentMinutes, addMinutes, now);
            record.Value.DurationMinutes = minutes;

            var result = await agent.UpdateStatus(record, CancellationToken.None);
            if (!result.Succeeded)
            {
                LogService.Warn("LiveStatus", $"UpdateStatus failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return (null, Failure(result.StatusCode, result.AtErrorDetail?.Message));
            }

            return (current with { ExpiresAt = record.Value.CreatedAt.AddMinutes(minutes) }, null);
        }
        catch (Exception ex)
        {
            LogService.Warn("LiveStatus", $"Extend threw: {ex.Message}");
            return (null, "Couldn't extend the stream.");
        }
    }

    public static async Task<bool> EndAsync()
    {
        if (PreviewMode.IsEnabled)
            return true;

        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.DeleteStatus(CancellationToken.None);
            if (result.Succeeded)
                return true;

            LogService.Warn("LiveStatus", $"DeleteStatus failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("LiveStatus", $"DeleteStatus threw: {ex.Message}");
        }
        return false;
    }

    private static string Failure(System.Net.HttpStatusCode status, string? serverMessage) =>
        status == 0 ? "Couldn't reach Bluesky. Check your connection."
        : !string.IsNullOrWhiteSpace(serverMessage) ? serverMessage
        : "Bluesky didn't accept that. Check the link and try again.";
}
