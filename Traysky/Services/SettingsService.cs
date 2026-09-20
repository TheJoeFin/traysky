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
    private const string ToastFollowsKey = "ToastFollows";
    private const string ToastLikesRepostsKey = "ToastLikesReposts";
    private const string LastFeedKeyKey = "LastFeedKey";
    private const string ComposeDraftKey = "ComposeDraft";
    private const string RecentHashtagsKey = "RecentHashtags";

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

    /// <summary><see cref="ViewModels.Items.FeedTabItem.Key"/> of the feed tab last shown, so the flyout
    /// reopens on the same feed. "timeline" (the default) means the home/Following feed.</summary>
    public static string LastFeedKey
    {
        get => GetString(LastFeedKeyKey, "timeline");
        set => Set(LastFeedKeyKey, value ?? "timeline");
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
