using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ChatPolicyTests
{
    [TestMethod]
    public void SuccessfulProbeAllowsChat()
    {
        Assert.AreEqual(ChatAccess.Allowed, ChatAccessPolicy.FromProbe(true, 200, null));
    }

    [TestMethod]
    public void ScopeAndSupportFailuresDenyChat()
    {
        // App password created without DM access.
        Assert.AreEqual(ChatAccess.Denied, ChatAccessPolicy.FromProbe(false, 400, "InvalidToken"));
        // OAuth grant without the chat scope.
        Assert.AreEqual(ChatAccess.Denied, ChatAccessPolicy.FromProbe(false, 401, "InvalidToken"));
        Assert.AreEqual(ChatAccess.Denied, ChatAccessPolicy.FromProbe(false, 403, null));
        // PDS that doesn't proxy chat.
        Assert.AreEqual(ChatAccess.Denied, ChatAccessPolicy.FromProbe(false, 404, "XRPCNotSupported"));
        Assert.AreEqual(ChatAccess.Denied, ChatAccessPolicy.FromProbe(false, 501, null));
    }

    [TestMethod]
    public void TransientFailuresSayNothing()
    {
        Assert.AreEqual(ChatAccess.Unknown, ChatAccessPolicy.FromProbe(false, 0, null));
        Assert.AreEqual(ChatAccess.Unknown, ChatAccessPolicy.FromProbe(false, 408, null));
        Assert.AreEqual(ChatAccess.Unknown, ChatAccessPolicy.FromProbe(false, 429, "RateLimitExceeded"));
        Assert.AreEqual(ChatAccess.Unknown, ChatAccessPolicy.FromProbe(false, 500, null));
        Assert.AreEqual(ChatAccess.Unknown, ChatAccessPolicy.FromProbe(false, 502, null));
        Assert.AreEqual(ChatAccess.Unknown, ChatAccessPolicy.FromProbe(false, 400, "ExpiredToken"));
    }

    [TestMethod]
    public void MessageButtonFollowsTheirSetting()
    {
        Assert.IsFalse(ChatAccessPolicy.CanOfferMessage(isSelf: true, IncomingChatSetting.All, theyFollowYou: true));

        Assert.IsTrue(ChatAccessPolicy.CanOfferMessage(false, IncomingChatSetting.All, false));
        Assert.IsFalse(ChatAccessPolicy.CanOfferMessage(false, IncomingChatSetting.None, true));
        Assert.IsTrue(ChatAccessPolicy.CanOfferMessage(false, IncomingChatSetting.Following, true));
        Assert.IsFalse(ChatAccessPolicy.CanOfferMessage(false, IncomingChatSetting.Following, false));

        // No declaration means "people I follow".
        Assert.IsTrue(ChatAccessPolicy.CanOfferMessage(false, IncomingChatSetting.NotDeclared, true));
        Assert.IsFalse(ChatAccessPolicy.CanOfferMessage(false, IncomingChatSetting.NotDeclared, false));
    }

    [TestMethod]
    public void RunsBreakOnSenderOrPause()
    {
        DateTimeOffset t = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

        Assert.IsTrue(ChatThreadPolicy.StartsRun(null, null, "did:a", t));
        Assert.IsFalse(ChatThreadPolicy.StartsRun("did:a", t, "did:a", t.AddMinutes(1)));
        Assert.IsFalse(ChatThreadPolicy.StartsRun("did:a", t, "did:a", t + ChatThreadPolicy.RunGap));
        Assert.IsTrue(ChatThreadPolicy.StartsRun("did:a", t, "did:a", t + ChatThreadPolicy.RunGap + TimeSpan.FromSeconds(1)));
        Assert.IsTrue(ChatThreadPolicy.StartsRun("did:a", t, "did:b", t.AddSeconds(5)));
    }

    [TestMethod]
    public void SendNeedsSomeTextWithinTheLimit()
    {
        Assert.IsFalse(ChatThreadPolicy.CanSend(null));
        Assert.IsFalse(ChatThreadPolicy.CanSend("   \r\n "));
        Assert.IsTrue(ChatThreadPolicy.CanSend("hi"));
        Assert.IsTrue(ChatThreadPolicy.CanSend(new string('x', ChatThreadPolicy.MaxMessageLength)));
        Assert.IsFalse(ChatThreadPolicy.CanSend(new string('x', ChatThreadPolicy.MaxMessageLength + 1)));
    }
}
