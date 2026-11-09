using System;
using System.Collections.Generic;
using Traysky.Models;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace Traysky.Services;

/// <summary>
/// Centralized service for application settings, backed by
/// <see cref="ApplicationData.LocalSettings"/>. Same shape as Traydio's: typed static
/// accessors, defensive reads, and change events for the few settings something live
/// reacts to.
/// </summary>
public static class SettingsService
{
    private const string IsFirstRunKey = "IsFirstRun";
    private const string TrayLeftClickActionKey = "TrayLeftClickAction";
    private const string TrayRightClickActionKey = "TrayRightClickAction";
    private const string TrayLeftDoubleClickActionKey = "TrayLeftDoubleClickAction";
    private const string TrayRightDoubleClickActionKey = "TrayRightDoubleClickAction";
    private const string PollIntervalSecondsKey = "PollIntervalSeconds";
    private const string ToastMentionsKey = "ToastMentions";
    private const string ToastRepliesKey = "ToastReplies";
    private const string ToastQuotesKey = "ToastQuotes";
    private const string ToastSubscribedPostsKey = "ToastSubscribedPosts";
    private const string ToastFollowsKey = "ToastFollows";
    private const string ToastLikesRepostsKey = "ToastLikesReposts";
    private const string ShowReplyContextKey = "ShowReplyContext";
    private const string UseThreadV2Key = "UseThreadV2";
    private const string ThreadSortKey = "ThreadSort";
    private const string LastFeedKeyKey = "LastFeedKey";
    private const string ComposeDraftKey = "ComposeDraft";
    private const string RecentHashtagsKey = "RecentHashtags";
    private const string FollowedHashtagsKey = "FollowedHashtags";
    private const string RecentSearchesKey = "RecentSearches";
    private const string ChatLogCursorKey = "ChatLogCursor";
    private const string PopupWidthKey = "PopupWidth";
    private const string PopupHeightKey = "PopupHeight";
    private const string PopupIsWindowKey = "PopupIsWindow";
    private const string WindowBoundsKey = "WindowBounds";

    public const int DefaultPopupWidth = 420;
    public const int DefaultPopupHeight = 600;
    private const int MinPopupWidth = 360;
    private const int MinPopupHeight = 320;
    private const int MaxPopupDimension = 4000;

    /// <summary>The flyout is the sub-breakpoint form: anything wider is a standard window (see ShellLayoutPolicy), so a
    /// remembered flyout width never reaches it, or reopening would flip straight back into a window.</summary>
    private const int MaxFlyoutWidth = 560;

    /// <summary>Raised when <see cref="PollIntervalSeconds"/> changes so the poller can re-arm its timer.</summary>
    public static event EventHandler? PollIntervalChanged;

    private static IPropertySet Values => ApplicationData.Current.LocalSettings.Values;

    public static bool IsFirstRun
    {
        get => GetBool(IsFirstRunKey, true);
        set => Set(IsFirstRunKey, value);
    }

    public static TrayClickAction TrayLeftClickAction
    {
        get => GetAction(TrayLeftClickActionKey, TrayClickPolicy.DefaultLeftAction);
        set => SetAction(TrayClickButton.Left, value);
    }

    public static TrayClickAction TrayRightClickAction
    {
        get => GetAction(TrayRightClickActionKey, TrayClickPolicy.DefaultRightAction);
        set => SetAction(TrayClickButton.Right, value);
    }

    public static TrayClickAction TrayLeftDoubleClickAction
    {
        get => GetAction(TrayLeftDoubleClickActionKey, TrayClickPolicy.DefaultDoubleClickAction);
        set => SetAction(TrayClickButton.LeftDouble, value);
    }

    public static TrayClickAction TrayRightDoubleClickAction
    {
        get => GetAction(TrayRightDoubleClickActionKey, TrayClickPolicy.DefaultDoubleClickAction);
        set => SetAction(TrayClickButton.RightDouble, value);
    }

    public static TrayClickAssignments TrayClickAssignments => new(
        TrayLeftClickAction,
        TrayRightClickAction,
        TrayLeftDoubleClickAction,
        TrayRightDoubleClickAction);

