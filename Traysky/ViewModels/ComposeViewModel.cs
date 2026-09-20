using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.AtProto.Repo;
using idunno.Bluesky;
using idunno.Bluesky.Embed;
using idunno.Bluesky.Video;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.Win32;

namespace Traysky.ViewModels;

/// <summary>
/// The compose box: a new post, a reply, or a quote. One instance lives for the app's life so
/// a half-written post survives the flyout being dismissed; the text is also saved to settings
/// so it survives a restart.
/// </summary>
public sealed partial class ComposeViewModel : ObservableObject
{
    private static readonly Lazy<ComposeViewModel> _instance = new(() => new ComposeViewModel());

    public static ComposeViewModel Instance => _instance.Value;

    private ComposeViewModel()
    {
        Text = SettingsService.ComposeDraft;
        IsExpanded = Text.Length > 0;
        Attachments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAttachments));
            OnPropertyChanged(nameof(HasVideoAttachment));
            OnPropertyChanged(nameof(ImageAttachmentCount));
            OnPropertyChanged(nameof(CanAttachImage));
            OnPropertyChanged(nameof(CanAttachVideo));
            PostCommand.NotifyCanExecuteChanged();
        };
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Counter))]
    [NotifyPropertyChangedFor(nameof(IsNearLimit))]
    [NotifyPropertyChangedFor(nameof(IsOverLimit))]
    [NotifyCanExecuteChangedFor(nameof(PostCommand))]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>Collapsed to a single "What's up?" line until focused or given a context.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContext))]
    [NotifyPropertyChangedFor(nameof(ContextLabel))]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    public partial PostItem? ReplyTo { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContext))]
    [NotifyPropertyChangedFor(nameof(ContextLabel))]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    public partial PostItem? QuoteOf { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PostCommand))]
    public partial bool IsPosting { get; private set; }

    [ObservableProperty]
    public partial string? UploadStatus { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    /// <summary>Images or a video picked for this post, not yet uploaded. Images and video are mutually exclusive.</summary>
    public ObservableCollection<ComposeAttachmentItem> Attachments { get; } = [];

    /// <summary>Raised on the UI thread after a post goes through, so the timeline can refresh.</summary>
    public event EventHandler? Posted;

    /// <summary>Raised when something wants the text box focused (tray "Compose" action, reply button).</summary>
    public event EventHandler? FocusRequested;

    public string Counter => $"{PostTextPolicy.CountGraphemes(Text)}/{PostTextPolicy.MaxGraphemes}";

    public bool IsNearLimit => PostTextPolicy.IsNearLimit(Text) && !PostTextPolicy.IsOverLimit(Text);

    public bool IsOverLimit => PostTextPolicy.IsOverLimit(Text);

    public bool HasContext => ReplyTo is not null || QuoteOf is not null;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasAttachments => Attachments.Count > 0;

    public bool HasVideoAttachment => Attachments.Any(a => a.IsVideo);

    public int ImageAttachmentCount => Attachments.Count(a => !a.IsVideo);

    public bool CanAttachImage => ComposeAttachmentPolicy.CanAddImage(ImageAttachmentCount, HasVideoAttachment);

    public bool CanAttachVideo => ComposeAttachmentPolicy.CanAddVideo(ImageAttachmentCount, HasVideoAttachment);

    public string ContextLabel => ReplyTo is not null
        ? $"Replying to @{ReplyTo.AuthorHandle}"
        : QuoteOf is not null ? $"Quoting @{QuoteOf.AuthorHandle}" : string.Empty;

    public string Placeholder => ReplyTo is not null ? "Write your reply" : QuoteOf is not null ? "Add a comment" : "What's up?";

    public void BeginReply(PostItem post)
    {
        QuoteOf = null;
        ReplyTo = post;
        IsExpanded = true;
        Error = null;
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public void BeginQuote(PostItem post)
    {
        ReplyTo = null;
        QuoteOf = post;
        IsExpanded = true;
        Error = null;
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public void BeginNew()
    {
        IsExpanded = true;
        Error = null;
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearContext()
    {
        ReplyTo = null;
        QuoteOf = null;
    }

    [RelayCommand]
    private void RemoveAttachment(ComposeAttachmentItem attachment) => Attachments.Remove(attachment);

    [RelayCommand]
    private Task AttachImageAsync() => AttachAsync(ComposeAttachmentKind.Image);

    [RelayCommand]
    private Task AttachVideoAsync() => AttachAsync(ComposeAttachmentKind.Video);

    private async Task AttachAsync(ComposeAttachmentKind kind)
    {
        if (kind == ComposeAttachmentKind.Image && !CanAttachImage)
        {
            Error = $"Up to {ComposeAttachmentPolicy.MaxImages} images per post, or one video.";
            return;
        }

        if (kind == ComposeAttachmentKind.Video && !CanAttachVideo)
        {
            Error = "A post can have images or one video, not both.";
            return;
        }

        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = kind == ComposeAttachmentKind.Video ? PickerLocationId.VideosLibrary : PickerLocationId.PicturesLibrary
        };
        foreach (string extension in kind == ComposeAttachmentKind.Image ? ComposeAttachmentPolicy.ImageExtensions : ComposeAttachmentPolicy.VideoExtensions)
            picker.FileTypeFilter.Add(extension);

        // The popup is the foreground window whenever this can be invoked, so there's no need
        // to thread a Window reference all the way down from App into a deeply-nested control.
        nint hwnd = PInvoke.GetForegroundWindow();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        Error = null;

        // The picker's own window takes the OS foreground while it's open, which isn't a WinUI
        // popup HWND the tray flyout already knows to ignore — without this it reads as focus
        // having left the flyout entirely and light-dismisses it mid-pick.
        using IDisposable suppression = LightDismissSuppressionService.Suppress();

        if (kind == ComposeAttachmentKind.Video)
        {
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is not null)
                await AddVideoAsync(file);
            return;
        }

        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        foreach (StorageFile file in files)
        {
            if (!CanAttachImage)
            {
                Error = $"Up to {ComposeAttachmentPolicy.MaxImages} images per post.";
                break;
            }

            await AddImageAsync(file);
        }
    }

    private async Task AddImageAsync(StorageFile file)
    {
        string extension = Path.GetExtension(file.Name);
        string? mimeType = ComposeAttachmentPolicy.MimeTypeFor(extension);
        if (mimeType is null || !ComposeAttachmentPolicy.IsImageExtension(extension))
        {
            Error = "Unsupported image type.";
            return;
        }

        byte[] bytes = await ReadAllBytesAsync(file);
        string? sizeError = ComposeAttachmentPolicy.ValidateSize(ComposeAttachmentKind.Image, bytes.Length);
        if (sizeError is not null)
        {
            Error = sizeError;
            return;
        }

        using InMemoryRandomAccessStream memoryStream = await ToInMemoryStreamAsync(bytes);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(memoryStream);

        memoryStream.Seek(0);
        var thumbnail = new BitmapImage();
        await thumbnail.SetSourceAsync(memoryStream);

        Attachments.Add(new ComposeAttachmentItem
        {
            Bytes = bytes,
            MimeType = mimeType,
            FileName = file.Name,
            Kind = ComposeAttachmentKind.Image,
            PixelWidth = (int)decoder.PixelWidth,
            PixelHeight = (int)decoder.PixelHeight,
            Thumbnail = thumbnail
        });
    }

    private async Task AddVideoAsync(StorageFile file)
    {
        string extension = Path.GetExtension(file.Name);
        string? mimeType = ComposeAttachmentPolicy.MimeTypeFor(extension);
        if (mimeType is null || !ComposeAttachmentPolicy.IsVideoExtension(extension))
        {
            Error = "Unsupported video type.";
            return;
        }

        byte[] bytes = await ReadAllBytesAsync(file);
        string? sizeError = ComposeAttachmentPolicy.ValidateSize(ComposeAttachmentKind.Video, bytes.Length);
        if (sizeError is not null)
        {
            Error = sizeError;
            return;
        }

        Attachments.Add(new ComposeAttachmentItem
        {
            Bytes = bytes,
            MimeType = mimeType,
            FileName = file.Name,
            Kind = ComposeAttachmentKind.Video
        });
    }

    private static async Task<byte[]> ReadAllBytesAsync(StorageFile file)
    {
        using IRandomAccessStream stream = await file.OpenReadAsync();
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static async Task<InMemoryRandomAccessStream> ToInMemoryStreamAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        return stream;
    }

    /// <summary>Collapse back to one line if nothing is typed, no reply/quote is pending, and nothing is attached.</summary>
    public void CollapseIfEmpty()
    {
        if (string.IsNullOrWhiteSpace(Text) && !HasContext && !HasAttachments)
            IsExpanded = false;
    }

    partial void OnTextChanged(string value)
    {
        SettingsService.ComposeDraft = value;
        if (Error is not null)
            Error = null;
    }

    private bool CanPost() => !IsPosting && (PostTextPolicy.CanPost(Text) || (HasAttachments && !PostTextPolicy.IsOverLimit(Text)));

    [RelayCommand(CanExecute = nameof(CanPost))]
    private async Task PostAsync()
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
        {
            Error = "Sign in to post.";
            return;
        }

        IsPosting = true;
        Error = null;
        UploadStatus = null;
        string text = Text.Trim();
        string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        BlueskyAgent agent = BlueskySessionService.Instance.Agent;

        try
        {
            AtProtoHttpResult<CreateRecordResult> result;

            if (HasAttachments)
            {
                var builder = new PostBuilder(text, langs: new[] { lang });

                if (HasVideoAttachment)
                {
                    EmbeddedVideo? video = await UploadVideoAsync(agent, Attachments.First(a => a.IsVideo));
                    if (video is null)
                        return;

                    builder.Add(video);
                }
                else
                {
                    var images = new List<EmbeddedImage>();
                    UploadStatus = Attachments.Count > 1 ? "Uploading images…" : "Uploading image…";
                    foreach (ComposeAttachmentItem attachment in Attachments)
                    {
                        AtProtoHttpResult<Blob> uploadResult = await agent.UploadBlob(attachment.Bytes, attachment.MimeType);
                        if (!uploadResult.Succeeded || uploadResult.Result is null)
                        {
                            Error = "Couldn't upload an image. Try again.";
                            return;
                        }

                        images.Add(new EmbeddedImage(
                            uploadResult.Result,
                            attachment.AltText,
                            attachment.HasAspectRatio ? new AspectRatio(attachment.PixelWidth!.Value, attachment.PixelHeight!.Value) : null));
                    }

                    builder.Add((ICollection<EmbeddedImage>)images);
                }

                UploadStatus = null;

                if (ReplyTo is not null)
                    builder = await builder.ReplyTo(ReplyTo.Reference, agent);
                if (QuoteOf is not null)
                    builder.Add(QuoteOf.Reference);

                await builder.ExtractFacets(agent.FacetExtractor, CancellationToken.None);
                result = await agent.Post(builder);
            }
            else if (ReplyTo is not null)
                result = await agent.ReplyTo(ReplyTo.Reference, text);
            else if (QuoteOf is not null)
                result = await agent.Quote(QuoteOf.Reference, text);
            else
                result = await agent.Post(text, langs: [lang]);

            if (result.Succeeded)
            {
                LogService.Info("Compose", $"Posted {result.Result?.Uri}");
                if (ReplyTo is not null)
                    ReplyTo.ReplyCount++;
                if (QuoteOf is not null)
                    QuoteOf.RepostCount = QuoteOf.RepostCount; // quotes are counted separately; leave as-is

                Text = string.Empty;
                ReplyTo = null;
                QuoteOf = null;
                Attachments.Clear();
                IsExpanded = false;
                Posted?.Invoke(this, EventArgs.Empty);
                return;
            }

            LogService.Warn("Compose", $"Post failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error} {result.AtErrorDetail?.Message}");
            Error = result.StatusCode switch
            {
                System.Net.HttpStatusCode.TooManyRequests => "You're posting too fast. Try again in a minute.",
                0 => "Couldn't reach Bluesky. Your draft is safe.",
                _ => string.IsNullOrEmpty(result.AtErrorDetail?.Message) ? "Post failed. Your draft is safe." : result.AtErrorDetail.Message
            };
        }
        catch (Exception ex)
        {
            LogService.Error("Compose", "Post threw", ex);
            Error = "Couldn't reach Bluesky. Your draft is safe.";
        }
        finally
        {
            IsPosting = false;
            UploadStatus = null;
        }
    }

    /// <summary>
    /// Uploads and waits for a video to finish processing. Bluesky's video pipeline is
    /// asynchronous (transcoding happens server-side), so unlike an image blob this needs a
    /// poll loop; returns null (with <see cref="Error"/> set) if the upload or processing fails.
    /// </summary>
    private async Task<EmbeddedVideo?> UploadVideoAsync(BlueskyAgent agent, ComposeAttachmentItem attachment)
    {
        UploadStatus = "Uploading video…";
        AtProtoHttpResult<JobStatus> uploadResult = await agent.UploadVideo(attachment.FileName, attachment.Bytes, attachment.MimeType);
        if (!uploadResult.Succeeded || uploadResult.Result is null)
        {
            Error = "Couldn't upload the video. Try again.";
            return null;
        }

        JobStatus job = uploadResult.Result;
        UploadStatus = "Processing video…";

        // The transcoding job is polled rather than pushed; two minutes covers all but
        // exceptionally long clips, and failing loudly beats spinning forever.
        for (int attempt = 0; job.State is not (JobState.Completed or JobState.Failed) && attempt < 60; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            AtProtoHttpResult<JobStatus> statusResult = await agent.GetJobStatus(job.JobId);
            if (!statusResult.Succeeded || statusResult.Result is null)
            {
                Error = "Couldn't check video processing status.";
                return null;
            }

            job = statusResult.Result;
        }

        if (job.State != JobState.Completed || job.Blob is null)
        {
            Error = string.IsNullOrEmpty(job.Message) ? "Video processing failed." : job.Message;
            return null;
        }

        return new EmbeddedVideo(
            job.Blob,
            captions: null,
            attachment.AltText,
            attachment.HasAspectRatio ? new AspectRatio(attachment.PixelWidth!.Value, attachment.PixelHeight!.Value) : null);
    }
}
