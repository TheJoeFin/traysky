using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Traysky.Models;
using Windows.Storage;

namespace Traysky.Services;

/// <summary>
/// Persists the refresh token between launches, encrypted with DPAPI for the current Windows
/// user. Chosen over the Credential Locker because an OAuth session later needs a DPoP key
/// alongside the token, and a single encrypted blob grows with that more gracefully than
/// PasswordVault's fixed username/password slots.
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

    public static void Save(PersistedSession session)
    {
        try
        {
            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(session, SessionJsonContext.Default.PersistedSession);
            byte[] cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

            string path = FilePath;
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, cipher);
            File.Move(tmp, path, overwrite: true);

            LogService.Info("SessionStore", $"Saved session for {session.Did}");
        }
        catch (Exception ex)
        {
            LogService.Error("SessionStore", "Could not save session", ex);
        }
    }

    public static void Clear()
    {
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
    }
}
