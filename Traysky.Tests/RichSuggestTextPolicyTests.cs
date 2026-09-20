using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class RichSuggestTextPolicyTests
{
    [TestMethod]
    public void StripTokenMarkup_RemovesZeroWidthSpacesAroundTokens()
    {
        string richEditText = "hello ​@alice.bsky.social​ check ​#worldcup​ out\r";
        Assert.AreEqual("hello @alice.bsky.social check #worldcup out", RichSuggestTextPolicy.StripTokenMarkup(richEditText));
    }

    [TestMethod]
    public void StripTokenMarkup_TrimsTrailingParagraphMark()
    {
        Assert.AreEqual("no tokens here", RichSuggestTextPolicy.StripTokenMarkup("no tokens here\r"));
    }

    [TestMethod]
    public void StripTokenMarkup_NoTokens_LeavesTextUnchanged()
    {
        Assert.AreEqual("plain text, no facets", RichSuggestTextPolicy.StripTokenMarkup("plain text, no facets"));
    }

    [TestMethod]
    public void StripTokenMarkup_NullOrEmpty_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, RichSuggestTextPolicy.StripTokenMarkup(null));
        Assert.AreEqual(string.Empty, RichSuggestTextPolicy.StripTokenMarkup(string.Empty));
    }
}
