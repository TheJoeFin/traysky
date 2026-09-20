using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>
/// One image or video the user has picked for a post but not yet uploaded. Held entirely in
/// memory (bytes read up front) so the compose box doesn't need repeated, possibly-sandboxed
/// access back to the original file; uploaded to a <c>Blob</c> only when the post is sent.
/// </summary>
public sealed partial class ComposeAttachmentItem : ObservableObject
{
    public byte[] Bytes { get; init; } = [];
    public string MimeType { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public ComposeAttachmentKind Kind { get; init; }
    public int? PixelWidth { get; init; }
    public int? PixelHeight { get; init; }

    /// <summary>Null for video — no frame is extracted, the UI just shows a video glyph.</summary>
    public BitmapImage? Thumbnail { get; init; }

    [ObservableProperty]
    public partial string AltText { get; set; } = string.Empty;

    public bool IsVideo => Kind == ComposeAttachmentKind.Video;

    public bool HasAspectRatio => PixelWidth is > 0 && PixelHeight is > 0;
}
