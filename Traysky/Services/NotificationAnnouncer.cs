using idunno.AtProto;
using idunno.Bluesky;
using idunno.Bluesky.Notifications;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
    // Toast tag -> the notification URIs it announced, so it can be removed once they are all read.
    private readonly Dictionary<string, List<string>> _toasts = [];
    private int _tagSeq;
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
        if (e.Current == 0)
        {
            // Everything is read (here or on another client): nothing should linger.
            if (_toasts.Count > 0)
            {
                ToastService.ClearAll();
                _toasts.Clear();
            }
            return;
        }

        if (e.Current < e.Previous && _toasts.Count > 0)
        {
            _ = ClearReadToastsAsync();
            return;
        }

        if (!_hasBaseline || e.Current <= e.Previous || _isFlyoutVisible())
            return;

        _ = AnnounceAsync(Math.Min(e.Current - e.Previous, 20));
    }

    /// <summary>Removes the toasts whose notifications have since been read.</summary>
    private async Task ClearReadToastsAsync()
    {
        try
        {
            AtProtoHttpResult<NotificationCollection> result = await BlueskySessionService.Instance.Agent.ListNotifications(limit: 50);
            if (!result.Succeeded || result.Result is null)
                return;

            HashSet<string> read = [];
            foreach (Notification n in result.Result)
            {
                if (n.IsRead)
                    read.Add(n.Uri.ToString());
            }

            foreach (KeyValuePair<string, List<string>> toast in _toasts.ToArray())
            {
                if (toast.Value.TrueForAll(read.Contains))
                {
                    ToastService.RemoveByTag(toast.Key);
                    _toasts.Remove(toast.Key);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("Announce", $"Clearing read toasts failed: {ex.Message}");
        }
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
            List<string> summaryUris = [];
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
                    case NotificationKind.SubscribedPost when SettingsService.ToastSubscribedPosts:
                        individual.Add(n);
                        break;
                    case NotificationKind.Like when SettingsService.ToastLikesReposts:
                        likes++;
                        summaryUris.Add(n.Uri.ToString());
                        break;
                    case NotificationKind.Repost when SettingsService.ToastLikesReposts:
                        reposts++;
                        summaryUris.Add(n.Uri.ToString());
                        break;
                    case NotificationKind.Follow when SettingsService.ToastFollows:
                        follows++;
                        summaryUris.Add(n.Uri.ToString());
                        break;
                }
            }

            _lastAnnouncedIndexedAt = newestSeen;

            // Oldest first so the newest ends up on top of the stack.
            for (int i = individual.Count - 1; i >= 0 && i >= individual.Count - 3; i--)
            {
                Notification n = individual[i];
                string who = FeedMapper.DisplayNameOf(n.Author);
                string verb = NotificationGroupingPolicy.ToastVerb(FeedMapper.ToKind(n.Reason));
                string body = (n.Record as Post)?.Text ?? string.Empty;
                string tag = NextTag();
                _toasts[tag] = [n.Uri.ToString()];
                ToastService.Show($"{who} {verb}", body, ShellDestination.Notifications, n.Author.Avatar, tag);
            }

            List<string> parts = [];
            if (likes > 0) parts.Add($"{likes} new like{(likes == 1 ? "" : "s")}");
            if (reposts > 0) parts.Add($"{reposts} new repost{(reposts == 1 ? "" : "s")}");
            if (follows > 0) parts.Add($"{follows} new follower{(follows == 1 ? "" : "s")}");

            if (parts.Count > 0)
            {
                string tag = NextTag();
                _toasts[tag] = summaryUris;
                ToastService.Show("Bluesky", string.Join(", ", parts), ShellDestination.Notifications, tag: tag);
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("Announce", $"Failed: {ex.Message}");
        }
    }

    private string NextTag() => $"n{++_tagSeq}";

    private static bool AnyToastEnabled() =>
        SettingsService.ToastMentions || SettingsService.ToastReplies || SettingsService.ToastQuotes
        || SettingsService.ToastSubscribedPosts || SettingsService.ToastFollows || SettingsService.ToastLikesReposts;
}
