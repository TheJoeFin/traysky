using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.AtProto.Repo;
using idunno.Bluesky;
using idunno.Bluesky.Actor;
using idunno.Bluesky.Embed;
using idunno.Bluesky.Feed;
using idunno.Bluesky.Video;
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
using Windows.ApplicationModel.DataTransfer;
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
        Drafts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowNoDrafts));
        Attachments.CollectionChanged += (_, _) =>
        {
            if (!HasVideoAttachment)
                VideoLimitsText = null;
            if (UploadPhase == UploadPhase.Cancelled)
                SetPhase(UploadPhase.Idle); // the "Upload cancelled" note is stale once the attachments change
            OnPropertyChanged(nameof(HasAttachments));
            OnPropertyChanged(nameof(HasVideoAttachment));
            OnPropertyChanged(nameof(ImageAttachmentCount));
            OnPropertyChanged(nameof(CanAttachImage));
            OnPropertyChanged(nameof(CanAttachVideo));
            PostCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasLinkCard));
            OnPropertyChanged(nameof(HasLinkCardPlaceholder));
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
    [NotifyPropertyChangedFor(nameof(InteractionSummary))]
    [NotifyPropertyChangedFor(nameof(HasCustomInteraction))]
    public partial PostItem? ReplyTo { get; private set; }

    /// <summary>Who may reply to the post being written. Only applies to a post that starts a thread; a reply uses its thread's.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InteractionSummary))]
    [NotifyPropertyChangedFor(nameof(HasCustomInteraction))]
    public partial ReplyChoice ReplySetting { get; set; } = ReplyChoice.Everyone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InteractionSummary))]
    [NotifyPropertyChangedFor(nameof(HasCustomInteraction))]
    public partial bool AllowQuotes { get; set; } = true;

    /// <summary>"Anyone can reply · Quotes allowed": shown on the compose box's settings button.</summary>
    public string InteractionSummary => InteractionGatePolicy.Summary(ReplySetting, AllowQuotes, ReplyTo is not null);

    /// <summary>Whether the controls differ from the defaults, so the button can stand out.</summary>
    /// <summary>The account's curation lists, for the "who can reply" dialog. A reply has no reply controls (they belong to the thread's root), so it offers none.</summary>
    public async Task<IReadOnlyList<ListItem>> LoadReplyGateListsAsync()
    {
        if (ReplyTo is not null)
            return [];

        return (PreviewMode.IsEnabled ? PreviewMode.SampleLists() : await ListsService.GetOwnCurationListsAsync()) ?? [];
    }

    /// <summary>Stores what the reply / quote dialog chose; a null <paramref name="reply"/> leaves the reply setting alone.</summary>
    public void ApplyInteractionChoice(ReplyChoice? reply, bool allowQuotes)
    {
        if (reply is not null)
            ReplySetting = reply;
        AllowQuotes = allowQuotes;
    }

    public bool HasCustomInteraction =>!InteractionGatePolicy.IsDefault(ReplyTo is null ? ReplySetting : ReplyChoice.Everyone, AllowQuotes);

    /// <summary>The draft the box was restored from, so saving again updates it and posting removes it.</summary>
    private TimestampIdentifier? _draftId;

    public ObservableCollection<DraftItem> Drafts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoDrafts))]
    public partial bool IsLoadingDrafts { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDraftsStatus))]
    public partial string? DraftsStatus { get; private set; }

    public bool HasDraftsStatus => !string.IsNullOrEmpty(DraftsStatus);

    public bool ShowNoDrafts => !IsLoadingDrafts && Drafts.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContext))]
    [NotifyPropertyChangedFor(nameof(ContextLabel))]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    [NotifyPropertyChangedFor(nameof(HasLinkCard))]
    [NotifyPropertyChangedFor(nameof(HasLinkCardPlaceholder))]
    public partial PostItem? QuoteOf { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PostCommand))]
    public partial bool IsPosting { get; private set; }

    [ObservableProperty]
    public partial string? UploadStatus { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancelUpload))]
    [NotifyCanExecuteChangedFor(nameof(CancelUploadCommand))]
    public partial UploadPhase UploadPhase { get; private set; }

    public bool CanCancelUpload => UploadProgressPolicy.CanCancel(UploadPhase);

    /// <summary>0–1 while a multipart video upload reports progress; null when indeterminate.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUploadProgress))]
    [NotifyPropertyChangedFor(nameof(UploadProgressValue))]
    public partial double? UploadProgress { get; private set; }

    public bool HasUploadProgress => UploadProgress is not null;

    public double UploadProgressValue => UploadProgress ?? 0;

    /// <summary>"3 videos, 120 MB left today", from the last <c>getUploadLimits</c> check; null when unlimited or unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVideoLimitsText))]
    public partial string? VideoLimitsText { get; private set; }

    public bool HasVideoLimitsText => !string.IsNullOrEmpty(VideoLimitsText);

    // The compose box is a singleton, so closing the popup or navigating away does not cancel an
    // in-flight upload: it finishes (or fails) in the background and the post goes out. Drafts only
    // store text and picked files, never upload state, so a draft can't capture a half-finished upload.
    private CancellationTokenSource? _uploadCts;

    private void SetPhase(UploadPhase phase, bool isVideo = false)
    {
        UploadPhase = phase;
        UploadStatus = UploadProgressPolicy.StatusText(phase, isVideo, Attachments.Count);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    /// <summary>Images or a video picked for this post, not yet uploaded. Images and video are mutually exclusive.</summary>
    public ObservableCollection<ComposeAttachmentItem> Attachments { get; } = [];

    /// <summary>Raised on the UI thread after a post goes through, so the timeline can refresh.</summary>
    public event EventHandler? Posted;

    /// <summary>Bumped just before <see cref="Posted"/>, so a page can tell whether a post went through while it wasn't showing.</summary>
    public int PostedCount { get; private set; }

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
    private void RemoveAttachment(ComposeAttachmentItem attachment)
    {
        if (UploadProgressPolicy.CanEditAttachments(UploadPhase))
            Attachments.Remove(attachment);
    }

    [RelayCommand(CanExecute = nameof(CanCancelUpload))]
    private void CancelUpload() => _uploadCts?.Cancel();

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

    /// <summary>
    /// Routes clipboard content (Ctrl+V in the editor) into the same attachment pipeline as the
    /// file picker instead of the editor's own paste - a screenshot or a file copied from
    /// Explorer needs to become a Blob upload, not text/inline content dropped into the post
    /// body. Handles a raw bitmap (screenshot, image copied from a browser) via
    /// <see cref="DataPackageView.GetBitmapAsync"/> and one or more files copied from Explorer
    /// via <see cref="StandardDataFormats.StorageItems"/>, applying the same image/video
    /// exclusivity and count rules as picking files does.
    /// </summary>
    public async Task TryAttachFromClipboardAsync(DataPackageView dataPackageView)
    {
        Error = null;

        if (dataPackageView.Contains(StandardDataFormats.StorageItems))
        {
            IReadOnlyList<IStorageItem> items = await dataPackageView.GetStorageItemsAsync();
            bool attachedAny = await AddFilesAsync(items.OfType<StorageFile>());

            if (!attachedAny && Error is null && items.Count > 0)
                Error = "That doesn't contain a supported image or video.";

            return;
        }

        if (dataPackageView.Contains(StandardDataFormats.Bitmap))
        {
            if (!CanAttachImage)
            {
                Error = $"Up to {ComposeAttachmentPolicy.MaxImages} images per post, or one video.";
                return;
            }

            RandomAccessStreamReference streamRef = await dataPackageView.GetBitmapAsync();
            await AddClipboardBitmapAsync(streamRef);
        }
    }

    /// <summary>
    /// Takes in something dragged onto the compose box. Files and pictures go through the same
    /// attach rules as paste; plain text (or a dragged link) is appended to the draft.
    /// </summary>
    public async Task AcceptDropAsync(DataPackageView dataPackageView)
    {
        ComposeDropKind kind = ClassifyDrop(dataPackageView);
        if (kind == ComposeDropKind.None)
            return;

        IsExpanded = true;

        if (kind == ComposeDropKind.Media)
        {
            await TryAttachFromClipboardAsync(dataPackageView);
            return;
        }

        string? dropped = dataPackageView.Contains(StandardDataFormats.Text)
            ? await dataPackageView.GetTextAsync()
            : (await dataPackageView.GetWebLinkAsync())?.AbsoluteUri;

        if (string.IsNullOrWhiteSpace(dropped))
            return;

        Error = null;
        Text = ShareTargetPolicy.MergeIntoDraft(Text, dropped.Trim());
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public static ComposeDropKind ClassifyDrop(DataPackageView dataPackageView) => ComposeDropPolicy.Classify(
        dataPackageView.Contains(StandardDataFormats.StorageItems),
        dataPackageView.Contains(StandardDataFormats.Bitmap),
        dataPackageView.Contains(StandardDataFormats.Text),
        dataPackageView.Contains(StandardDataFormats.WebLink));

    /// <summary>
    /// Takes in something shared to Traysky from another app (see <see cref="ShareTargetService"/>)
    /// as a fresh post: any reply/quote in progress is dropped, the shared text goes below
    /// whatever draft was already typed, and the files go through the same rules as picking them.
    /// </summary>
    public async Task AcceptShareAsync(StagedShare share)
    {
        ReplyTo = null;
        QuoteOf = null;
        Text = ShareTargetPolicy.MergeIntoDraft(Text, share.Text);
        Error = null;
        IsExpanded = true;

        var files = new List<StorageFile>(share.FilePaths.Count);
        foreach (string path in share.FilePaths)
            files.Add(await StorageFile.GetFileFromPathAsync(path));

        await AddFilesAsync(files);
    }

    /// <summary>
    /// Attaches files by extension, applying the image/video exclusivity and count rules; a file
    /// that doesn't fit leaves <see cref="Error"/> set. Returns whether anything was attached.
    /// </summary>
    private async Task<bool> AddFilesAsync(IEnumerable<StorageFile> files)
    {
        bool attachedAny = false;

        foreach (StorageFile file in files)
        {
            string extension = Path.GetExtension(file.Name);
            if (ComposeAttachmentPolicy.IsVideoExtension(extension))
            {
                if (!CanAttachVideo)
                {
                    Error = "A post can have images or one video, not both.";
                    continue;
                }

                await AddVideoAsync(file);
                attachedAny = true;
            }
            else if (ComposeAttachmentPolicy.IsImageExtension(extension))
            {
                if (!CanAttachImage)
                {
                    Error = $"Up to {ComposeAttachmentPolicy.MaxImages} images per post, or one video.";
                    continue;
                }

                await AddImageAsync(file);
                attachedAny = true;
            }
        }

        return attachedAny;
    }

    /// <summary>
    /// A clipboard bitmap has no filename or guaranteed encoding, so it's decoded and re-encoded
    /// as PNG rather than trusted as-is, to match what <see cref="ComposeAttachmentPolicy"/> and
    /// Bluesky's blob upload expect.
    /// </summary>
    private async Task AddClipboardBitmapAsync(RandomAccessStreamReference streamRef)
    {
        using IRandomAccessStreamWithContentType sourceStream = await streamRef.OpenReadAsync();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(sourceStream);
        SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();

        using var encodedStream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, encodedStream);
        encoder.SetSoftwareBitmap(softwareBitmap);
        await encoder.FlushAsync();

        var bytes = new byte[encodedStream.Size];
        encodedStream.Seek(0);
        using (var reader = new DataReader(encodedStream))
        {
            await reader.LoadAsync((uint)encodedStream.Size);
            reader.ReadBytes(bytes);
            // Leaves encodedStream open - it's reused below for the thumbnail - the same
            // precaution ToInMemoryStreamAsync takes with DataWriter.DetachStream().
            reader.DetachStream();
        }

        await AddImageBytesAsync(bytes, "image/png", $"pasted-{DateTime.Now:yyyyMMddHHmmss}.png");
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

        await AddImageBytesAsync(await ReadAllBytesAsync(file), mimeType, file.Name);
    }

    /// <summary>
    /// Adds an image, shrinking it first if it's over the blob limit: a gentle pass that keeps every
    /// pixel runs automatically; if that isn't enough the image is added flagged as oversized, with a
    /// Compress button that applies the lossy ladder. Animated GIFs can't be re-encoded, so they are rejected.
    /// </summary>
    private async Task AddImageBytesAsync(byte[] bytes, string mimeType, string fileName)
    {
        bool oversized = ComposeAttachmentPolicy.ValidateSize(ComposeAttachmentKind.Image, bytes.Length) is not null;
        int? width = null, height = null;

        if (oversized)
        {
            if (!ImageCompressionPolicy.CanCompress(mimeType))
            {
                Error = ComposeAttachmentPolicy.ValidateSize(ComposeAttachmentKind.Image, bytes.Length);
                return;
            }

            CompressedImage? gentle = await TryCompressAsync(bytes, ImageCompressionPolicy.GentleSteps);
            if (gentle is not null)
            {
                bytes = gentle.Bytes;
                mimeType = "image/jpeg";
                fileName = Path.ChangeExtension(fileName, ".jpg");
                width = gentle.Width;
                height = gentle.Height;
                oversized = false;
            }
        }

        using InMemoryRandomAccessStream memoryStream = await ToInMemoryStreamAsync(bytes);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(memoryStream);

        Attachments.Add(new ComposeAttachmentItem
        {
            Bytes = bytes,
            MimeType = mimeType,
            FileName = fileName,
            Kind = ComposeAttachmentKind.Image,
            PixelWidth = width ?? (int)decoder.PixelWidth,
            PixelHeight = height ?? (int)decoder.PixelHeight,
            IsOversized = oversized
        });
    }

    private static async Task<CompressedImage?> TryCompressAsync(byte[] bytes, IReadOnlyList<ImageCompressionPolicy.Step> steps)
    {
        try
        {
            return await ImageCompressor.TryCompressAsync(bytes, steps, ComposeAttachmentPolicy.MaxImageBytes);
        }
        catch (Exception ex)
        {
            LogService.Warn("Compose", $"Image compression threw: {ex.Message}");
            return null;
        }
    }

    [RelayCommand]
    private async Task CompressAttachmentAsync(ComposeAttachmentItem attachment)
    {
        int index = Attachments.IndexOf(attachment);
        if (index < 0)
            return;

        Error = null;
        CompressedImage? result = await TryCompressAsync(attachment.Bytes, ImageCompressionPolicy.LossySteps);
        if (result is null)
        {
            Error = "Couldn't shrink this image enough. Try a smaller one.";
            return;
        }

        index = Attachments.IndexOf(attachment); // the list may have changed while compressing
        if (index < 0)
            return;

        Attachments[index] = new ComposeAttachmentItem
        {
            Bytes = result.Bytes,
            MimeType = "image/jpeg",
            FileName = Path.ChangeExtension(attachment.FileName, ".jpg"),
            Kind = ComposeAttachmentKind.Image,
            PixelWidth = result.Width,
            PixelHeight = result.Height,
            AltText = attachment.AltText
        };
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

        // Preflight against today's video allowance so it fails here rather than after a long upload.
        // If the check itself fails the attach still goes ahead; the video service is authoritative.
        if (BlueskySessionService.Instance.IsSignedIn)
        {
            var limits = await VideoUploadService.GetLimitsAsync(BlueskySessionService.Instance.Agent);
            if (limits is { Succeeded: true, Result: { } allowance })
            {
                string? limitError = ComposeAttachmentPolicy.ValidateVideoLimits(
                    allowance.CanUpload, allowance.RemainingDailyVideos, allowance.RemainingDailyBytes, bytes.Length);
                VideoLimitsText = ComposeAttachmentPolicy.LimitsSummary(allowance.RemainingDailyVideos, allowance.RemainingDailyBytes);
                if (limitError is not null)
                {
                    Error = limitError;
                    return;
                }
            }
        }

        Attachments.Add(new ComposeAttachmentItem
        {
            Bytes = bytes,
            MimeType = mimeType,
            FileName = file.Name,
            Kind = ComposeAttachmentKind.Video
        });
    }

    internal static async Task<byte[]> ReadAllBytesAsync(StorageFile file)
    {
        using IRandomAccessStream stream = await file.OpenReadAsync();
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    internal static async Task<InMemoryRandomAccessStream> ToInMemoryStreamAsync(byte[] bytes)
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

    /// <summary>
    /// Live results for the RichSuggestBox '@' popup. Debounced slightly since it fires on every
    /// keystroke; <see cref="Traysky.Controls.ComposeBox"/> also discards a stale response that
    /// lands after a newer request, so a slow reply here just never gets shown.
    /// </summary>
    public async Task<IReadOnlyList<MentionSuggestionItem>> SearchMentionsAsync(string query)
    {
        string trimmed = query.Trim();
        if (trimmed.Length == 0)
            return [];

        try
        {
            await Task.Delay(150);

            AtProtoHttpResult<PagedViewReadOnlyCollection<ProfileViewBasic>> result =
                await BlueskySessionService.Instance.Agent.SearchActorsTypeahead(trimmed, limit: 8);

            if (!result.Succeeded || result.Result is null)
                return [];

            return result.Result.Select(FeedMapper.ToMentionSuggestion).ToList();
        }
        catch (Exception ex)
        {
            LogService.Warn("Compose", $"Mention search for '{trimmed}' failed: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Results for the RichSuggestBox '#' popup. Bluesky has no tag-search API, so this is just
    /// the user's own recently-used tags (<see cref="SettingsService.RecentHashtags"/>) filtered
    /// by what they've typed, plus the typed text itself so it can always be picked as a token.
    /// </summary>
    public IReadOnlyList<HashtagSuggestionItem> SearchHashtags(string query)
    {
        string normalized = HashtagSuggestionPolicy.Normalize(query);
        IReadOnlyList<string> matches = HashtagSuggestionPolicy.Filter(SettingsService.RecentHashtags, normalized);

        var results = new List<HashtagSuggestionItem>(matches.Count + 1);
        if (normalized.Length > 0 && !matches.Any(t => string.Equals(t, normalized, StringComparison.OrdinalIgnoreCase)))
            results.Add(new HashtagSuggestionItem { Tag = normalized, IsRecent = false });

        results.AddRange(matches.Select(t => new HashtagSuggestionItem { Tag = t, IsRecent = true }));
        return results;
    }

    /// <summary>Remembers a chosen '#' tag so it resurfaces next time the user starts typing one like it.</summary>
    public void RecordHashtagUsed(string tag) =>
        SettingsService.RecentHashtags = HashtagSuggestionPolicy.WithRecentTag(SettingsService.RecentHashtags, tag);

    partial void OnTextChanged(string value)
    {
        SettingsService.ComposeDraft = value;
        if (Error is not null)
            Error = null;

        _ = UpdateLinkCardAsync(value);
    }

    private EmbeddedExternal? _linkCard;
    private CancellationTokenSource? _linkCardCts;
    private Uri? _linkCardUri;
    private Uri? _dismissedLinkUri;

    /// <summary>The card to preview and attach, hidden whenever images, video or a quote already use the post's one embed slot.</summary>
    public bool HasLinkCard => _linkCard is not null && LinkCardPolicy.CanAttachCard(HasAttachments, QuoteOf is not null);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLinkCardPlaceholder))]
    public partial bool IsLoadingLinkCard { get; private set; }

    /// <summary>Shown while the card is being fetched, so the box doesn't jump in size only when it lands.</summary>
    public bool HasLinkCardPlaceholder => IsLoadingLinkCard && LinkCardPolicy.CanAttachCard(HasAttachments, QuoteOf is not null);

    public string LinkCardHost => _linkCardUri?.Host ?? string.Empty;

    public string LinkCardTitle => _linkCard?.External.Title ?? string.Empty;

    public string LinkCardDescription => _linkCard?.External.Description ?? string.Empty;

    public bool HasLinkCardDescription => !string.IsNullOrEmpty(LinkCardDescription);

    public Uri? LinkCardThumbnail { get; private set; }

    public bool HasLinkCardThumbnail => LinkCardThumbnail is not null;

    private void NotifyLinkCardChanged()
    {
        OnPropertyChanged(nameof(HasLinkCard));
        OnPropertyChanged(nameof(HasLinkCardPlaceholder));
        OnPropertyChanged(nameof(LinkCardHost));
        OnPropertyChanged(nameof(LinkCardTitle));
        OnPropertyChanged(nameof(LinkCardDescription));
        OnPropertyChanged(nameof(HasLinkCardDescription));
        OnPropertyChanged(nameof(LinkCardThumbnail));
        OnPropertyChanged(nameof(HasLinkCardThumbnail));
    }

    private void ClearLinkCard()
    {
        _linkCardCts?.Cancel();
        _linkCardCts = null;
        _linkCardUri = null;
        _linkCard = null;
        LinkCardThumbnail = null;
        IsLoadingLinkCard = false;
        NotifyLinkCardChanged();
    }

    [RelayCommand]
    private void DismissLinkCard()
    {
        _dismissedLinkUri = _linkCardUri;
        ClearLinkCard();
    }

    /// <summary>
    /// Keeps the link card in step with the draft: after typing pauses, the first link in the
    /// text is looked up (Open Graph title/description/image, as bsky.app does) and previewed.
    /// A link the user dismissed the card for stays dismissed until it leaves the text.
    /// </summary>
    private async Task UpdateLinkCardAsync(string text)
    {
        Uri? uri = LinkCardPolicy.FirstLink(text);

        if (uri is null)
        {
            _dismissedLinkUri = null;
            if (_linkCardUri is not null || _linkCardCts is not null)
                ClearLinkCard();
            return;
        }

        if (uri == _linkCardUri || uri == _dismissedLinkUri)
            return;

        // A different link than the one on screen or in flight: drop it and start over.
        ClearLinkCard();
        _dismissedLinkUri = null;

        if (!BlueskySessionService.Instance.IsSignedIn)
            return;

        var cts = new CancellationTokenSource();
        _linkCardCts = cts;
        _linkCardUri = uri;
        IsLoadingLinkCard = true;
        OnPropertyChanged(nameof(LinkCardHost));

        try
        {
            await Task.Delay(LinkCardPolicy.Debounce, cts.Token);

            BlueskyAgent agent = BlueskySessionService.Instance.Agent;
            EmbeddedExternal? card = await agent.CreateOpenGraphEmbeddedCardGenerator().Generate(uri, cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            if (card is null)
                return;

            _linkCard = card;
            LinkCardThumbnail = card.External.Thumbnail is { } blob
                ? LinkCardPolicy.ThumbnailPreviewUri(agent.Did?.ToString(), blob.Reference.Link.ToString())
                : null;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            LogService.Warn("Compose", $"Link preview for '{uri.Host}' failed: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_linkCardCts, cts))
            {
                IsLoadingLinkCard = false;
                NotifyLinkCardChanged();
            }
        }
    }

    [RelayCommand]
    private async Task RefreshDraftsAsync()
    {
        if (IsLoadingDrafts)
            return;

        IsLoadingDrafts = true;
        DraftsStatus = null;
        try
        {
            List<DraftItem>? drafts = PreviewMode.IsEnabled ? PreviewMode.SampleDrafts() : await DraftsService.GetAllAsync();
            if (drafts is null)
            {
                DraftsStatus = BlueskySessionService.Instance.IsSignedIn ? "Couldn't load your drafts. Try again in a moment." : "Sign in to use drafts.";
                return;
            }

            Drafts.Clear();
            foreach (DraftItem draft in drafts)
                Drafts.Add(draft);
        }
        finally
        {
            IsLoadingDrafts = false;
        }
    }

    /// <summary>Saves what's in the box as a draft (updating the one it was restored from) and leaves the box as it is.</summary>
    [RelayCommand]
    private Task SaveDraftAsync() => SaveDraftCoreAsync(clearAfter: false);

    /// <summary>Saves the draft and empties the box, for setting a post aside. Refuses while media is attached, since a draft can't hold it.</summary>
    [RelayCommand]
    private Task SaveDraftAndClearAsync() => SaveDraftCoreAsync(clearAfter: true);

    private async Task SaveDraftCoreAsync(bool clearAfter)
    {
        DraftsStatus = null;

        if (!BlueskySessionService.Instance.IsSignedIn)
        {
            DraftsStatus = "Sign in to save drafts.";
            return;
        }

        if (!DraftPolicy.HasStorableContent(Text, QuoteOf is not null))
        {
            DraftsStatus = "Write something first. A draft needs some text or a quoted post.";
            return;
        }

        if (clearAfter && HasAttachments)
        {
            DraftsStatus = "Remove the attached media first, or use Save draft: a draft can't hold images or video.";
            return;
        }

        string text = Text.Trim();
        StrongReference? quote = QuoteOf?.Reference;
        ReplyChoice reply = ReplyTo is null ? ReplySetting : ReplyChoice.Everyone;

        bool saved;
        if (PreviewMode.IsEnabled)
            saved = true;
        else if (_draftId is { } existing)
            saved = await DraftsService.UpdateAsync(existing, text, quote, reply, AllowQuotes);
        else
        {
            _draftId = await DraftsService.CreateAsync(text, quote, reply, AllowQuotes);
            saved = _draftId is not null;
        }

        if (!saved)
        {
            DraftsStatus = "Couldn't save the draft. What you wrote is still here.";
            return;
        }

        string? note = DraftPolicy.SaveNote(ImageAttachmentCount, HasVideoAttachment, ReplyTo is not null);

        if (clearAfter)
        {
            _draftId = null;
            Text = string.Empty;
            ReplyTo = null;
            QuoteOf = null;
            ReplySetting = ReplyChoice.Everyone;
            AllowQuotes = true;
            IsExpanded = false;
        }

        await RefreshDraftsAsync();
        DraftsStatus = note is null ? "Draft saved." : $"Draft saved. {note}";
    }

    /// <summary>Raised when opening a draft would overwrite what's in the box; the view that has the Drafts flyout open asks, then calls <see cref="RestoreDraftAsync"/>.</summary>
    public event EventHandler<DraftItem>? RestoreConfirmationRequested;

    [RelayCommand]
    private async Task OpenDraftAsync(DraftItem draft)
    {
        // Restoring the draft the box already came from loses nothing, so only other content needs a say-so.
        bool wouldOverwrite = PostTextPolicy.HasDraftContent(Text, Attachments.Count)
            && !(draft.Id is not null && Equals(draft.Id, _draftId) && draft.Text == Text.Trim());

        if (wouldOverwrite && RestoreConfirmationRequested is not null)
        {
            RestoreConfirmationRequested(this, draft);
            return;
        }

        await RestoreDraftAsync(draft);
    }

    /// <summary>Loads a draft into the box, replacing what's there. A draft that quoted a post that has since gone loses only the quote.</summary>
    public async Task RestoreDraftAsync(DraftItem draft)
    {
        DraftsStatus = null;

        PostItem? quote = null;
        if (draft.Quote is { } reference && !PreviewMode.IsEnabled)
        {
            quote = await FetchPostAsync(reference.Uri);
            if (quote is null)
                DraftsStatus = "The post this draft quoted couldn't be loaded, so the quote was left out.";
        }

        ReplyTo = null;
        QuoteOf = quote;
        Text = draft.Text;
        ReplySetting = draft.Reply;
        AllowQuotes = draft.AllowQuotes;
        _draftId = draft.Id;
        IsExpanded = true;
        Error = null;

        if (DraftsStatus is null && draft.HasNote)
            DraftsStatus = draft.Note;

        DraftRestored?.Invoke(this, EventArgs.Empty);
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised once a draft is in the box, so the Drafts flyout can close.</summary>
    public event EventHandler? DraftRestored;

    [RelayCommand]
    private async Task DeleteDraftAsync(DraftItem draft)
    {
        int index = Drafts.IndexOf(draft);
        if (index < 0)
            return;

        DraftsStatus = null;
        Drafts.RemoveAt(index);

        if (draft.Id is not { } id || PreviewMode.IsEnabled)
            return;

        if (await DraftsService.DeleteAsync(id))
        {
            if (Equals(_draftId, id))
                _draftId = null;
            return;
        }

        Drafts.Insert(Math.Min(index, Drafts.Count), draft);
        DraftsStatus = "Couldn't delete the draft. Try again in a moment.";
    }

    private static async Task<PostItem?> FetchPostAsync(AtUri uri)
    {
        try
        {
            await LabelService.Instance.EnsureLoadedAsync();
            AtProtoHttpResult<IReadOnlyCollection<PostView>> posts =
                await BlueskySessionService.Instance.Agent.GetPosts([uri], LabelService.Instance.Subscribed);

            PostView? post = posts.Succeeded ? posts.Result?.FirstOrDefault() : null;
            return post is null ? null : FeedMapper.ToPostItem(post);
        }
        catch (Exception ex)
        {
            LogService.Warn("Compose", $"Fetching a draft's quoted post failed: {ex.Message}");
            return null;
        }
    }

    private bool CanPost() => !IsPosting
        && !Attachments.Any(a => a.IsOversized)
        && (PostTextPolicy.CanPost(Text) || (HasAttachments && !PostTextPolicy.IsOverLimit(Text)));

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
        SetPhase(UploadPhase.Idle);
        string text = Text.Trim();
        string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        BlueskyAgent agent = BlueskySessionService.Instance.Agent;
        bool isVideo = HasVideoAttachment;

        using var uploadCts = new CancellationTokenSource();
        _uploadCts = uploadCts;
        CancellationToken ct = uploadCts.Token;

        try
        {
            AtProtoHttpResult<CreateRecordResult> result;

            if (HasAttachments)
            {
                var builder = new PostBuilder(text, langs: new[] { lang });

                if (isVideo)
                {
                    EmbeddedVideo? video = await UploadVideoAsync(agent, Attachments.First(a => a.IsVideo), ct);
                    if (video is null)
                        return;

                    builder.Add(video);
                }
                else
                {
                    var images = new List<EmbeddedImage>();
                    SetPhase(UploadProgressPolicy.Begin(UploadPhase));
                    foreach (ComposeAttachmentItem attachment in Attachments.ToList())
                    {
                        AtProtoHttpResult<Blob> uploadResult = await agent.UploadBlob(attachment.Bytes, attachment.MimeType, cancellationToken: ct);
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

                // Past this point the media is uploaded; cancelling is no longer offered and the record is created.
                SetPhase(UploadProgressPolicy.Complete(UploadPhase), isVideo);

                if (ReplyTo is not null)
                    builder = await builder.ReplyTo(ReplyTo.Reference, agent);
                if (QuoteOf is not null)
                    builder.Add(QuoteOf.Reference);

                await builder.ExtractFacets(new HashtagAwareFacetExtractor(agent.FacetExtractor), CancellationToken.None);
                result = await agent.Post(builder);
            }
            else
            {
                // Always a builder, never agent.Post(text): the library's own extractor would end a tag at a hyphen.
                var builder = new PostBuilder(text, langs: new[] { lang });
                if (HasLinkCard && _linkCard is not null)
                    builder.EmbedRecord(_linkCard);

                if (ReplyTo is not null)
                    builder = await builder.ReplyTo(ReplyTo.Reference, agent);
                if (QuoteOf is not null)
                    builder.Add(QuoteOf.Reference);

                await builder.ExtractFacets(new HashtagAwareFacetExtractor(agent.FacetExtractor), CancellationToken.None);
                result = await agent.Post(builder);
            }

            if (result.Succeeded)
            {
                LogService.Info("Compose", $"Posted {result.Result?.Uri}");
                if (ReplyTo is not null)
                    ReplyTo.ReplyCount++;
                if (QuoteOf is not null)
                    QuoteOf.RepostCount = QuoteOf.RepostCount; // quotes are counted separately; leave as-is

                // The gates are separate records keyed to the new post, so they follow the post going through.
                // A reply has no reply controls of its own: they belong to the thread's root.
                bool gatesApplied = true;
                string? newPostUri = result.Result?.Uri.ToString();
                if (newPostUri is not null && !InteractionGatePolicy.IsDefault(ReplyTo is null ? ReplySetting : ReplyChoice.Everyone, AllowQuotes))
                    gatesApplied = await GatesService.ApplyToNewPostAsync(newPostUri, ReplyTo is null ? ReplySetting : null, AllowQuotes);

                Text = string.Empty;
                ReplyTo = null;
                QuoteOf = null;
                Attachments.Clear();
                ReplySetting = ReplyChoice.Everyone;
                AllowQuotes = true;
                IsExpanded = false;
                PostedCount++;
                Posted?.Invoke(this, EventArgs.Empty);

                // The draft this post grew from has served its purpose.
                if (_draftId is { } sentDraft)
                {
                    _draftId = null;
                    _ = DraftsService.DeleteAsync(sentDraft);
                }

                if (!gatesApplied)
                    Error = "Posted, but the reply or quote settings didn't apply. Use \"Reply and quote settings\" on the post to try again.";
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
        catch (OperationCanceledException)
        {
            // The user cancelled: not an error. Attachments stay so they can retry; any blobs already
            // uploaded are simply abandoned (the PDS garbage-collects unreferenced blobs).
            LogService.Info("Compose", "Upload cancelled");
            SetPhase(UploadProgressPolicy.Cancel(UploadPhase), isVideo);
        }
        catch (Exception ex)
        {
            LogService.Error("Compose", "Post threw", ex);
            Error = "Couldn't reach Bluesky. Your draft is safe.";
        }
        finally
        {
            // An early return with an error leaves the phase active; settle it so nothing reads "Uploading…".
            SetPhase(UploadProgressPolicy.Fail(UploadPhase), isVideo);
            _uploadCts = null;
            UploadProgress = null;
            IsPosting = false;
        }
    }

    /// <summary>
    /// Uploads and waits for a video to finish processing. Bluesky's video pipeline is
    /// asynchronous (transcoding happens server-side), so unlike an image blob this needs a
    /// poll loop; returns null (with <see cref="Error"/> set) if the upload or processing fails.
    /// </summary>
    private async Task<EmbeddedVideo?> UploadVideoAsync(BlueskyAgent agent, ComposeAttachmentItem attachment, CancellationToken ct)
    {
        SetPhase(UploadProgressPolicy.Begin(UploadPhase), isVideo: true);
        UploadProgress = 0;
        VideoUploadOutcome outcome = await VideoUploadService.UploadAsync(
            agent, attachment.FileName, attachment.Bytes, attachment.MimeType, new Progress<double>(p => UploadProgress = p), ct);

        JobStatus? uploaded = outcome.Job;
        if (outcome.UseSingleRequest)
        {
            // The multipart route isn't usable; fall back to the original single request (no progress).
            UploadProgress = null;
            AtProtoHttpResult<JobStatus> uploadResult = await agent.UploadVideo(attachment.FileName, attachment.Bytes, attachment.MimeType, ct);
            uploaded = uploadResult.Succeeded ? uploadResult.Result : null;
        }

        if (uploaded is null)
        {
            Error = outcome.Error ?? "Couldn't upload the video. Try again.";
            return null;
        }

        UploadProgress = null;
        JobStatus job = uploaded;
        SetPhase(UploadProgressPolicy.BeginProcessing(UploadPhase), isVideo: true);

        // The transcoding job is polled rather than pushed; the budget is bounded because
        // failing loudly beats spinning forever.
        for (int attempt = 0; job.State is not (JobState.Completed or JobState.Failed) && !UploadProgressPolicy.IsPollBudgetSpent(attempt); attempt++)
        {
            await Task.Delay(UploadProgressPolicy.PollInterval, ct);
            AtProtoHttpResult<JobStatus> statusResult = await agent.GetJobStatus(job.JobId, ct);
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
