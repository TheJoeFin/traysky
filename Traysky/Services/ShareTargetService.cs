using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Traysky.Services;

/// <summary>One share, read off the share sheet and waiting on disk for the compose box.</summary>
public sealed record StagedShare(string Folder, string Text, IReadOnlyList<string> FilePaths);

/// <summary>
/// Receives content from the Windows share sheet. Windows launches a fresh process for every
/// share, while the compose box lives in the instance already sitting in the tray, so the
/// share is staged: the launched process reads everything out of the <see cref="ShareOperation"/>
/// into a folder under TemporaryFolder, tells Windows it's done, and signals
/// <see cref="ReceivedEventName"/>; the running instance then picks the folder up. Copying the
/// data out (rather than redirecting the activation) means nothing depends on the share
/// source keeping its data alive once the launched process exits.
/// </summary>
public static class ShareTargetService
{
    public const string ReceivedEventName = "Global\\Traysky_ShareReceived_Event";

    private const string TextFileName = "text.txt";
    private const string ReadyFileName = "ready";
    private const string MediaFolderName = "media";

    private static string StagingRoot => Path.Combine(ApplicationData.Current.TemporaryFolder.Path, "Share");

    /// <summary>
    /// Reads a share into the staging folder and reports it complete. Never throws: a failure is
    /// reported back to the share sheet and logged.
    /// </summary>
    public static async Task StageAsync(ShareOperation operation)
    {
        string folder = Path.Combine(StagingRoot, $"{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid():N}");

        try
        {
            operation.ReportStarted();
            DataPackageView data = operation.Data;
            string mediaFolder = Path.Combine(folder, MediaFolderName);
            Directory.CreateDirectory(mediaFolder);

            string? text = data.Contains(StandardDataFormats.Text) ? await data.GetTextAsync() : null;
            Uri? link = data.Contains(StandardDataFormats.WebLink) ? await data.GetWebLinkAsync() : null;

            int fileCount = 0;
            if (data.Contains(StandardDataFormats.StorageItems))
            {
                StorageFolder destination = await StorageFolder.GetFolderFromPathAsync(mediaFolder);
                foreach (IStorageItem item in await data.GetStorageItemsAsync())
                {
                    if (item is StorageFile file && ShareTargetPolicy.IsSupportedFile(file.Name))
                    {
                        await file.CopyAsync(destination, file.Name, NameCollisionOption.GenerateUniqueName);
                        fileCount++;
                    }
                }
            }

            if (fileCount == 0 && data.Contains(StandardDataFormats.Bitmap))
                await SaveBitmapAsPngAsync(await data.GetBitmapAsync(), Path.Combine(mediaFolder, $"shared-{DateTime.Now:yyyyMMddHHmmss}.png"));

            operation.ReportDataRetrieved();

            await File.WriteAllTextAsync(Path.Combine(folder, TextFileName), ShareTargetPolicy.ComposeText(data.Properties.Title, text, link?.ToString()));

            // Written last: the running instance ignores a folder until this exists, so it never
            // picks up a half-copied share.
            await File.WriteAllTextAsync(Path.Combine(folder, ReadyFileName), string.Empty);

            LogService.Info("Share", $"Staged a share ({fileCount} file(s), link={link is not null}, text={text is not null})");
            operation.ReportCompleted();
        }
        catch (Exception ex)
        {
            LogService.Error("Share", "Could not read the shared content", ex);
            TryDelete(folder);
            try { operation.ReportError("Traysky couldn't read what was shared."); } catch { }
        }
    }

    /// <summary>Wakes the running instance so it collects what <see cref="StageAsync"/> left.</summary>
    public static void SignalRunningInstance()
    {
        try
        {
            // Open-or-create rather than OpenExisting: if the running instance is still starting
            // up and hasn't made the event yet, it inherits this signaled one and fires at once.
            using var received = new EventWaitHandle(false, EventResetMode.AutoReset, ReceivedEventName);
            received.Set();
        }
        catch (Exception ex)
        {
            LogService.Warn("Share", $"Could not signal the running instance: {ex.Message}");
        }
    }

    /// <summary>Every fully staged share, oldest first. The caller deletes each with <see cref="Discard"/> once applied.</summary>
    public static IReadOnlyList<StagedShare> TakePending()
    {
        var shares = new List<StagedShare>();
        try
        {
            if (!Directory.Exists(StagingRoot))
                return shares;

            foreach (string folder in Directory.GetDirectories(StagingRoot).Order(StringComparer.Ordinal))
            {
                if (!File.Exists(Path.Combine(folder, ReadyFileName)))
                {
                    // A share that never finished staging (the process died mid-copy).
                    if (Directory.GetCreationTimeUtc(folder) < DateTime.UtcNow.AddHours(-1))
                        TryDelete(folder);
                    continue;
                }

                string textPath = Path.Combine(folder, TextFileName);
                string text = File.Exists(textPath) ? File.ReadAllText(textPath) : string.Empty;
                string mediaFolder = Path.Combine(folder, MediaFolderName);
                string[] files = Directory.Exists(mediaFolder)
                    ? Directory.GetFiles(mediaFolder).Order(StringComparer.OrdinalIgnoreCase).ToArray()
                    : [];

                shares.Add(new StagedShare(folder, text, files));
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("Share", $"Could not read staged shares: {ex.Message}");
        }

        return shares;
    }

    public static void Discard(StagedShare share) => TryDelete(share.Folder);

    private static async Task SaveBitmapAsPngAsync(RandomAccessStreamReference bitmap, string path)
    {
        // Like a clipboard bitmap, a shared one has no filename or promised encoding.
        using IRandomAccessStreamWithContentType source = await bitmap.OpenReadAsync();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(source);
        using SoftwareBitmap pixels = await decoder.GetSoftwareBitmapAsync();

        using FileStream output = File.Create(path);
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output.AsRandomAccessStream());
        encoder.SetSoftwareBitmap(pixels);
        await encoder.FlushAsync();
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex)
        {
            LogService.Warn("Share", $"Could not delete '{folder}': {ex.Message}");
        }
    }
}
