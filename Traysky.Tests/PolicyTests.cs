using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Traysky.Models;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class PostTextPolicyTests
{
    [TestMethod]
    public void CountsGraphemesNotChars()
    {
        // Family emoji: one grapheme, many UTF-16 code units.
        Assert.AreEqual(1, PostTextPolicy.CountGraphemes("👨‍👩‍👧"));
        Assert.AreEqual(0, PostTextPolicy.CountGraphemes(""));
        Assert.AreEqual(0, PostTextPolicy.CountGraphemes(null));
        Assert.AreEqual(5, PostTextPolicy.CountGraphemes("héllo"));
    }

    [TestMethod]
    public void LimitBoundaries()
    {
        string at300 = new('a', 300);
        string at301 = new('a', 301);
        string at280 = new('a', 280);

        Assert.IsTrue(PostTextPolicy.CanPost(at300));
        Assert.IsFalse(PostTextPolicy.CanPost(at301));
        Assert.IsTrue(PostTextPolicy.IsOverLimit(at301));
        Assert.IsFalse(PostTextPolicy.IsOverLimit(at300));
        Assert.IsTrue(PostTextPolicy.IsNearLimit(at280));
        Assert.IsFalse(PostTextPolicy.IsNearLimit(new string('a', 279)));
        Assert.AreEqual(0, PostTextPolicy.Remaining(at300));
    }

    [TestMethod]
    public void WhitespaceOnly_CannotPost()
    {
        Assert.IsFalse(PostTextPolicy.CanPost("   \n "));
        Assert.IsFalse(PostTextPolicy.CanPost(null));
    }

    [TestMethod]
    public void DraftContentIsTextOrAttachments()
    {
        Assert.IsFalse(PostTextPolicy.HasDraftContent(null, 0));
        Assert.IsFalse(PostTextPolicy.HasDraftContent("  \n ", 0));
        Assert.IsTrue(PostTextPolicy.HasDraftContent("hi", 0));
        Assert.IsTrue(PostTextPolicy.HasDraftContent("", 1));
    }
}

[TestClass]
public class RelativeTimeFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    [TestMethod]
    public void Buckets()
    {
        Assert.AreEqual("now", RelativeTimeFormatter.Format(Now.AddSeconds(-30), Now, En));
        Assert.AreEqual("5m", RelativeTimeFormatter.Format(Now.AddMinutes(-5), Now, En));
        Assert.AreEqual("3h", RelativeTimeFormatter.Format(Now.AddHours(-3), Now, En));
        Assert.AreEqual("2d", RelativeTimeFormatter.Format(Now.AddDays(-2), Now, En));
    }

    [TestMethod]
    public void OlderThanAWeek_ShowsDate()
    {
        string thisYear = RelativeTimeFormatter.Format(Now.AddDays(-30), Now, En);
        StringAssert.StartsWith(thisYear, "Aug");
        Assert.IsFalse(thisYear.Contains("2026"));

        string lastYear = RelativeTimeFormatter.Format(Now.AddYears(-1), Now, En);
        StringAssert.Contains(lastYear, "2025");
    }

    [TestMethod]
    public void FutureTimestamp_ReadsAsNow()
    {
        Assert.AreEqual("now", RelativeTimeFormatter.Format(Now.AddMinutes(5), Now, En));
    }
}

[TestClass]
public class UnreadBadgePolicyTests
{
    [TestMethod]
    public void SignedOut_FollowsTaskbarTheme()
    {
        Assert.AreEqual(TrayIconVariant.SignedOutDark, UnreadBadgePolicy.Choose(false, 0, false, isDarkTaskbar: true));
        Assert.AreEqual(TrayIconVariant.SignedOutLight, UnreadBadgePolicy.Choose(false, 0, false, isDarkTaskbar: false));
        // Unread count is meaningless while signed out.
        Assert.AreEqual(TrayIconVariant.SignedOutDark, UnreadBadgePolicy.Choose(false, 7, false, true));
    }

