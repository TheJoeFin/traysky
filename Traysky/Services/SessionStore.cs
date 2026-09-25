using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Models;
using Windows.Storage;

namespace Traysky.Services;

/// <summary>
/// Persists the refresh token between launches, encrypted with DPAPI for the current Windows
/// user. Chosen over Credential Manager (PasswordVault / CredWrite) because it doesn't fit: an
/// OAuth session's JSON is ~2.2 KB of UTF-8, almost all of it the DPoP key, and Credential
/// Manager caps a secret at 2560 bytes. PasswordVault stores the password as UTF-16, which
/// roughly doubles it past the cap, and raw UTF-8 through CredWrite leaves only ~300 bytes of
/// headroom, so a long PDS URL or a change in how idunno serializes the key would sign people
/// out. Credential Manager encrypts with the same per-user DPAPI key anyway, so moving there
/// wouldn't make it any more secure. See issue #9.
/// </summary>
public static class SessionStore
{
    private const string FileName = "session.bin";

    // Extra entropy ties the blob to this app: another app running as the same user still
    // has to know this to decrypt it. Not a secret, just a namespace.
    private static readonly byte[] Entropy = "Traysky.Session.v1"u8.ToArray();

    private static string FilePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, FileName);

    public static bool Exists
    {
        get
        {
            try
            {
                return File.Exists(FilePath);
            }
            catch
            {
                return false;
            }
        }
    }

    public static PersistedSession? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            byte[] cipher = File.ReadAllBytes(FilePath);
            byte[] plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize(plain, SessionJsonContext.Default.PersistedSession);
        }
        catch (Exception ex)
        {
            // A blob from another user profile, a corrupt file, a changed schema: all mean
            // "sign in again", never a crash.
            LogService.Warn("SessionStore", $"Could not load saved session: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // Save and Clear share one file (and one .tmp beside it), so they take turns. The
    // generation moves on every Clear, the moment it is called: a save that was queued before
    // a sign-out sees the change and drops its tokens instead of bringing the account back.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static int _generation;
    private static long _lastSaveSequence;
    private static long _lastWrittenSequence;

    public static async Task Save(PersistedSession session)
    {
        int generation = Volatile.Read(ref _generation);
        long sequence = Interlocked.Increment(ref _lastSaveSequence);

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != Volatile.Read(ref _generation))
            {
                LogService.Info("SessionStore", "Skipped a save that was queued before sign-out");
                return;
            }

            // The lock does not queue in order, so an older token pair can arrive after a newer
            // one has been written. Never step back.
            if (sequence < _lastWrittenSequence)
                return;

            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(session, SessionJsonContext.Default.PersistedSession);
            byte[] cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

            // Not cancellable on purpose: once a refresh has swapped tokens with the server, the
            // old refresh token is spent, and a half-done save would sign the user out next launch.
            string path = FilePath;
            string tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, cipher).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
            _lastWrittenSequence = sequence;

            LogService.Info("SessionStore", $"Saved session for {session.Did}");
        }
        catch (Exception ex)
        {
            LogService.Error("SessionStore", "Could not save session", ex);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Deletes the saved session. Any save that has not reached the file yet is dropped as soon
    /// as this is called, so fire-and-forget callers are safe; await it when the file must be
    /// gone before carrying on.
    /// </summary>
    public static async Task Clear()
    {
        Interlocked.Increment(ref _generation);

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
                LogService.Info("SessionStore", "Cleared saved session");
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("SessionStore", $"Could not clear session: {ex.Message}");
        }
        finally
        {
            Gate.Release();
        }
    }
}
