using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class FacetSegmenterTests
{
    private static FacetInput Link(string text, string match, string target)
    {
        // Byte offsets computed the way a well-behaved client computes them: over UTF-8.
        int charIndex = text.IndexOf(match, System.StringComparison.Ordinal);
        long start = Encoding.UTF8.GetByteCount(text.AsSpan(0, charIndex));
        long end = start + Encoding.UTF8.GetByteCount(match);
        return new FacetInput(start, end, FacetKind.Link, target);
    }

    [TestMethod]
    public void NoFacets_IsOnePlainSegment()
    {
        var segments = FacetSegmenter.Segment("hello world", null);

        Assert.AreEqual(1, segments.Count);
        Assert.AreEqual(FacetKind.Plain, segments[0].Kind);
        Assert.AreEqual("hello world", segments[0].Text);
    }

    [TestMethod]
    public void EmptyText_IsEmpty()
    {
        Assert.AreEqual(0, FacetSegmenter.Segment("", null).Count);
        Assert.AreEqual(0, FacetSegmenter.Segment(null, null).Count);
    }

    [TestMethod]
    public void AsciiLink_SplitsAroundIt()
    {
        string text = "see example.com now";
        var segments = FacetSegmenter.Segment(text, [Link(text, "example.com", "https://example.com")]);

        CollectionAssert.AreEqual(
            new[] { "see ", "example.com", " now" },
            segments.Select(s => s.Text).ToArray());
        Assert.AreEqual(FacetKind.Link, segments[1].Kind);
        Assert.AreEqual("https://example.com", segments[1].Target);
    }

    [TestMethod]
    public void EmojiBeforeFacet_ByteOffsetsStillLandOnTheRightChars()
    {
        // 🦋 is 4 bytes in UTF-8 but 2 UTF-16 chars; raw offsets would be off by two.
        string text = "🦋 hi @alice.bsky.social!";
        int charIndex = text.IndexOf("@alice", System.StringComparison.Ordinal);
        long start = Encoding.UTF8.GetByteCount(text.AsSpan(0, charIndex));
        long end = start + Encoding.UTF8.GetByteCount("@alice.bsky.social");

        var segments = FacetSegmenter.Segment(text, [new FacetInput(start, end, FacetKind.Mention, "did:plc:abc")]);

        Assert.AreEqual(3, segments.Count);
        Assert.AreEqual("🦋 hi ", segments[0].Text);
        Assert.AreEqual("@alice.bsky.social", segments[1].Text);
        Assert.AreEqual(FacetKind.Mention, segments[1].Kind);
        Assert.AreEqual("!", segments[2].Text);
    }

    [TestMethod]
    public void SurrogatePairInsideFacet_IsKeptWhole()
    {
        string text = "tag: #🦋butterfly end";
        var segments = FacetSegmenter.Segment(text, [Link(text, "#🦋butterfly", "butterfly")]);

        Assert.AreEqual("#🦋butterfly", segments[1].Text);
    }

    [TestMethod]
    public void AdjacentFacets_ProduceNoEmptyPlainSegment()
    {
        string text = "#one#two";
        var facets = new List<FacetInput>
        {
            new(0, 4, FacetKind.Tag, "one"),
            new(4, 8, FacetKind.Tag, "two")
        };

        var segments = FacetSegmenter.Segment(text, facets);

        Assert.AreEqual(2, segments.Count);
        Assert.IsTrue(segments.All(s => s.Kind == FacetKind.Tag));
    }

    [TestMethod]
    public void OverlappingFacet_IsDropped()
    {
        string text = "abcdefgh";
        var facets = new List<FacetInput>
        {
            new(0, 4, FacetKind.Link, "a"),
            new(2, 6, FacetKind.Link, "b") // overlaps the first
        };

        var segments = FacetSegmenter.Segment(text, facets);

        Assert.AreEqual(2, segments.Count);
        Assert.AreEqual("abcd", segments[0].Text);
        Assert.AreEqual("efgh", segments[1].Text);
        Assert.AreEqual(FacetKind.Plain, segments[1].Kind);
    }

    [TestMethod]
    public void FacetPastEndOfText_IsClamped()
    {
        string text = "short";
        var segments = FacetSegmenter.Segment(text, [new FacetInput(2, 500, FacetKind.Link, "x")]);

        Assert.AreEqual(2, segments.Count);
        Assert.AreEqual("sh", segments[0].Text);
        Assert.AreEqual("ort", segments[1].Text);
    }

    [TestMethod]
    public void UnsortedFacets_AreEmittedInTextOrder()
    {
        string text = "a b c";
        var facets = new List<FacetInput>
        {
            new(4, 5, FacetKind.Tag, "c"),
            new(0, 1, FacetKind.Tag, "a")
        };

        var segments = FacetSegmenter.Segment(text, facets);

        CollectionAssert.AreEqual(new[] { "a", " b ", "c" }, segments.Select(s => s.Text).ToArray());
    }

    [TestMethod]
    public void ZeroLengthFacet_IsIgnored()
    {
        var segments = FacetSegmenter.Segment("abc", [new FacetInput(1, 1, FacetKind.Link, "x")]);

        Assert.AreEqual(1, segments.Count);
        Assert.AreEqual(FacetKind.Plain, segments[0].Kind);
    }
}
