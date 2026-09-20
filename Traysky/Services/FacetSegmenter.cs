using System;
using System.Collections.Generic;
using System.Text;

namespace Traysky.Services;

/// <summary>What a rich-text segment is, which decides how it renders and what a click does.</summary>
public enum FacetKind
{
    Plain,
    Link,
    Mention,
    Tag
}

/// <summary>
/// One run of a post's text. <see cref="Target"/> is the link URL, the mentioned DID or the
/// bare tag depending on <see cref="Kind"/>, and null for plain text.
/// </summary>
public readonly record struct TextSegment(FacetKind Kind, string Text, string? Target);

/// <summary>
/// A facet as it comes off the wire, reduced to what the segmenter needs: a UTF-8 byte range
/// and one feature. Facets with several features are flattened to one input each.
/// </summary>
public readonly record struct FacetInput(long ByteStart, long ByteEnd, FacetKind Kind, string Target);

/// <summary>
/// Turns a post's text plus its facets into renderable segments.
/// </summary>
/// <remarks>
/// AT Protocol facets index <b>UTF-8 byte offsets</b>, not .NET string (UTF-16) indices, so a
/// post that starts with an emoji shifts every facet after it by two or three positions if the
/// offsets are used raw. The text is encoded once and a byte→char map built from it; facets are
/// then resolved through the map. Real-world data has overlapping and out-of-range facets, so
/// the segmenter is defensive: it sorts by start, drops anything that overlaps a facet already
/// emitted, and clamps ranges to the text. Pure .NET on purpose - see Traysky.Tests.
/// </remarks>
public static class FacetSegmenter
{
    public static IReadOnlyList<TextSegment> Segment(string? text, IEnumerable<FacetInput>? facets)
    {
        text ??= string.Empty;

        if (text.Length == 0)
            return [];

        List<FacetInput> ordered = [];
        if (facets is not null)
        {
            foreach (FacetInput f in facets)
            {
                if (f.ByteEnd <= f.ByteStart)
                    continue;
                ordered.Add(f);
            }
        }

        if (ordered.Count == 0)
            return [new TextSegment(FacetKind.Plain, text, null)];

        ordered.Sort((a, b) => a.ByteStart.CompareTo(b.ByteStart));

        // charIndexAtByte[b] = index of the char that starts at UTF-8 byte offset b, or -1 when
        // b lands inside a multi-byte sequence. Length+1 so ByteEnd == byte length resolves.
        int[] charIndexAtByte = BuildByteToCharMap(text, out int byteLength);

        List<TextSegment> segments = [];
        int cursor = 0; // char index

        foreach (FacetInput f in ordered)
        {
            long byteStart = Math.Clamp(f.ByteStart, 0, byteLength);
            long byteEnd = Math.Clamp(f.ByteEnd, 0, byteLength);
            if (byteEnd <= byteStart)
                continue;

            int start = ResolveChar(charIndexAtByte, (int)byteStart, roundUp: true);
            int end = ResolveChar(charIndexAtByte, (int)byteEnd, roundUp: false);

            if (start < cursor || end <= start)
                continue; // overlaps a facet already emitted, or resolved to nothing

            if (start > cursor)
                segments.Add(new TextSegment(FacetKind.Plain, text[cursor..start], null));

            segments.Add(new TextSegment(f.Kind, text[start..end], f.Target));
            cursor = end;
        }

        if (cursor < text.Length)
            segments.Add(new TextSegment(FacetKind.Plain, text[cursor..], null));

        return segments;
    }

    private static int[] BuildByteToCharMap(string text, out int byteLength)
    {
        byteLength = Encoding.UTF8.GetByteCount(text);
        int[] map = new int[byteLength + 1];
        Array.Fill(map, -1);

        int byteOffset = 0;
        for (int i = 0; i < text.Length;)
        {
            map[byteOffset] = i;

            int charLen = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
            byteOffset += Encoding.UTF8.GetByteCount(text.AsSpan(i, charLen));
            i += charLen;
        }

        map[byteLength] = text.Length;
        return map;
    }

    /// <summary>
    /// Maps a byte offset to a char index. Offsets inside a multi-byte sequence (which a
    /// well-formed facet never produces) snap outward so the facet still covers whole chars.
    /// </summary>
    private static int ResolveChar(int[] map, int byteOffset, bool roundUp)
    {
        if (map[byteOffset] >= 0)
            return map[byteOffset];

        if (roundUp)
        {
            for (int b = byteOffset + 1; b < map.Length; b++)
            {
                if (map[b] >= 0)
                    return map[b];
            }
            return map[^1];
        }

        for (int b = byteOffset - 1; b >= 0; b--)
        {
            if (map[b] >= 0)
                return map[b];
        }
        return 0;
    }
}
