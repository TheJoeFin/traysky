using idunno.AtProto;
using idunno.Bluesky;
using idunno.Bluesky.Notifications;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Traysky.Models;

namespace Traysky.Services;

/// <summary>
/// Turns a rise in the unread count into toasts. Mentions, replies and quotes get one toast
/// each with the text; likes, reposts and follows are rolled into one summary toast. Nothing
/// is announced for the count the app finds on launch - the badge covers that - only for
/// increases seen while running, and only while the flyout is not already open.
/// </summary>
public sealed class NotificationAnnouncer
{
    private readonly NotificationPollService _poll = NotificationPollService.Instance;
    private readonly Func<bool> _isFlyoutVisible;
    private bool _hasBaseline;
    private DateTimeOffset _lastAnnouncedIndexedAt = DateTimeOffset.MinValue;

    public NotificationAnnouncer(Func<bool> isFlyoutVisible)
    {
        _isFlyoutVisible = isFlyoutVisible;
        _poll.PropertyChanged += OnPollPropertyChanged;
        _poll.UnreadChanged += OnUnreadChanged;
        BlueskySessionService.Instance.SignedOut += (_, _) => { _hasBaseline = false; _lastAnnouncedIndexedAt = DateTimeOffset.MinValue; };
    }

    private void OnPollPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The first successful poll after sign-in establishes what "already there" means.
        if (e.PropertyName == nameof(NotificationPollService.LastSuccessfulPollUtc) && _poll.LastSuccessfulPollUtc is not null)
        {
            if (!_hasBaseline)
                _lastAnnouncedIndexedAt = DateTimeOffset.UtcNow;
            _hasBaseline = true;
        }
    }

    private void OnUnreadChanged(object? sender, UnreadChangedEventArgs e)
    {
        if (!_hasBaseline || e.Current <= e.Previous || _isFlyoutVisible())
            return;

        _ = AnnounceAsync(Math.Min(e.Current - e.Previous, 20));
    }

    private async Task AnnounceAsync(int expectedNew)
    {
        if (!AnyToastEnabled())
            return;

        try
        {
            AtProtoHttpResult<NotificationCollection> result = await BlueskySessionService.Instance.Agent.ListNotifications(limit: Math.Max(expectedNew, 5));
            if (!result.Succeeded || result.Result is null)
                return;

            int likes = 0, reposts = 0, follows = 0;
            List<Notification> individual = [];
            DateTimeOffset newestSeen = _lastAnnouncedIndexedAt;

            foreach (Notification n in result.Result)
            {
                if (n.IsRead || n.IndexedAt <= _lastAnnouncedIndexedAt || n.Author is null)
                    continue;

                if (n.IndexedAt > newestSeen)
                    newestSeen = n.IndexedAt;

                switch (FeedMapper.ToKind(n.Reason))
                {
                    case NotificationKind.Mention when SettingsService.ToastMentions:
                    case NotificationKind.Reply when SettingsService.ToastReplies:
                    case NotificationKind.Quote when SettingsService.ToastQuotes:
                        individual.Add(n);
                        break;
                    case NotificationKind.Like when SettingsService.ToastLikesReposts:
                        likes++;
                        break;
                    case NotificationKind.Repost when SettingsService.ToastLikesReposts:
                        reposts++;
                        break;
                    case NotificationKind.Follow when SettingsService.ToastFollows:
                        follows++;
                        break;
                }
            }

            _lastAnnouncedIndexedAt = newestSeen;

            // Oldest first so the newest ends up on top of the stack.
            for (int i = individual.Count - 1; i >= 0 && i >= individual.Count - 3; i--)
            {
                Notification n = individual[i];
                string who = FeedMapper.DisplayNameOf(n.Author);
                string verb = FeedMapper.ToKind(n.Reason) switch
                {
                    NotificationKind.Reply => "replied",
                    NotificationKind.Quote => "quoted you",
                    _ => "mentioned you"
                };
                string body = (n.Record as Post)?.Text ?? string.Empty;
                ToastService.Show($"{who} {verb}", body, ShellDestination.Notifications, n.Author.Avatar);
            }

            List<string> parts = [];
            if (likes > 0) parts.Add($"{likes} new like{(likes == 1 ? "" : "s")}");
            if (reposts > 0) parts.Add($"{reposts} new repost{(reposts == 1 ? "" : "s")}");
            if (follows > 0) parts.Add($"{follows} new follower{(follows == 1 ? "" : "s")}");

            if (parts.Count > 0)
                ToastService.Show("Bluesky", string.Join(", ", parts), ShellDestination.Notifications);
        }
        catch (Exception ex)
        {
            LogService.Warn("Announce", $"Failed: {ex.Message}");
        }
    }

    private static bool AnyToastEnabled() =>
        SettingsService.ToastMentions || SettingsService.ToastReplies || SettingsService.ToastQuotes
        || SettingsService.ToastFollows || SettingsService.ToastLikesReposts;
}
