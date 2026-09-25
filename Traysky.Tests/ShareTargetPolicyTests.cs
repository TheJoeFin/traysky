using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ShareTargetPolicyTests
{
    [TestMethod]
    public void LinkWithTitleUsesTitleThenLink()
    {
        Assert.AreEqual(
            "Example Domain\nhttps://example.com/",
            ShareTargetPolicy.ComposeText("Example Domain", null, "https://example.com/"));
    }

    [TestMethod]
    public void TextKeepsPriorityOverTitle()
    {
        Assert.AreEqual(
            "Look at this\nhttps://example.com/",
            ShareTargetPolicy.ComposeText("Example Domain", "  Look at this ", "https://example.com/"));
    }

    [TestMethod]
    public void LinkAlreadyInTextIsNotRepeated()
    {
        Assert.AreEqual(
            "https://example.com/",
            ShareTargetPolicy.ComposeText("Example Domain", "https://example.com/", "https://example.com/"));
    }

    [TestMethod]
    public void TitleThatIsJustTheLinkIsDropped()
    {
        Assert.AreEqual(
            "https://example.com/",
            ShareTargetPolicy.ComposeText("https://example.com/", null, "https://example.com/"));
    }

    [TestMethod]
    public void TitleAloneIsNotPostText()
    {
        // A file share usually carries only a title (the file name); that isn't worth posting.
        Assert.AreEqual(string.Empty, ShareTargetPolicy.ComposeText("IMG_0042.jpg", null, null));
    }

    [TestMethod]
    public void MergeKeepsTheExistingDraft()
    {
        Assert.AreEqual("shared", ShareTargetPolicy.MergeIntoDraft("", "shared"));
        Assert.AreEqual("draft", ShareTargetPolicy.MergeIntoDraft("draft", "  "));
        Assert.AreEqual("draft\n\nshared", ShareTargetPolicy.MergeIntoDraft("draft \n", "shared"));
    }

    [TestMethod]
    public void OnlyPostableFilesAreSupported()
    {
        Assert.IsTrue(ShareTargetPolicy.IsSupportedFile("photo.JPG"));
        Assert.IsTrue(ShareTargetPolicy.IsSupportedFile("clip.mp4"));
        Assert.IsFalse(ShareTargetPolicy.IsSupportedFile("notes.txt"));
        Assert.IsFalse(ShareTargetPolicy.IsSupportedFile("no-extension"));
    }

    [TestMethod]
    public void ManifestFileTypesMatchTheAttachmentPolicy()
    {
        XDocument manifest = XDocument.Load(Path.Combine(RepoRoot(), "Traysky", "Package.appxmanifest"));
        string[] declared = manifest.Descendants()
            .Where(e => e.Name.LocalName == "FileType" && e.Parent?.Parent?.Name.LocalName == "ShareTarget")
            .Select(e => e.Value.Trim().ToLowerInvariant())
            .Order()
            .ToArray();

        string[] supported = ComposeAttachmentPolicy.ImageExtensions
            .Concat(ComposeAttachmentPolicy.VideoExtensions)
            .Select(e => e.ToLowerInvariant())
            .Order()
            .ToArray();

        CollectionAssert.AreEqual(supported, declared);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Traysky.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Traysky.slnx not found above the test output folder.");
    }
}
