using CommunityToolkit.Mvvm.ComponentModel;
using idunno.AtProto;
using idunno.AtProto.Authentication;
using idunno.AtProto.Events;
using idunno.Bluesky;
using idunno.Bluesky.Actor;
using Microsoft.UI.Dispatching;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Models;
using AtHandle = idunno.AtProto.Handle;

namespace Traysky.Services;

public enum LoginOutcome
{
    Success,

    /// <summary>The account has email two-factor auth; ask for the code and call login again with it.</summary>
    NeedsAuthFactor,

    Failed,

    /// <summary>The user cancelled, or started another sign-in; nothing to report.</summary>
    Cancelled
}

/// <summary>
/// Owns the one <see cref="BlueskyAgent"/> the app uses and everything about "who is signed
/// in": login, restore-on-launch, logout, and the profile bits the UI shows. Observable
/// properties change on the UI thread; the agent's own events arrive on thread-pool threads
/// and are marshalled through the dispatcher captured at construction.
/// </summary>
public sealed partial class BlueskySessionService : ObservableObject
{
    private static readonly Lazy<BlueskySessionService> _instance = new(() => new BlueskySessionService());

    public static BlueskySessionService Instance => _instance.Value;

    private readonly DispatcherQueue _dispatcher;
    private readonly BlueskyAgent _agent;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    /// <summary>How long a browser sign-in waits for the redirect before giving up.</summary>
    private static readonly TimeSpan BrowserLoginTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Set while a login/restore is in flight so the agent's events do not double-fire UI updates.</summary>
    private int _transitioning;

    /// <summary>The browser sign-in waiting for its redirect, if any. Only one at a time.</summary>
    private PendingBrowserLogin? _pendingBrowserLogin;

    private sealed record PendingBrowserLogin(OAuthClient Client, string? State, TaskCompletionSource<Uri> Callback);

    private BlueskySessionService()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("BlueskySessionService must be created on the UI thread.");

        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        _agent = new BlueskyAgent(new BlueskyAgentOptions
        {
            HttpClientOptions = new HttpClientOptions
            {
                HttpUserAgent = $"Traysky/{version} (+https://github.com/TheJoeFin/Traysky)",
                Timeout = TimeSpan.FromSeconds(30)
            },

            // Needed for browser sign-in, and also to refresh or revoke an OAuth session restored
            // from disk: idunno refuses to refresh DPoP credentials without a client id.
            OAuthOptions = new OAuthOptions
            {
                ClientId = OAuthCallbackPolicy.ClientId,
                ReturnUri = new Uri(OAuthCallbackPolicy.RedirectUri),
                Scopes = OAuthCallbackPolicy.Scopes
            }
        });

