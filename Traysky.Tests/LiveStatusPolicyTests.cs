using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class LiveStatusPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void LiveUntilTheExpiryThenNot()
    {
        string live = LiveStatusPolicy.LiveValue;
        Assert.IsTrue(LiveStatusPolicy.IsLive(live, Now.AddMinutes(1), null, null, Now));
        Assert.IsFalse(LiveStatusPolicy.IsLive(live, Now, null, null, Now), "expiry is exclusive");
        Assert.IsFalse(LiveStatusPolicy.IsLive(live, Now.AddSeconds(-1), null, null, Now));
    }

    [TestMethod]
    public void MissingExpiryCountsAsLive()
    {
        Assert.IsTrue(LiveStatusPolicy.IsLive(LiveStatusPolicy.LiveValue, null, null, null, Now));
        Assert.IsTrue(LiveStatusPolicy.IsLive(LiveStatusPolicy.LiveValue, null, true, false, Now));
    }

    [TestMethod]
    public void ServerFlagsAndOtherValuesEndIt()
    {
        string live = LiveStatusPolicy.LiveValue;
        Assert.IsFalse(LiveStatusPolicy.IsLive(live, Now.AddHours(1), false, null, Now));
        Assert.IsFalse(LiveStatusPolicy.IsLive(live, Now.AddHours(1), true, true, Now));
        Assert.IsFalse(LiveStatusPolicy.IsLive("something.else", Now.AddHours(1), true, false, Now));
        Assert.IsFalse(LiveStatusPolicy.IsLive(null, Now.AddHours(1), null, null, Now));
    }

    [TestMethod]
    public void RemainingTextFormats()
    {
        Assert.AreEqual("1 h 12 m left", LiveStatusPolicy.RemainingText(Now.AddMinutes(72), Now));
        Assert.AreEqual("2 h left", LiveStatusPolicy.RemainingText(Now.AddHours(2), Now));
        Assert.AreEqual("45 m left", LiveStatusPolicy.RemainingText(Now.AddMinutes(45), Now));
        Assert.AreEqual("1 m left", LiveStatusPolicy.RemainingText(Now.AddMinutes(1), Now));
        Assert.AreEqual("Under a minute left", LiveStatusPolicy.RemainingText(Now.AddSeconds(30), Now));
    }

    [TestMethod]
    public void RemainingTextRoundsPartialMinutesUp()
    {
        Assert.AreEqual("2 m left", LiveStatusPolicy.RemainingText(Now.AddSeconds(61), Now));
        Assert.AreEqual("1 h left", LiveStatusPolicy.RemainingText(Now.AddMinutes(59).AddSeconds(1), Now));
    }

    [TestMethod]
    public void RemainingTextIsEmptyWithoutAFutureExpiry()
    {
        Assert.AreEqual(string.Empty, LiveStatusPolicy.RemainingText(null, Now));
        Assert.AreEqual(string.Empty, LiveStatusPolicy.RemainingText(Now, Now));
        Assert.AreEqual(string.Empty, LiveStatusPolicy.RemainingText(Now.AddMinutes(-5), Now));
    }

    [TestMethod]
    [DataRow("https://twitch.tv/me","https://twitch.tv/me")]
    [DataRow("  twitch.tv/me  ", "https://twitch.tv/me")]
    [DataRow("http://www.youtube.com/live/abc", "http://www.youtube.com/live/abc")]
    public void AcceptsWebLinks(string input, string expected)
    {
        Assert.IsTrue(LiveStatusPolicy.TryNormalizeLink(input, out Uri? uri));
        Assert.AreEqual(expected, uri!.GetLeftPart(UriPartial.Path).TrimEnd('/') + uri.Query);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("file:///C:/Windows/win.ini")]
    [DataRow("javascript:alert(1)")]
    [DataRow("ms-appdata:///local/session.bin")]
    [DataRow("localhost")]
    [DataRow("https://")]
    public void RejectsOtherLinks(string? input)
    {
        Assert.IsFalse(LiveStatusPolicy.TryNormalizeLink(input, out Uri? uri));
        Assert.IsNull(uri);
    }

    [TestMethod]
    public void TitleFallsBackToTheHostAndIsTrimmed()
    {
        Uri link = new("https://www.twitch.tv/me");
        Assert.AreEqual("twitch.tv", LiveStatusPolicy.ResolveTitle(null, link));
        Assert.AreEqual("twitch.tv", LiveStatusPolicy.ResolveTitle("   ", link));
        Assert.AreEqual("Speedrun night", LiveStatusPolicy.ResolveTitle("  Speedrun night ", link));
        Assert.AreEqual(LiveStatusPolicy.TitleMaxLength, LiveStatusPolicy.ResolveTitle(new string('x', 300), link).Length);
    }

    [TestMethod]
    public void PresetsAreLabelled()
    {
        CollectionAssert.AreEqual(new[] { 30, 60, 120, 240 }, LiveStatusPolicy.PresetMinutes);
        Assert.AreEqual("30 minutes", LiveStatusPolicy.PresetLabel(30));
        Assert.AreEqual("1 hour", LiveStatusPolicy.PresetLabel(60));
        Assert.AreEqual("4 hours", LiveStatusPolicy.PresetLabel(240));
        CollectionAssert.Contains(LiveStatusPolicy.PresetMinutes, LiveStatusPolicy.DefaultMinutes);
    }

    [TestMethod]
    public void ClampKeepsDurationsInRange()
    {
        Assert.AreEqual(1, LiveStatusPolicy.ClampMinutes(0));
        Assert.AreEqual(1, LiveStatusPolicy.ClampMinutes(-10));
        Assert.AreEqual(90, LiveStatusPolicy.ClampMinutes(90));
        Assert.AreEqual(LiveStatusPolicy.MaxMinutes, LiveStatusPolicy.ClampMinutes(100000));
    }

    [TestMethod]
    public void ExtendCountsFromTheCreationTime()
    {
        DateTimeOffset created = Now.AddMinutes(-20);
        // Created 20 m ago, runs 60 m (ends in 40 m); +30 m ends in 70 m => 90 m from creation.
        Assert.AreEqual(90, LiveStatusPolicy.ExtendedDuration(created, 60, 30, Now));
    }

    [TestMethod]
    public void ExtendingAnExpiredStatusRestartsFromNow()
    {
        DateTimeOffset created = Now.AddMinutes(-100);
        // Ended 40 m ago; +30 m from now => 130 m from creation.
        Assert.AreEqual(130, LiveStatusPolicy.ExtendedDuration(created, 60, 30, Now));
    }

    [TestMethod]
    public void ExtendIsCapped()
    {
        Assert.AreEqual(LiveStatusPolicy.MaxMinutes, LiveStatusPolicy.ExtendedDuration(Now, LiveStatusPolicy.MaxMinutes, 30, Now));
    }

    [TestMethod]
    public void ToInfoBuildsFromAViewOrReturnsNull()
    {
        LiveStatusInfo? info = LiveStatusPolicy.ToInfo(LiveStatusPolicy.LiveValue, "Night stream", "https://twitch.tv/me", Now.AddHours(1), true, false, Now);
        Assert.IsNotNull(info);
        Assert.AreEqual("Night stream", info.Title);
        Assert.AreEqual("twitch.tv", info.Link!.Host);

        Assert.IsNull(LiveStatusPolicy.ToInfo(LiveStatusPolicy.LiveValue, "x", "https://twitch.tv/me", Now.AddSeconds(-1), null, null, Now));
        Assert.IsNull(LiveStatusPolicy.ToInfo("other", "x", "https://twitch.tv/me", null, null, null, Now));
    }

    [TestMethod]
    public void ToInfoToleratesAnUnusableLinkAndTitle()
    {
        LiveStatusInfo? info = LiveStatusPolicy.ToInfo(LiveStatusPolicy.LiveValue, null, "javascript:alert(1)", null, null, null, Now);
        Assert.IsNotNull(info);
        Assert.IsNull(info.Link, "an unsafe link is dropped, so nothing can be opened");
        Assert.AreEqual("Live", info.Title);
    }
}
