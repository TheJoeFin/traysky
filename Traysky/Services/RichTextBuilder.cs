using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Collections.Generic;
using Windows.System;

namespace Traysky.Services;

/// <summary>
/// Fills a <see cref="RichTextBlock"/> with a post's segments: plain runs, and hyperlinks for
/// links, mentions and tags that open in the browser.
/// </summary>
public static class RichTextBuilder
{
    private static long _lastLinkClickTicks;

    /// <summary>
    /// True for a short window after a hyperlink in any post was clicked. Hyperlinks are
    /// inlines, not UIElements, so their click does not stop the Tapped that reaches the card.
    /// </summary>
    public static bool WasLinkClickedRecently() =>
        Environment.TickCount64 - System.Threading.Volatile.Read(ref _lastLinkClickTicks) < 500;
    public static void Populate(RichTextBlock target, IReadOnlyList<TextSegment> segments)
    {
        target.Blocks.Clear();

        Paragraph paragraph = new();
        foreach (TextSegment segment in segments)
        {
            if (segment.Kind == FacetKind.Plain || segment.Target is null)
            {
                paragraph.Inlines.Add(new Run { Text = segment.Text });
                continue;
            }

            Hyperlink link = new()
            {
                UnderlineStyle = UnderlineStyle.None
            };
            link.Inlines.Add(new Run { Text = segment.Text });

            if (segment.Kind == FacetKind.Mention)
            {
                // A mention opens the profile inside the flyout.
                string did = segment.Target;
                link.Click += (_, _) =>
                {
                    System.Threading.Volatile.Write(ref _lastLinkClickTicks, Environment.TickCount64);
                    Pages.ProfilePage.Open(did);
                };
                ToolTipService.SetToolTip(link, "View profile");
            }
            else
            {
                string url = segment.Kind == FacetKind.Tag ? BlueskyLinks.HashtagUrl(segment.Target) : segment.Target;
                link.Click += (_, _) =>
                {
                    System.Threading.Volatile.Write(ref _lastLinkClickTicks, Environment.TickCount64);
                    _ = OpenAsync(url);
                };
                ToolTipService.SetToolTip(link, url);
            }
            paragraph.Inlines.Add(link);
        }

        target.Blocks.Add(paragraph);
    }

    /// <summary>Opens a web URL in the default browser. Fire-and-forget; failures are logged only.</summary>
    public static async System.Threading.Tasks.Task OpenAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            if (BlueskyLinks.TryParseWebUri(url, out Uri? uri))
                await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            LogService.Warn("Links", $"Could not open '{url}': {ex.Message}");
        }
    }
}