        _agent.Authenticated += OnAuthenticated;
        _agent.CredentialsUpdatedAsync = CredentialsUpdatedAsync;
        _agent.TokenRefreshFailed += OnTokenRefreshFailed;
        _agent.Unauthenticated += OnUnauthenticated;
    }

    /// <summary>The shared agent. Callers check <see cref="IsSignedIn"/> first.</summary>
    public BlueskyAgent Agent => _agent;

    [ObservableProperty]
    public partial bool IsSignedIn { get; private set; }

    /// <summary>True from launch until the saved session has been tried, so the UI shows a spinner instead of the login page for a moment.</summary>
    [ObservableProperty]
    public partial bool IsRestoring { get; private set; }

    [ObservableProperty]
    public partial string? Did { get; private set; }

    [ObservableProperty]
    public partial string? Handle { get; private set; }

    [ObservableProperty]
    public partial string? DisplayName { get; private set; }

    [ObservableProperty]
    public partial Uri? AvatarUri { get; private set; }

    /// <summary>Raised on the UI thread after a successful login or restore.</summary>
    public event EventHandler? SignedIn;

    /// <summary>Raised on the UI thread after logout or when the refresh token stopped working.</summary>
    public event EventHandler? SignedOut;

    /// <summary>
    /// Restores the previous session from the encrypted refresh token, if there is one.
    /// Returns false (and clears the store) when the token no longer works.
    /// </summary>
    public async Task<bool> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        if (PreviewMode.IsEnabled)
        {
            await OnUiAsync(() =>
            {
                Handle = PreviewMode.Handle;
                DisplayName = "Preview Account";
                IsSignedIn = true;
                SignedIn?.Invoke(this, EventArgs.Empty);
            }).ConfigureAwait(false);
            return true;
        }

        PersistedSession? saved = SessionStore.Load();
        if (saved is null)
            return false;

        IsRestoring = true;
        Interlocked.Exchange(ref _transitioning, 1);

        try
        {
            if (!Enum.TryParse(saved.AuthenticationType, out AuthenticationType authType))
                authType = AuthenticationType.UsernamePassword;

            AtProtoCredential credential = AtProtoCredential.Create(
                service: new Uri(saved.Service),
                authenticationType: authType,
                refreshToken: saved.RefreshToken,
                dPoPProofKey: saved.DPoPProofKey,
                dPoPNonce: saved.DPoPNonce);

            bool ok = await _agent.RefreshCredentials(credential, cancellationToken).ConfigureAwait(false);

            if (!ok)
            {
                LogService.Warn("Session", "Saved session could not be refreshed; clearing it");
                await SessionStore.Clear().ConfigureAwait(false);
                await OnUiAsync(() => ApplySignedOut()).ConfigureAwait(false);
                return false;
            }

            // The handle we saved is good enough to render the tray tooltip immediately; the
            // profile fetch fills in the rest.
            await OnUiAsync(() => ApplySignedIn(saved.Handle)).ConfigureAwait(false);
            await LoadProfileAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("Session", "Restore failed", ex);
            await OnUiAsync(() => ApplySignedOut()).ConfigureAwait(false);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _transitioning, 0);
            await OnUiAsync(() => IsRestoring = false).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Signs in with a handle (or DID) and an app password. <paramref name="authFactorToken"/>
    /// is the emailed code, only needed after a <see cref="LoginOutcome.NeedsAuthFactor"/>.
    /// </summary>
    public async Task<(LoginOutcome Outcome, string? Error)> LoginAsync(
        string handle,
        string appPassword,
        string? authFactorToken = null,
        CancellationToken cancellationToken = default)
    {
        string identifier = BlueskyLinks.NormalizeHandle(handle);
        if (identifier.Length == 0 || string.IsNullOrEmpty(appPassword))
            return (LoginOutcome.Failed, "Enter your handle and an app password.");

        Interlocked.Exchange(ref _transitioning, 1);
        try
        {
            AtProtoHttpResult<bool> result = await _agent.Login(
                identifier,
                appPassword,
                authFactorToken: string.IsNullOrWhiteSpace(authFactorToken) ? null : authFactorToken.Trim(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result.Succeeded)
            {
                LogService.Info("Session", $"Signed in as {identifier}");
                await OnUiAsync(() => ApplySignedIn(identifier)).ConfigureAwait(false);
                await LoadProfileAsync(cancellationToken).ConfigureAwait(false);
                return (LoginOutcome.Success, null);
            }

            string? error = result.AtErrorDetail?.Error;
            string? message = result.AtErrorDetail?.Message;

            if (string.Equals(error, "AuthFactorTokenRequired", StringComparison.OrdinalIgnoreCase))
                return (LoginOutcome.NeedsAuthFactor, null);

            LogService.Warn("Session", $"Login failed: {(int)result.StatusCode} {error} {message}");

            string friendly = result.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "That handle and app password didn't match.",
                System.Net.HttpStatusCode.TooManyRequests => "Too many attempts. Wait a few minutes and try again.",
                0 => "Couldn't reach Bluesky. Check your connection.",
                _ => string.IsNullOrEmpty(message) ? $"Sign in failed ({(int)result.StatusCode})." : message
            };
            return (LoginOutcome.Failed, friendly);
        }
        catch (Exception ex)
        {
            LogService.Error("Session", "Login threw", ex);
            return (LoginOutcome.Failed, "Couldn't reach Bluesky. Check your connection.");
        }
        finally
        {
            Interlocked.Exchange(ref _transitioning, 0);
        }
    }

    /// <summary>
    /// Signs in through the browser with atproto OAuth: opens the account's authorization
    /// server, then waits for <see cref="TryCompleteBrowserLogin"/> to hand over the redirect.
    /// Starting another browser sign-in, cancelling <paramref name="cancellationToken"/> or
    /// the timeout ends the wait with <see cref="LoginOutcome.Cancelled"/>.
    /// </summary>
    public async Task<(LoginOutcome Outcome, string? Error)> LoginWithBrowserAsync(
        string handle,
        CancellationToken cancellationToken = default)
    {
        string identifier = BlueskyLinks.NormalizeHandle(handle);
        if (!AtHandle.TryParse(identifier, out AtHandle? atHandle) || atHandle is null)
            return (LoginOutcome.Failed, "Enter your handle, like alice.bsky.social.");

        OAuthClient client = _agent.CreateOAuthClient();
        Uri startUri;
        try
        {
            // Resolves the handle to its PDS and authorization server, then pushes the
            // authorization request (PAR); the returned URI only carries client_id and request_uri.
            startUri = await _agent.BuildOAuth2LoginUri(client, atHandle, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (LoginOutcome.Cancelled, null);
        }
        catch (Exception ex)
        {
            LogService.Warn("Session", $"Browser sign-in could not start: {ex.GetType().Name}: {ex.Message}");
            return (LoginOutcome.Failed, "Couldn't reach that account's Bluesky server. Check the handle and your connection.");
        }

        PendingBrowserLogin pending = new(client, client.State?.State, new(TaskCreationOptions.RunContinuationsAsynchronously));
        Interlocked.Exchange(ref _pendingBrowserLogin, pending)?.Callback.TrySetCanceled();

        Uri callback;
        try
        {
            OAuthClient.OpenBrowser(startUri);
            LogService.Info("Session", "Waiting for browser sign-in");

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(BrowserLoginTimeout);
            callback = await pending.Callback.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (pending.Callback.Task.IsCanceled || cancellationToken.IsCancellationRequested)
                return (LoginOutcome.Cancelled, null);

            LogService.Warn("Session", "Browser sign-in timed out");
            return (LoginOutcome.Failed, "Sign in timed out. Try again.");
        }
        catch (Exception ex)
        {
            LogService.Error("Session", "Could not open the browser for sign-in", ex);
            return (LoginOutcome.Failed, "Couldn't open your browser.");
        }
        finally
        {
            Interlocked.CompareExchange(ref _pendingBrowserLogin, null, pending);
        }

        if (OAuthCallbackPolicy.ErrorMessage(callback) is string denied)
        {
            LogService.Warn("Session", $"Browser sign-in returned error {OAuthCallbackPolicy.GetQueryValue(callback, "error")}");
            return (LoginOutcome.Failed, denied);
        }

        Interlocked.Exchange(ref _transitioning, 1);
        try
        {
            // Exchanges the code for DPoP-bound tokens and logs the agent in; the Authenticated
            // event persists them (including the proof key) like any other session.
            if (!await _agent.ProcessOAuth2LoginResponse(pending.Client, callback.ToString(), cancellationToken).ConfigureAwait(false))
            {
                LogService.Warn("Session", "Browser sign-in response was rejected");
                return (LoginOutcome.Failed, "Bluesky didn't accept the sign-in. Try again.");
            }

            LogService.Info("Session", $"Signed in as {identifier} (OAuth)");
            await OnUiAsync(() => ApplySignedIn(identifier)).ConfigureAwait(false);
            await LoadProfileAsync(cancellationToken).ConfigureAwait(false);
            return (LoginOutcome.Success, null);
        }
        catch (Exception ex)
        {
            LogService.Error("Session", "Browser sign-in failed", ex);
            return (LoginOutcome.Failed, "Couldn't finish signing in. Try again.");
        }
        finally
        {
            Interlocked.Exchange(ref _transitioning, 0);
        }
    }

    /// <summary>
    /// Hands an OAuth redirect (from protocol activation) to the browser sign-in waiting for
    /// it. Returns false when <paramref name="uri"/> is not an OAuth callback at all. The URI
    /// carries a one-time authorization code, so it is never logged.
    /// </summary>
    public bool TryCompleteBrowserLogin(Uri uri)
    {
        if (!OAuthCallbackPolicy.IsCallback(uri))
            return false;

        PendingBrowserLogin? pending = Volatile.Read(ref _pendingBrowserLogin);
        if (pending is null)
        {
            LogService.Warn("Session", "OAuth redirect arrived with no sign-in in progress; ignoring it");
            return true;
        }

        // A stale tab from an earlier attempt must not complete the current one.
        if (pending.State is not null
            && !string.Equals(OAuthCallbackPolicy.GetQueryValue(uri, "state"), pending.State, StringComparison.Ordinal))
        {
            LogService.Warn("Session", "OAuth redirect did not match the sign-in in progress; ignoring it");
            return true;
        }

        pending.Callback.TrySetResult(uri);
        return true;
    }

    public async Task LogoutAsync()
    {
        Interlocked.Exchange(ref _transitioning, 1);
        try
        {
            await SessionStore.Clear().ConfigureAwait(false);
            if (_agent.IsAuthenticated)
                await _agent.Logout().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Warn("Session", $"Logout error (continuing): {ex.Message}");
        }
        finally
        {
            // Again, in case a background refresh saved new tokens while the agent was still
            // signed in, between the first clear and the logout finishing.
            await SessionStore.Clear().ConfigureAwait(false);
            Interlocked.Exchange(ref _transitioning, 0);
        }

        await OnUiAsync(ApplySignedOut).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes sure the agent holds an unexpired access token, refreshing it if not. The agent
    /// treats an expired token as "not authenticated" and throws <see cref="AuthenticationRequiredException"/>
    /// from every call, and its own background refresh timer does not recover when a refresh
    /// throws (e.g. DNS is not back yet right after the machine wakes). Returns false when
    /// the token could not be refreshed; a rejected refresh token signs out via
    /// <see cref="OnTokenRefreshFailed"/>.
    /// </summary>
    public async Task<bool> EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (PreviewMode.IsEnabled || _agent.IsAuthenticated)
            return true;

        if (!IsSignedIn || string.IsNullOrEmpty(_agent.Credentials?.RefreshToken))
            return false;

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have refreshed while this one waited.
            if (_agent.IsAuthenticated)
                return true;

            LogService.Info("Session", "Access token expired; refreshing");
            bool ok = await _agent.RefreshCredentials(cancellationToken).ConfigureAwait(false);
            if (!ok)
                LogService.Warn("Session", "Access token refresh was rejected");
            return ok;
        }
        catch (Exception ex)
        {
            LogService.Warn("Session", $"Access token refresh threw: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task LoadProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_agent.Did is null)
                return;

            AtProtoHttpResult<ProfileViewDetailed> profile = await _agent.GetProfile(_agent.Did, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!profile.Succeeded || profile.Result is null)
                return;

            ProfileViewDetailed p = profile.Result;
            await OnUiAsync(() =>
            {
                Handle = p.Handle?.ToString();
                DisplayName = string.IsNullOrWhiteSpace(p.DisplayName) ? p.Handle?.ToString() : p.DisplayName;
                AvatarUri = p.Avatar;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Warn("Session", $"Profile load failed: {ex.Message}");
        }
    }

    private void ApplySignedIn(string? handle)
    {
        Did = _agent.Did?.ToString();
        Handle = handle;
        DisplayName ??= handle;
        IsSignedIn = true;
        SignedIn?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySignedOut()
    {
        bool was = IsSignedIn;
        IsSignedIn = false;
        Did = null;
        Handle = null;
        DisplayName = null;
        AvatarUri = null;
        if (was)
            SignedOut?.Invoke(this, EventArgs.Empty);
    }

    // ---- Agent events (thread-pool threads) --------------------------------------------

    private async void OnAuthenticated(object? sender, AuthenticatedEventArgs e)
    {
        // async void: anything that escapes here takes the app down.
        try
        {
            await Persist(e.AccessCredentials).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("Session", "Could not persist session after sign-in", ex);
        }
    }

    // The token is ignored on purpose; see SessionStore.Save.
    private Task CredentialsUpdatedAsync(CredentialsUpdatedEventArgs e, CancellationToken cancellationToken) => Persist(e.AccessCredentials);

    private void OnTokenRefreshFailed(object? sender, TokenRefreshFailedEventArgs e)
    {
        LogService.Warn("Session", $"Token refresh failed: {e.StatusCode} {e.Error?.Error} {e.Error?.Message}");

        // A refresh can fail because the network blinked, in which case the agent keeps trying
        // and the access token may still be valid for a while. Only treat an explicit rejection
        // as "signed out".
        if (e.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.BadRequest)
        {
            _ = SessionStore.Clear();
            if (Volatile.Read(ref _transitioning) == 0)
                _dispatcher.TryEnqueue(ApplySignedOut);
        }
    }

    private void OnUnauthenticated(object? sender, UnauthenticatedEventArgs e)
    {
        _ = SessionStore.Clear();
        if (Volatile.Read(ref _transitioning) == 0)
            _dispatcher.TryEnqueue(ApplySignedOut);
    }

    private async Task Persist(AccessCredentials? credentials)
    {
        if (credentials is null || string.IsNullOrEmpty(credentials.RefreshToken))
            return;

        string? proofKey = null;
        string? nonce = null;
        if (credentials is DPoPAccessCredentials dpop)
        {
            proofKey = dpop.DPoPProofKey;
            nonce = dpop.DPoPNonce;
        }

        await SessionStore.Save(new PersistedSession
        {
            Service = credentials.Service.ToString(),
            Did = credentials.Did?.ToString() ?? _agent.Did?.ToString() ?? string.Empty,
            Handle = Handle,
            AuthenticationType = credentials.AuthenticationType.ToString(),
            RefreshToken = credentials.RefreshToken,
            DPoPProofKey = proofKey,
            DPoPNonce = nonce,
            SavedAtUtc = DateTimeOffset.UtcNow
        }).ConfigureAwait(false);
    }

    private Task OnUiAsync(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
        {
            tcs.SetResult(); // dispatcher shutting down; nothing to update
        }
        return tcs.Task;
    }
}
