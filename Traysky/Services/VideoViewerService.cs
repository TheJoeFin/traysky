using System;

namespace Traysky.Services;

public sealed record VideoViewerRequest(Uri PlaylistUri, Uri? Thumbnail, double AspectRatio);

/// <summary>
/// Lets any control ask the shell to play a video over the popup instead of launching the
/// browser. <see cref="Pages.ShellPage"/> owns the actual overlay and is the only subscriber;
/// this just decouples <see cref="Controls.EmbedPresenter"/> from it.
/// </summary>
public static class VideoViewerService
{
    public static event EventHandler<VideoViewerRequest>? VideoRequested;

    public static void Show(Uri playlistUri, Uri? thumbnail, double aspectRatio) =>
        VideoRequested?.Invoke(null, new VideoViewerRequest(playlistUri, thumbnail, aspectRatio));
}