    [TestMethod]
    public void SignedIn_Matrix()
    {
        Assert.AreEqual(TrayIconVariant.Normal, UnreadBadgePolicy.Choose(true, 0, false, true));
        Assert.AreEqual(TrayIconVariant.Unread, UnreadBadgePolicy.Choose(true, 1, false, true));
        Assert.AreEqual(TrayIconVariant.Offline, UnreadBadgePolicy.Choose(true, 0, true, true));
        // A known unread dot survives going offline.
        Assert.AreEqual(TrayIconVariant.Unread, UnreadBadgePolicy.Choose(true, 3, true, true));
    }

    [TestMethod]
    public void EveryVariant_HasAnAsset()
    {
        foreach (TrayIconVariant v in Enum.GetValues<TrayIconVariant>())
        {
            StringAssert.EndsWith(UnreadBadgePolicy.AssetPath(v, isDarkTaskbar: false), ".ico");
            StringAssert.EndsWith(UnreadBadgePolicy.AssetPath(v, isDarkTaskbar: true), ".ico");
        }
    }

    [TestMethod]
    public void Tooltip_Pluralises()
    {
        StringAssert.Contains(UnreadBadgePolicy.Tooltip(true, "joe.bsky.social", 1, false), "1 unread notification");
        StringAssert.Contains(UnreadBadgePolicy.Tooltip(true, "joe.bsky.social", 2, false), "2 unread notifications");
        StringAssert.Contains(UnreadBadgePolicy.Tooltip(false, null, 0, false), "sign in");
        Assert.IsTrue(UnreadBadgePolicy.Tooltip(true, "joe.bsky.social", 999, true).Length < 128);
    }
}

[TestClass]
public class NotificationGroupingPolicyTests
{
    private static NotificationInput N(string id, NotificationKind kind, string who, string? subject, bool read = false, int minutesAgo = 0) =>
        new(id, kind, who, subject, read, new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero).AddMinutes(-minutesAgo));

    [TestMethod]
    public void ConsecutiveLikesOnSamePost_Collapse()
    {
        var groups = NotificationGroupingPolicy.Group(
        [
            N("1", NotificationKind.Like, "Alice", "at://p/1"),
            N("2", NotificationKind.Like, "Bob", "at://p/1", minutesAgo: 1),
            N("3", NotificationKind.Like, "Cid", "at://p/1", minutesAgo: 2),
            N("4", NotificationKind.Like, "Dee", "at://p/1", minutesAgo: 3)
        ]);

        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(4, groups[0].Members.Count);
        Assert.AreEqual("Alice, Bob and 2 others liked your post", NotificationGroupingPolicy.Headline(groups[0]));
    }

    [TestMethod]
    public void LikesOnDifferentPosts_DoNotCollapse()
    {
        var groups = NotificationGroupingPolicy.Group(
        [
            N("1", NotificationKind.Like, "Alice", "at://p/1"),
            N("2", NotificationKind.Like, "Bob", "at://p/2")
        ]);

        Assert.AreEqual(2, groups.Count);
    }

    [TestMethod]
    public void MentionBetweenLikes_KeepsOrder()
    {
        var groups = NotificationGroupingPolicy.Group(
        [
            N("1", NotificationKind.Like, "Alice", "at://p/1"),
            N("2", NotificationKind.Mention, "Bob", "at://p/9"),
            N("3", NotificationKind.Like, "Cid", "at://p/1")
        ]);

        Assert.AreEqual(3, groups.Count);
        Assert.AreEqual(NotificationKind.Mention, groups[1].Kind);
    }

    [TestMethod]
    public void Follows_CollapseRegardlessOfSubject()
    {
        var groups = NotificationGroupingPolicy.Group(
        [
            N("1", NotificationKind.Follow, "Alice", null),
            N("2", NotificationKind.Follow, "Bob", null)
        ]);

        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual("Alice and Bob followed you", NotificationGroupingPolicy.Headline(groups[0]));
    }

    [TestMethod]
    public void GroupIsUnread_IfAnyMemberIs()
    {
        var groups = NotificationGroupingPolicy.Group(
        [
            N("1", NotificationKind.Like, "Alice", "at://p/1", read: true),
            N("2", NotificationKind.Like, "Bob", "at://p/1", read: false)
        ]);

        Assert.IsTrue(groups[0].IsUnread);
    }

    [TestMethod]
    public void ThreeNames_AreListedInFull()
    {
        var groups = NotificationGroupingPolicy.Group(
        [
            N("1", NotificationKind.Repost, "A", "x"),
            N("2", NotificationKind.Repost, "B", "x"),
            N("3", NotificationKind.Repost, "C", "x")
        ]);

        Assert.AreEqual("A, B and C reposted your post", NotificationGroupingPolicy.Headline(groups[0]));
    }
}

