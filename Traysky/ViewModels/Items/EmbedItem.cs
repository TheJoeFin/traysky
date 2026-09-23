using System;
using System.Collections.Generic;

namespace Traysky.ViewModels.Items;

public enum EmbedKind
{
    None,
    Images,
    External,
    Quote,
    Video,

    /// <summary>A quoted post that was deleted, or whose author blocked the viewer.</summary>
    Unavailable
}

public sealed record EmbedImage(Uri Thumbnail, Uri FullSize, string AltText, double AspectRatio);

/// <summary>
/// The UI-shaped view of a post's embed. One flat record rather than a hierarchy so the
/// XAML template selector stays trivial; only the members for <see cref="Kind"/> are set.
/// </summary>
public sealed class EmbedItem
{
    public EmbedKind Kind { get; init; }

    // Images / Video
    public IReadOnlyList<EmbedImage> Images { get; init; } = [];

    // External link card
    public Uri? ExternalUri { get; init; }
    public string? ExternalHost { get; init; }
    public string? ExternalTitle { get; init; }
    public string? ExternalDescription { get; init; }
    public Uri? ExternalThumbnail { get; init; }

    // Quote
    public string? QuoteAuthorName { get; init; }
    public string? QuoteAuthorHandle { get; init; }
    public Uri? QuoteAuthorAvatar { get; init; }
    public string? QuoteText { get; init; }
    public string? QuoteWebUrl { get; init; }
    public string? QuoteAtUri { get; init; }
    public IReadOnlyList<EmbedImage> QuoteImages { get; init; } = [];

    // Video
    public Uri? VideoThumbnail { get; init; }
    public Uri? VideoPlaylistUri { get; init; }
    public double VideoAspectRatio { get; init; } = 16.0 / 9.0;

    /// <summary>Where a click on the whole embed goes: the link, the quoted post, or nothing.</summary>
    public string? OpenUrl { get; init; }

    public bool HasImages => Images.Count > 0;
    public bool HasQuoteImages => QuoteImages.Count > 0;
    public bool HasQuote => QuoteAtUri is not null;
    public bool HasExternalThumbnail => ExternalThumbnail is not null;
    public bool HasQuoteAvatar => QuoteAuthorAvatar is not null;
    public bool HasQuoteText => !string.IsNullOrEmpty(QuoteText);
    public bool HasExternalDescription => !string.IsNullOrEmpty(ExternalDescription);
    public bool IsImages => Kind == EmbedKind.Images;
    public bool IsExternal => Kind == EmbedKind.External;
    public bool IsQuote => Kind == EmbedKind.Quote;
    public bool IsVideo => Kind == EmbedKind.Video;
    public bool HasVideoPlaylist => VideoPlaylistUri is not null;
    public bool IsUnavailable => Kind == EmbedKind.Unavailable;
}
