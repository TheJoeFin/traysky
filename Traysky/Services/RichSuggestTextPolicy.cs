namespace Traysky.Services;

/// <summary>
/// RichSuggestBox embeds a chosen '@'/'#' token as U+200B (zero width space) + prefix + display
/// text + U+200B, and RichEditBox's GetText appends a trailing paragraph-mark '\r'. Bluesky's own
/// mention/hashtag facet regexes expect plain "@handle"/"#tag" text bounded by ordinary
/// whitespace, so this runs before the text is counted, drafted to settings, or handed to
/// PostAsync's existing (unchanged) facet-extraction path.
/// </summary>
public static class RichSuggestTextPolicy
{
    private const char ZeroWidthSpace = '​';

    public static string StripTokenMarkup(string? richEditText)
    {
        if (string.IsNullOrEmpty(richEditText))
            return string.Empty;

        string trimmed = richEditText.TrimEnd('\r');
        return trimmed.IndexOf(ZeroWidthSpace) < 0 ? trimmed : trimmed.Replace(ZeroWidthSpace.ToString(), string.Empty);
    }
}
