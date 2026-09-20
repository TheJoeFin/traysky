using System;

namespace Traysky.Services;

/// <summary>
/// Lets any control ask the shell to show a full-size image over the popup instead of
/// launching the browser. <see cref="Pages.ShellPage"/> owns the actual overlay and is the
/// only subscriber; this just decouples <see cref="Controls.EmbedPresenter"/> from it.
/// </summary>
public static class ImageViewerService
{
    public static event EventHandler<Uri>? ImageRequested;

    public static void Show(Uri imageUri) => ImageRequested?.Invoke(null, imageUri);
}