    /// <summary>
    /// Stores one slot and, through <see cref="TrayClickPolicy.EnsureFlyoutReachable"/>, any
    /// other slot the guard had to move the flyout to.
    /// </summary>
    private static void SetAction(TrayClickButton button, TrayClickAction action)
    {
        TrayClickAssignments requested = TrayClickAssignments.With(button, action);
        TrayClickAssignments stored = TrayClickPolicy.EnsureFlyoutReachable(button, requested);

        Set(TrayLeftClickActionKey, (int)stored.Left);
        Set(TrayRightClickActionKey, (int)stored.Right);
        Set(TrayLeftDoubleClickActionKey, (int)stored.LeftDouble);
        Set(TrayRightDoubleClickActionKey, (int)stored.RightDouble);
    }

    public static int PollIntervalSeconds
    {
        get => PollBackoffPolicy.SanitizeIntervalSeconds(GetInt(PollIntervalSecondsKey, PollBackoffPolicy.DefaultIntervalSeconds));
        set
        {
            int sanitized = PollBackoffPolicy.SanitizeIntervalSeconds(value);
            if (sanitized == PollIntervalSeconds)
                return;
            Set(PollIntervalSecondsKey, sanitized);
            PollIntervalChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static bool ToastMentions
    {
        get => GetBool(ToastMentionsKey, true);
        set => Set(ToastMentionsKey, value);
    }

    public static bool ToastReplies
    {
        get => GetBool(ToastRepliesKey, true);
        set => Set(ToastRepliesKey, value);
    }

    public static bool ToastQuotes
    {
        get => GetBool(ToastQuotesKey, true);
        set => Set(ToastQuotesKey, value);
    }

    /// <summary>Posts from accounts whose bell you turned on. On by default: you asked for them.</summary>
    public static bool ToastSubscribedPosts
    {
        get => GetBool(ToastSubscribedPostsKey, true);
        set => Set(ToastSubscribedPostsKey, value);
    }

    public static bool ToastFollows
    {
        get => GetBool(ToastFollowsKey, false);
        set => Set(ToastFollowsKey, value);
    }

    public static bool ToastLikesReposts
    {
        get => GetBool(ToastLikesRepostsKey, false);
        set => Set(ToastLikesRepostsKey, value);
    }

    /// <summary>Whether a reply in the timeline shows the post it replies to (and the thread's first post) above it.</summary>
    public static bool ShowReplyContext
    {
        get => GetBool(ShowReplyContextKey, true);
        set => Set(ShowReplyContextKey, value);
    }

    /// <summary>Whether the post page uses the v2 thread endpoint (server-sorted, hidden replies). Off falls back to v1.</summary>
    public static bool UseThreadV2
    {
        get => GetBool(UseThreadV2Key, true);
        set => Set(UseThreadV2Key, value);
    }

    /// <summary>How replies are ordered on the post page when <see cref="UseThreadV2"/> is on.</summary>
    public static ThreadSort ThreadSort
    {
        get => ThreadSortOptions.Parse(GetString(ThreadSortKey, ThreadSortOptions.ToApiValue(ThreadSortOptions.Default)));
        set => Set(ThreadSortKey, ThreadSortOptions.ToApiValue(value));
    }

    /// <summary><see cref="ViewModels.Items.FeedTabItem.Key"/> of the feed tab last shown, so the flyout
    /// reopens on the same feed. "timeline" (the default) means the home/Following feed.</summary>
    public static string LastFeedKey
    {
        get => GetString(LastFeedKeyKey, "timeline");
        set => Set(LastFeedKeyKey, value ?? "timeline");
    }

    /// <summary>The flyout's size in DIPs as the user last resized it, so a wide layout survives reopening.</summary>
    public static (int Width, int Height) PopupSize
    {
        get => (
            Math.Clamp(GetInt(PopupWidthKey, DefaultPopupWidth), MinPopupWidth, MaxFlyoutWidth),
            Math.Clamp(GetInt(PopupHeightKey, DefaultPopupHeight), MinPopupHeight, MaxPopupDimension));
        set
        {
            Set(PopupWidthKey, Math.Clamp(value.Width, MinPopupWidth, MaxFlyoutWidth));
            Set(PopupHeightKey, Math.Clamp(value.Height, MinPopupHeight, MaxPopupDimension));
        }
    }

    /// <summary>True once the shell was last left as a standard window (it was widened past the first breakpoint).</summary>
    public static bool PopupIsWindow
    {
        get => GetBool(PopupIsWindowKey, false);
        set => Set(PopupIsWindowKey, value);
    }

    /// <summary>Where the standard window last sat, in physical pixels; null until it has been one.</summary>
    public static PixelRect? WindowBounds
    {
        get
        {
            string[] parts = GetString(WindowBoundsKey, string.Empty).Split(',');
            if (parts.Length == 4
                && int.TryParse(parts[0], out int x)
                && int.TryParse(parts[1], out int y)
                && int.TryParse(parts[2], out int width)
                && int.TryParse(parts[3], out int height)
                && width > 0 && height > 0)
            {
                return new PixelRect(x, y, width, height);
            }

            return null;
        }
        set
        {
            if (value is { } b)
                Set(WindowBoundsKey, $"{b.X},{b.Y},{b.Width},{b.Height}");
        }
    }

    /// <summary>An unsent post survives quitting the app. Empty string clears it.</summary>
    public static string ComposeDraft
    {
        get => GetString(ComposeDraftKey, string.Empty);
        set => Set(ComposeDraftKey, value ?? string.Empty);
    }

    /// <summary>Tags chosen from the compose box's '#' suggestion popup, most-recent first (see <see cref="HashtagSuggestionPolicy"/>).</summary>
    public static IReadOnlyList<string> RecentHashtags
    {
        get
        {
            string raw = GetString(RecentHashtagsKey, string.Empty);
            return raw.Length == 0 ? [] : raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        set => Set(RecentHashtagsKey, string.Join('\n', value ?? []));
    }

    /// <summary>Hashtags followed in this app (no leading #), shown as extra feed tabs. Local only; not synced with bsky.app.</summary>
    public static IReadOnlyList<string> FollowedHashtags
    {
        get
        {
            string raw = GetString(FollowedHashtagsKey, string.Empty);
            return raw.Length == 0 ? [] : raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        set => Set(FollowedHashtagsKey, string.Join('\n', value ?? []));
    }

    /// <summary>Queries submitted on the search page, most-recent first (see <see cref="SearchQueryPolicy"/>).</summary>
    public static IReadOnlyList<string> RecentSearches
    {
        get
        {
            string raw = GetString(RecentSearchesKey, string.Empty);
            return raw.Length == 0 ? [] : raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        set => Set(RecentSearchesKey, string.Join('\n', value ?? []));
    }

    /// <summary>Where the chat log sync left off for <paramref name="did"/>; null when it hasn't run for that account.</summary>
    public static string? GetChatLogCursor(string did)
    {
        string[] parts = GetString(ChatLogCursorKey, string.Empty).Split('\n');
        return parts.Length == 2 && parts[0] == did && parts[1].Length > 0 ? parts[1] : null;
    }

    public static void SetChatLogCursor(string did, string? cursor) =>
        Set(ChatLogCursorKey, string.IsNullOrEmpty(cursor) ? string.Empty : did + '\n' + cursor);

    private static TrayClickAction GetAction(string key, TrayClickAction fallback) =>
        TrayClickPolicy.Parse(GetInt(key, (int)fallback), fallback);

    private static bool GetBool(string key, bool fallback)
    {
        try
        {
            if (Values.TryGetValue(key, out object? value))
            {
                return value switch
                {
                    bool b => b,
                    string s when bool.TryParse(s, out bool parsed) => parsed,
                    _ => fallback
                };
            }
        }
        catch
        {
            // Settings store unavailable: fall through to the default.
        }
        return fallback;
    }

    private static int GetInt(string key, int fallback)
    {
        try
        {
            if (Values.TryGetValue(key, out object? value))
            {
                return value switch
                {
                    int i => i,
                    long l => (int)l,
                    string s when int.TryParse(s, out int parsed) => parsed,
                    _ => fallback
                };
            }
        }
        catch
        {
        }
        return fallback;
    }

    private static string GetString(string key, string fallback)
    {
        try
        {
            if (Values.TryGetValue(key, out object? value) && value is string s)
                return s;
        }
        catch
        {
        }
        return fallback;
    }

    private static void Set(string key, object value)
    {
        try
        {
            Values[key] = value;
        }
        catch (Exception ex)
        {
            LogService.Warn("Settings", $"Failed to save '{key}': {ex.Message}");
        }
    }
}