[TestClass]
public class BlueskyLinksTests
{
    [TestMethod]
    public void PostUrl_PrefersHandle()
    {
        Assert.AreEqual(
            "https://bsky.app/profile/joe.bsky.social/post/3k2abc",
            BlueskyLinks.PostUrl("at://did:plc:xyz/app.bsky.feed.post/3k2abc", "joe.bsky.social"));
        Assert.AreEqual(
            "https://bsky.app/profile/did:plc:xyz/post/3k2abc",
            BlueskyLinks.PostUrl("at://did:plc:xyz/app.bsky.feed.post/3k2abc"));
    }

    [TestMethod]
    public void PostUrl_RejectsNonPosts()
    {
        Assert.IsNull(BlueskyLinks.PostUrl("at://did:plc:xyz/app.bsky.feed.like/3k2abc"));
        Assert.IsNull(BlueskyLinks.PostUrl("https://not.an.at.uri"));
        Assert.IsNull(BlueskyLinks.PostUrl(null));
    }

    [TestMethod]
    public void NormalizeHandle()
    {
        Assert.AreEqual("alice.bsky.social", BlueskyLinks.NormalizeHandle("  @Alice.bsky.social "));
        Assert.AreEqual("alice.bsky.social", BlueskyLinks.NormalizeHandle("alice"));
        Assert.AreEqual("did:plc:ABC", BlueskyLinks.NormalizeHandle("did:plc:ABC"));
        Assert.AreEqual("", BlueskyLinks.NormalizeHandle("  "));
    }

    [TestMethod]
    public void HashtagUrl_Escapes()
    {
        Assert.AreEqual("https://bsky.app/hashtag/caf%C3%A9", BlueskyLinks.HashtagUrl("#café"));
    }

    [TestMethod]
    public void TryParseWebUri_AcceptsOnlyAbsoluteHttp()
    {
        Assert.IsTrue(BlueskyLinks.TryParseWebUri("https://example.com/a?b=c", out Uri? https));
        Assert.AreEqual("example.com", https!.Host);
        Assert.IsTrue(BlueskyLinks.TryParseWebUri("HTTP://example.com", out _));

        Assert.IsFalse(BlueskyLinks.TryParseWebUri("file:///C:/Windows/win.ini", out Uri? rejected));
        Assert.IsNull(rejected);
        Assert.IsFalse(BlueskyLinks.TryParseWebUri("ms-appdata:///local/session.bin", out _));
        Assert.IsFalse(BlueskyLinks.TryParseWebUri("javascript:alert(1)", out _));
        Assert.IsFalse(BlueskyLinks.TryParseWebUri("/relative/path", out _));
        Assert.IsFalse(BlueskyLinks.TryParseWebUri("", out _));
        Assert.IsFalse(BlueskyLinks.TryParseWebUri(null, out _));
    }

    [TestMethod]
    public void IsWebUri_RejectsLocalAndRelative()
    {
        Assert.IsTrue(BlueskyLinks.IsWebUri(new Uri("https://cdn.bsky.app/img/x.jpg")));
        Assert.IsFalse(BlueskyLinks.IsWebUri(new Uri("file:///C:/x.jpg")));
        Assert.IsFalse(BlueskyLinks.IsWebUri(new Uri("x.jpg", UriKind.Relative)));
        Assert.IsFalse(BlueskyLinks.IsWebUri(null));
    }
}

[TestClass]
public class PollBackoffPolicyTests
{
    [TestMethod]
    public void DoublesAndCaps()
    {
        TimeSpan configured = TimeSpan.FromSeconds(60);
        Assert.AreEqual(TimeSpan.FromSeconds(60), PollBackoffPolicy.NextInterval(configured, 0));
        Assert.AreEqual(TimeSpan.FromSeconds(120), PollBackoffPolicy.NextInterval(configured, 1));
        Assert.AreEqual(TimeSpan.FromSeconds(240), PollBackoffPolicy.NextInterval(configured, 2));
        Assert.AreEqual(PollBackoffPolicy.MaxInterval, PollBackoffPolicy.NextInterval(configured, 20));
    }

