using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ComposeAttachmentPolicyTests
{
    [TestMethod]
    public void MimeTypeFor_KnownImageExtensions()
    {
        Assert.AreEqual("image/jpeg", ComposeAttachmentPolicy.MimeTypeFor(".jpg"));
        Assert.AreEqual("image/jpeg", ComposeAttachmentPolicy.MimeTypeFor(".JPEG"));
        Assert.AreEqual("image/png", ComposeAttachmentPolicy.MimeTypeFor(".png"));
        Assert.AreEqual("image/webp", ComposeAttachmentPolicy.MimeTypeFor(".webp"));
        Assert.AreEqual("image/gif", ComposeAttachmentPolicy.MimeTypeFor(".gif"));
    }

    [TestMethod]
    public void MimeTypeFor_KnownVideoExtensions()
    {
        Assert.AreEqual("video/mp4", ComposeAttachmentPolicy.MimeTypeFor(".mp4"));
        Assert.AreEqual("video/mp4", ComposeAttachmentPolicy.MimeTypeFor(".M4V"));
        Assert.AreEqual("video/quicktime", ComposeAttachmentPolicy.MimeTypeFor(".mov"));
        Assert.AreEqual("video/webm", ComposeAttachmentPolicy.MimeTypeFor(".webm"));
    }

    [TestMethod]
    public void MimeTypeFor_UnknownExtension_ReturnsNull()
    {
        Assert.IsNull(ComposeAttachmentPolicy.MimeTypeFor(".exe"));
        Assert.IsNull(ComposeAttachmentPolicy.MimeTypeFor(""));
    }

    [TestMethod]
    public void IsImageExtension_And_IsVideoExtension_AreDisjoint()
    {
        Assert.IsTrue(ComposeAttachmentPolicy.IsImageExtension(".png"));
        Assert.IsFalse(ComposeAttachmentPolicy.IsVideoExtension(".png"));

        Assert.IsTrue(ComposeAttachmentPolicy.IsVideoExtension(".mp4"));
        Assert.IsFalse(ComposeAttachmentPolicy.IsImageExtension(".mp4"));
    }

    [TestMethod]
    public void CanAddImage_RespectsMaxAndVideoExclusion()
    {
        Assert.IsTrue(ComposeAttachmentPolicy.CanAddImage(0, hasVideo: false));
        Assert.IsTrue(ComposeAttachmentPolicy.CanAddImage(ComposeAttachmentPolicy.MaxImages - 1, hasVideo: false));
        Assert.IsFalse(ComposeAttachmentPolicy.CanAddImage(ComposeAttachmentPolicy.MaxImages, hasVideo: false));
        Assert.IsFalse(ComposeAttachmentPolicy.CanAddImage(0, hasVideo: true));
    }

    [TestMethod]
    public void CanAddVideo_OnlyWhenNothingElseAttached()
    {
        Assert.IsTrue(ComposeAttachmentPolicy.CanAddVideo(0, hasVideo: false));
        Assert.IsFalse(ComposeAttachmentPolicy.CanAddVideo(1, hasVideo: false));
        Assert.IsFalse(ComposeAttachmentPolicy.CanAddVideo(0, hasVideo: true));
    }

    [TestMethod]
    public void ValidateSize_WithinLimit_ReturnsNull()
    {
        Assert.IsNull(ComposeAttachmentPolicy.ValidateSize(ComposeAttachmentKind.Image, ComposeAttachmentPolicy.MaxImageBytes));
        Assert.IsNull(ComposeAttachmentPolicy.ValidateSize(ComposeAttachmentKind.Video, ComposeAttachmentPolicy.MaxVideoBytes));
    }

    [TestMethod]
    public void ValidateSize_OverLimit_ReturnsMessage()
    {
        Assert.IsNotNull(ComposeAttachmentPolicy.ValidateSize(ComposeAttachmentKind.Image, ComposeAttachmentPolicy.MaxImageBytes + 1));
        Assert.IsNotNull(ComposeAttachmentPolicy.ValidateSize(ComposeAttachmentKind.Video, ComposeAttachmentPolicy.MaxVideoBytes + 1));
    }
}
