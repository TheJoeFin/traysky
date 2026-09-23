using CommunityToolkit.Mvvm.ComponentModel;
using idunno.AtProto;
using Microsoft.UI.Dispatching;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Traysky.Services;

public sealed class UnreadChangedEventArgs(int previous, int current) : EventArgs
{
    public int Previous { get; } = previous;
    public int Current { get; } = current;
}

/// <summary>
/// Asks Bluesky for the unread notification count on a timer while signed in, and turns the
/// answer into the tray badge. One in-flight request at a time, exponential backoff on
/// failure, and a pause when Windows says there is no internet. Everything observable here
/// changes on the UI thread.
/// </summary>
public sealed partial class NotificationPollService : ObservableObject
{
    private static readonly Lazy<NotificationPollService> _instance = new(() => new NotificationPollService());

    public static NotificationPollService Instance => _instance.Value;

    private readonly DispatcherQueueTimer _timer;
    private readonly BlueskySessionService _session = BlueskySessionService.Instance;
    private int _inFlight;
    private int _consecutiveFailures;
    private DateTimeOffset? _rateLimitedUntilUtc;

    private NotificationPollService()
    {
        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("NotificationPollService must be created on the UI thread.");

        _timer = dispatcher.CreateTimer();
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => _ = PollAsync();

        _session.SignedIn += (_, _) => Start();
        _session.SignedOut += (_, _) => Stop();
        SettingsService.PollIntervalChanged += (_, _) => Rearm();
    }

    [ObservableProperty]
    public partial int UnreadCount { get; private set; }

    /// <summary>True when the last poll could not reach Bluesky (or Windows reports no internet).</summary>
    [ObservableProperty]
    public partial bool IsOffline { get; private set; }

    [ObservableProperty]
    public partial DateTimeOffset? LastSuccessfulPollUtc { get; private set; }

    /// <summary>Raised on the UI thread whenever the count changes; the toast service watches for rises.</summary>
    public event EventHandler<UnreadChangedEventArgs>? UnreadChanged;

    public void Start()
    {
        if (!_session.IsSignedIn)
            return;

        _consecutiveFailures = 0;
        Rearm();
        _ = PollAsync();
    }

    public void Stop()
    {
        _timer.Stop();
        SetUnread(0);
        IsOffline = false;
    }

    /// <summary>The user opened the flyout or hit refresh: check now rather than at the next tick.</summary>
    public Task PollNowAsync() => PollAsync();

    /// <summary>
    /// The user just looked at the notifications tab; drop the badge right away instead of
    /// waiting for the server to reflect the seen-at update on the next poll.
    /// </summary>
    public void MarkSeenLocally()
    {
        SetUnread(0);
    }

    private void Rearm()
    {
        TimeSpan configured = TimeSpan.FromSeconds(SettingsService.PollIntervalSeconds);
        TimeSpan interval = PollBackoffPolicy.NextInterval(configured, _consecutiveFailures);

        if (_rateLimitedUntilUtc is DateTimeOffset reset && reset > DateTimeOffset.UtcNow)
            interval = PollBackoffPolicy.UntilRateLimitReset(reset, DateTimeOffset.UtcNow);

        _timer.Interval = interval;
        _timer.Stop();
        _timer.Start();
    }

    private async Task PollAsync()
    {
        if (!_session.IsSignedIn)
            return;

        if (PreviewMode.IsEnabled)
        {
            SetUnread(2);
            LastSuccessfulPollUtc = DateTimeOffset.UtcNow;
            return;
        }

        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
            return;

        try
        {
            if (!NetworkStatusService.IsInternetAvailable())
            {
                IsOffline = true;
                return;
            }

            if (!await _session.EnsureAuthenticatedAsync().ConfigureAwait(true))
            {
                // A rejected refresh token has already signed the user out; anything else
                // means Bluesky could not be reached to refresh, which is offline.
                if (_session.IsSignedIn)
                {
                    _consecutiveFailures++;
                    IsOffline = true;
                    Rearm();
                }
                return;
            }

            AtProtoHttpResult<int> result = await _session.Agent.GetNotificationUnreadCount().ConfigureAwait(true);

            if (result.Succeeded)
            {
                bool wasBackedOff = _consecutiveFailures > 0 || _rateLimitedUntilUtc is not null;
                _consecutiveFailures = 0;
                _rateLimitedUntilUtc = null;
                IsOffline = false;
                // Count first, then the poll timestamp: the announcer takes the first
                // successful poll as its baseline, so this order keeps the count found on
                // launch from being toasted as if it were new.
                SetUnread(result.Result);
                LastSuccessfulPollUtc = DateTimeOffset.UtcNow;

                if (wasBackedOff)
                    Rearm();
                return;
            }

            _consecutiveFailures++;
            LogService.Warn("Poll", $"Unread count failed ({(int)result.StatusCode} {result.AtErrorDetail?.Error}); failures={_consecutiveFailures}");

            if (result.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                DateTimeOffset? reset = result.RateLimit?.Reset;
                _rateLimitedUntilUtc = reset ?? DateTimeOffset.UtcNow.AddMinutes(5);
            }

            // Auth failures are the session service's problem; everything else reads as offline.
            if (result.StatusCode != System.Net.HttpStatusCode.Unauthorized)
                IsOffline = true;

            Rearm();
        }
        catch (AuthenticationRequiredException)
        {
            // The token expired between the check above and the call; the next poll refreshes it.
            LogService.Warn("Poll", "Unread count needed a fresh access token; retrying next poll");
            Rearm();
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            IsOffline = true;
            LogService.Warn("Poll", $"Unread count threw: {ex.GetType().Name}: {ex.Message}");
            Rearm();
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    private void SetUnread(int count)
    {
        int previous = UnreadCount;
        if (previous == count)
            return;

        UnreadCount = count;
        UnreadChanged?.Invoke(this, new UnreadChangedEventArgs(previous, count));
    }
}
