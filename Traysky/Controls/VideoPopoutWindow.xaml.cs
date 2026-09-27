using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using Traysky.Services;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Win32;
using Windows.Win32.Foundation;
using WinRT.Interop;
using WinUIEx;

namespace Traysky.Controls;

/// <summary>
/// A video popped out of the flyout into its own always-on-top, resizable window, so it keeps
/// playing while the user scrolls or after the flyout light-dismisses. One window is reused:
/// popping out another video replaces what it's playing.
/// </summary>
public sealed partial class VideoPopoutWindow : WindowEx
{
    private const double DefaultWidth = 480;
    private const double MaxDefaultHeight = 540;
    private const int ScreenMargin = 24;

    private static VideoPopoutWindow? s_current;

    private MediaPlayer? _player;

    private VideoPopoutWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(App.IsAppInDarkMode() ? "Assets\\Wings-Light.ico" : "Assets\\Wings-Dark.ico");
        IsAlwaysOnTop = true;
        IsMaximizable = false;
        IsMinimizable = true;
        IsResizable = true;

        Closed += (_, _) =>
        {
            ReleasePlayer();
            s_current = null;
        };
    }

    /// <summary>Plays <paramref name="request"/> in the pop-out window, opening it if needed,
    /// resuming from <paramref name="startAt"/>.</summary>
    public static void Show(VideoViewerRequest request, TimeSpan startAt)
    {
        bool isNew = s_current is null;
        s_current ??= new VideoPopoutWindow();
        s_current.Play(request, startAt);

        if (isNew)
        {
            s_current.SizeAndPlace(request.AspectRatio);

            // Shown without activation, so the flyout keeps focus and the user can go on
            // scrolling instead of the flyout light-dismissing the moment this appears.
            s_current.AppWindow.Show(activateWindow: false);
        }
    }

    public static void CloseCurrent() => s_current?.Close();

    private void Play(VideoViewerRequest request, TimeSpan startAt)
    {
        ReleasePlayer();

        _player = new MediaPlayer
        {
            AutoPlay = true,
            Source = MediaSource.CreateFromUri(request.PlaylistUri),
        };

        if (startAt > TimeSpan.Zero)
            _player.MediaOpened += (sender, _) => sender.PlaybackSession.Position = startAt;

        VideoPlayer.PosterSource = request.Thumbnail is null ? null : new BitmapImage(request.Thumbnail);
        VideoPlayer.SetMediaPlayer(_player);
    }

    private void ReleasePlayer()
    {
        if (_player is null)
            return;

        _player.Pause();
        VideoPlayer.SetMediaPlayer(null);
        _player.Dispose();
        _player = null;
    }

    /// <summary>Sizes the window to the video's shape and puts it in the bottom-right corner
    /// of the work area, out of the way of the flyout's content, picture-in-picture style.</summary>
    private void SizeAndPlace(double aspectRatio)
    {
        double aspect = aspectRatio > 0 ? aspectRatio : 16.0 / 9.0;
        double height = Math.Min(DefaultWidth / aspect, MaxDefaultHeight);
        double width = height * aspect;
        this.SetWindowSize(width, height);

        DisplayArea display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        RectInt32 work = display.WorkArea;
        uint dpi = PInvoke.GetDpiForWindow((HWND)WindowNative.GetWindowHandle(this));
        double scale = (dpi == 0 ? 96 : dpi) / 96.0;
        int margin = (int)Math.Round(ScreenMargin * scale);
        SizeInt32 size = AppWindow.Size;

        AppWindow.Move(new PointInt32(
            work.X + work.Width - size.Width - margin,
            work.Y + work.Height - size.Height - margin));
    }
}