    [TestMethod]
    public void RateLimitWait_HasFloorAndCeiling()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Assert.AreEqual(TimeSpan.FromSeconds(5), PollBackoffPolicy.UntilRateLimitReset(now.AddMinutes(-5), now));
        Assert.AreEqual(PollBackoffPolicy.MaxInterval, PollBackoffPolicy.UntilRateLimitReset(now.AddHours(2), now));
        Assert.AreEqual(TimeSpan.FromSeconds(65), PollBackoffPolicy.UntilRateLimitReset(now.AddSeconds(60), now));
    }

    [TestMethod]
    public void Sanitize_RejectsOffMenuValues()
    {
        Assert.AreEqual(60, PollBackoffPolicy.SanitizeIntervalSeconds(1));
        Assert.AreEqual(300, PollBackoffPolicy.SanitizeIntervalSeconds(300));
    }
}

[TestClass]
public class TrayClickPolicyTests
{
    [TestMethod]
    public void RemovingLastFlyout_MovesItToAnotherSlot()
    {
        TrayClickAssignments all = new(TrayClickAction.None, TrayClickAction.None, TrayClickAction.None, TrayClickAction.None);

        TrayClickAssignments fixedUp = TrayClickPolicy.EnsureFlyoutReachable(TrayClickButton.Left, all);

        Assert.AreEqual(TrayClickAction.None, fixedUp.Left, "the user's own change is honoured");
        Assert.AreEqual(TrayClickAction.ShowFlyout, fixedUp.Right);
    }

    [TestMethod]
    public void AnyOpeningAction_CountsAsReachable()
    {
        TrayClickAssignments compose = new(TrayClickAction.None, TrayClickAction.Compose, TrayClickAction.None, TrayClickAction.None);
        Assert.AreEqual(compose, TrayClickPolicy.EnsureFlyoutReachable(TrayClickButton.Left, compose));
    }

    [TestMethod]
    public void Resolve_FallsBackWhileSignedOut()
    {
        Assert.AreEqual(ShellDestination.Current, TrayClickPolicy.Resolve(TrayClickAction.Compose, isSignedIn: false));
        Assert.AreEqual(ShellDestination.Compose, TrayClickPolicy.Resolve(TrayClickAction.Compose, isSignedIn: true));
        Assert.AreEqual(ShellDestination.Notifications, TrayClickPolicy.Resolve(TrayClickAction.ShowNotifications, true));
    }

    [TestMethod]
    public void Parse_FallsBackForUnknownValues()
    {
        Assert.AreEqual(TrayClickAction.ShowFlyout, TrayClickPolicy.Parse(99, TrayClickAction.ShowFlyout));
        Assert.AreEqual(TrayClickAction.Compose, TrayClickPolicy.Parse(2, TrayClickAction.None));
    }

    [TestMethod]
    public void DeferOnlyWhenDoubleClickAssigned()
    {
        Assert.IsFalse(TrayClickPolicy.ShouldDeferSingleClick(TrayClickAction.None));
        Assert.IsTrue(TrayClickPolicy.ShouldDeferSingleClick(TrayClickAction.Compose));
    }
}

[TestClass]
public class PersistedSessionTests
{
    [TestMethod]
    public void RoundTripsThroughSourceGeneratedContext()
    {
        PersistedSession session = new()
        {
            Service = "https://bsky.social/",
            Did = "did:plc:abc",
            Handle = "joe.bsky.social",
            AuthenticationType = "UsernamePassword",
            RefreshToken = "eyJ.refresh.token",
            SavedAtUtc = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero)
        };

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(session, SessionJsonContext.Default.PersistedSession);
        PersistedSession? back = JsonSerializer.Deserialize(bytes, SessionJsonContext.Default.PersistedSession);

        Assert.IsNotNull(back);
        Assert.AreEqual(session.Did, back.Did);
        Assert.AreEqual(session.RefreshToken, back.RefreshToken);
        Assert.IsNull(back.DPoPProofKey);
        Assert.AreEqual(session.SavedAtUtc, back.SavedAtUtc);
    }
}
