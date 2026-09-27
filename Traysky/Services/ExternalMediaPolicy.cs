using System;
using System.Diagnostics.CodeAnalysis;

namespace Traysky.Services;

/// <summary>An image or GIF that a link card actually points at, ready to show inline.</summary>
public sealed record ExternalMedia(Uri Uri, string AltText, double? AspectRatio);

/// <summary>
/// Decides when a link card (<c>app.bsky.embed.external</c>) is really just a GIF or picture,
/// so it can be shown as one instead of as a card, the way bsky.app plays a GIF picked from its
/// Tenor/Klipy picker. Pure .NET on purpose.
/// </summary>
public static class ExternalMediaPolicy
{
    private static readonly string[] ImageExtensions = [".gif", ".png", ".jpg", ".jpeg", ".webp"];

    public static bool TryGetMedia(string? url, string? title, string? description, [NotNullWhen(true)] out ExternalMedia? media)
    {
        media = null;
        if (!BlueskyLinks.TryParseWebUri(url, out Uri? uri))
            return false;

        Uri? imageUri = IsImagePath(uri.AbsolutePath) ? uri : GiphyPageToImage(uri);
        if (imageUri is null)
            return false;

        media = new ExternalMedia(imageUri, AltTextOf(title, description), AspectOf(uri.Query));
        return true;
    }

    private static bool IsImagePath(string path)
    {
        foreach (string extension in ImageExtensions)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// A giphy.com/gifs/some-words-ID page link, which bsky.app also plays inline, mapped to the
    /// GIF itself. Other Giphy pages (search, channels) stay link cards.
    /// </summary>
    private static Uri? GiphyPageToImage(Uri uri)
    {
        if (!uri.Host.Equals("giphy.com", StringComparison.OrdinalIgnoreCase)
            && !uri.Host.Equals("www.giphy.com", StringComparison.OrdinalIgnoreCase))
            return null;

        string[] parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !parts[0].Equals("gifs", StringComparison.OrdinalIgnoreCase))
            return null;

        string id = parts[1][(parts[1].LastIndexOf('-') + 1)..];
        if (id.Length == 0 || !IsAlphanumeric(id))
            return null;

        return new Uri($"https://i.giphy.com/media/{id}/giphy.gif");
    }

    private static bool IsAlphanumeric(string value)
    {
        foreach (char c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The GIF pickers write the alt text into the card description as "Alt: …"; otherwise the
    /// title is the best description there is.
    /// </summary>
    private static string AltTextOf(string? title, string? description)
    {
        string? text = description?.Trim();
        if (!string.IsNullOrEmpty(text) && text.StartsWith("alt:", StringComparison.OrdinalIgnoreCase))
            return text[4..].Trim();

        return title?.Trim() is { Length: > 0 } t ? t : text ?? string.Empty;
    }

    /// <summary>Tenor and Klipy links carry the GIF's size as <c>ww</c>/<c>hh</c> query parameters.</summary>
    private static double? AspectOf(string query)
    {
        int? width = null, height = null;
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0 || !int.TryParse(pair.AsSpan(eq + 1), out int value) || value <= 0)
                continue;

            switch (pair[..eq])
            {
                case "ww": width = value; break;
                case "hh": height = value; break;
            }
        }
        return width is not null && height is not null ? (double)width.Value / height.Value : null;
    }
}
