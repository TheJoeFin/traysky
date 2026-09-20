using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class HashtagSuggestionPolicyTests
{
    [TestMethod]
    public void WithRecentTag_AddsNewTagToFront()
    {
        IReadOnlyList<string> updated = HashtagSuggestionPolicy.WithRecentTag(["worldcup"], "dotnet");
        Assert.IsTrue(new[] { "dotnet", "worldcup" }.SequenceEqual(updated));
    }

    [TestMethod]
    public void WithRecentTag_MovesExistingTagToFrontCaseInsensitively()
    {
        IReadOnlyList<string> updated = HashtagSuggestionPolicy.WithRecentTag(["dotnet", "worldcup"], "DotNet");
        Assert.IsTrue(new[] { "DotNet", "worldcup" }.SequenceEqual(updated));
    }

    [TestMethod]
    public void WithRecentTag_CapsAtMaxRecents()
    {
        var recents = new List<string>();
        for (int i = 0; i < HashtagSuggestionPolicy.MaxRecents; i++)
            recents.Add($"tag{i}");

        IReadOnlyList<string> updated = HashtagSuggestionPolicy.WithRecentTag(recents, "newest");
        Assert.AreEqual(HashtagSuggestionPolicy.MaxRecents, updated.Count);
        Assert.AreEqual("newest", updated[0]);
    }

    [TestMethod]
    public void WithRecentTag_IgnoresEmptyTag()
    {
        IReadOnlyList<string> updated = HashtagSuggestionPolicy.WithRecentTag(["dotnet"], "   ");
        Assert.IsTrue(new[] { "dotnet" }.SequenceEqual(updated));
    }

    [TestMethod]
    public void Filter_MatchesPrefixCaseInsensitively()
    {
        IReadOnlyList<string> matches = HashtagSuggestionPolicy.Filter(["dotnet", "DevLife", "worldcup"], "de");
        Assert.IsTrue(new[] { "DevLife" }.SequenceEqual(matches));
    }

    [TestMethod]
    public void Filter_EmptyQuery_ReturnsAllRecents()
    {
        IReadOnlyList<string> recents = ["dotnet", "worldcup"];
        Assert.IsTrue(recents.SequenceEqual(HashtagSuggestionPolicy.Filter(recents, string.Empty)));
    }

    [TestMethod]
    public void Normalize_StripsLeadingHashAndTrims()
    {
        Assert.AreEqual("dotnet", HashtagSuggestionPolicy.Normalize("  #dotnet  "));
        Assert.AreEqual(string.Empty, HashtagSuggestionPolicy.Normalize(null));
    }
}
