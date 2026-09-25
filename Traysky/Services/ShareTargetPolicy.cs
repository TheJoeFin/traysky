using System;
using System.Collections.Generic;
using System.IO;

namespace Traysky.Services;

/// <summary>
/// Turns what another app handed over through the Windows share sheet into compose-box
/// content. Pure .NET so the text rules are testable; <see cref="ShareTargetService"/> does
/// the WinRT reading and <see cref="ComposeAttachmentPolicy"/> owns which files are postable.
/// </summary>
public static class ShareTargetPolicy
{
    /// <summary>
    /// The post text for one share. Browsers typically send the page title plus a web link, and
    /// sometimes the same URL again as text, so the link is only appended when the text doesn't
    /// already contain it, and the title only stands in when there's no text of its own.
    /// </summary>
    public static string ComposeText(string? title, string? text, string? link)
    {
        var parts = new List<string>(2);
        string body = text?.Trim() ?? string.Empty;
        string url = link?.Trim() ?? string.Empty;

        if (body.Length > 0)
            parts.Add(body);
        else if (url.Length > 0 && !string.IsNullOrWhiteSpace(title) && !string.Equals(title.Trim(), url, StringComparison.Ordinal))
            parts.Add(title.Trim());

        if (url.Length > 0 && !body.Contains(url, StringComparison.OrdinalIgnoreCase))
            parts.Add(url);

        return string.Join("\n", parts);
    }

    /// <summary>
    /// Adds shared text to a draft the user may already have going, below it with a blank line,
    /// rather than throwing their work away.
    /// </summary>
    public static string MergeIntoDraft(string? draft, string shared)
    {
        if (string.IsNullOrWhiteSpace(shared))
            return draft ?? string.Empty;
        if (string.IsNullOrWhiteSpace(draft))
            return shared;

        return draft.TrimEnd() + "\n\n" + shared;
    }

    /// <summary>Whether a shared file is an image or video a post can carry.</summary>
    public static bool IsSupportedFile(string fileName) =>
        ComposeAttachmentPolicy.MimeTypeFor(Path.GetExtension(fileName)) is not null;
}
