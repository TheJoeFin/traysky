# Traysky — Bluesky in the Windows tray

A tiny, elegant Bluesky client that lives in the system tray. Click the butterfly, get a Mica
flyout with your timeline, your notifications, and a compose box. The icon tells you when you
have unread notifications. That's the whole pitch: **the fastest way to post and the calmest
way to keep up**, without a browser tab or a full app window.

Sibling of [Traydio](https://github.com/TheJoeFin/Traydio) (`D:\source\Trdo`) — same stack,
same tray plumbing, same principles (simple, native, uncluttered).

---

## 0. Status (2026-09-19)

**Done and verified running** (Debug, x64, MSIX loose-layout registered):

- M0 scaffold: solution, csproj, manifest (startup task + toast COM server), CI, tests, ported tray
  icon / popup / placement / click policy / settings / log services.
- M1 sign-in: login page with app password + email 2FA path, DPAPI session store, restore on
  launch, sign out. *Not yet exercised against a live account* — that needs your credentials.
- M2 notifications: poll service with backoff + rate-limit handling, tray badge icons + tooltip,
  Notifications tab with grouping and batch hydration, mark-seen after 1.5 s dwell, toasts via
  AppNotifications with click-through (only rises after the first poll are announced).
- M3 timeline + compose: Home tab with rich text, images, link cards, quotes, video thumbnails,
  like/repost with optimistic undo, cursor paging, compose/reply/quote with grapheme counter.
- Settings: account, poll interval, toast toggles, tray click actions, start with Windows,
  diagnostics. 39 unit tests pass. Release publish with trimming succeeds (idunno assemblies
  rooted, see csproj comment).
- Debug preview mode (`preview.flag` in LocalState) renders sample data without an account.

**Not done**: M4 (thread + profile pages, mute), M5 (global hotkey, localization, tutorial,
Store assets are placeholders), M6.

**2026-09-20: compose supports image and video attachments.** `ComposeBox` has attach buttons
(image/video, mutually exclusive per Bluesky's embed rules — up to 4 images, or exactly one
video) that open a `FileOpenPicker` initialized against `PInvoke.GetForegroundWindow()` (the
popup is always the foreground window when this is reachable, so no need to thread a `Window`
reference down from `App`). Picked files are read fully into memory as `ComposeAttachmentItem`
(bytes + mime type + pixel dimensions for images, no thumbnail decode for video) rather than
kept as `StorageFile` references, so re-reading never depends on sandbox file-access lifetime.
`ComposeAttachmentPolicy` (pure .NET, linked into `Traysky.Tests`) owns the extension→mime maps,
the image-count/video-exclusivity rule, and size limits. On post, `ComposeViewModel.PostAsync`
uploads images via `agent.UploadBlob` directly; video goes through `agent.UploadVideo` +
polling `agent.GetJobStatus` until `JobState.Completed` (Bluesky's video pipeline transcodes
server-side, unlike the synchronous image blob endpoint) before building an `EmbeddedVideo`.
Either embed is attached via `idunno.Bluesky.PostBuilder` (`Add(images)` / `Add(video)`,
`.ReplyTo(strongRef, agent)` for replies, `.Add(strongRef)` for quotes, then
`.ExtractFacets(agent.FacetExtractor)` since `PostBuilder` doesn't auto-extract like the
convenience `agent.Post(text, …)` overloads do) rather than the plain-image `Post`/`ReplyTo`/
`Quote` overloads, so one code path covers new/reply/quote × image/video instead of four.

---

## 1. Stack

| Concern | Choice | Why |
|---|---|---|
| Runtime / UI | .NET 10, WinUI 3, Windows App SDK 2.5.1, WinUIEx 2.9.3 | Identical to Traydio; the tray code ports 1:1 |
| Packaging | MSIX (single-project), x64 + ARM64, Store + GitHub sideload bundle | Reuse `MakeAppxBundle.ps1`, publish profiles, `build.yml` |
| Bluesky | `idunno.Bluesky` 6.0.0 | net10 target, trim/AOT-safe, result-based errors, token-refresh events, OAuth ready |
| MVVM | CommunityToolkit.Mvvm 8.4.x | Same as Traydio |
| P/Invoke | Microsoft.Windows.CsWin32 | Needed by the ported `WindowPlacementService` / popup window |
| Tests | MSTest, linked-file pattern from `Trdo.Tests.csproj` | Keeps policies testable without loading WinUI |
| Secrets | DPAPI (`ProtectedData`, CurrentUser scope) → file in `ApplicationData.LocalFolder` | Refresh token + (later) DPoP key are bigger than PasswordVault is comfortable with |
| Toasts | `Microsoft.Windows.AppNotifications` (Windows App SDK) | Real Action Center toasts that respect Focus Assist |

Trimming: Traydio publishes `PublishTrimmed=true` in Release. idunno.Bluesky documents trim +
AOT support, so keep trimming on, but check the first Release build's trim warnings and add a
`TrimmerRootAssembly` only if something actually breaks.

---

## 2. What to lift from Traydio verbatim (MIT, same author)

Copy these first; they are the expensive-to-rediscover parts of "a great tray app":

- `Controls/TrayPopupWindow.xaml(.cs)` — borderless, Mica, `IsShownInSwitchers=false`, rounded
  corners via DWM, `WS_EX_LAYERED` alpha fade + slide, light-dismiss with the
  `Xaml_WindowedPopupClass` / `#32768` carve-out so ComboBoxes and MenuFlyouts don't dismiss it,
  Escape accelerator, "same click that dismissed us" 300 ms guard. Rename `ShellHost` content.
- `Services/WindowPlacementService.cs` — the whole file. Tray-icon rect via
  `Shell_NotifyIconGetRect`, taskbar edge via `SHAppBarMessage`, per-monitor DPI, RTL, clamping.
- `App.xaml.cs` skeleton — `OnLaunched` single-instance mutex + restore event, `InitializeTrayIcon`,
  `TrayClickSequencer`, `UpdateTrayIconAsync` (theme via `SystemUsesLightTheme` registry +
  `UISettings.ColorValuesChanged`), `EnsureTrayIconVisibleAsync` (explorer-restart watchdog),
  `ShutdownAndExit` as the only exit path. Strip everything radio-specific.
- `Services/TrayClickPolicy.cs` + `Models/TrayClickAction.cs` + their tests.
- `Services/SettingsService.cs` pattern (LocalSettings + typed accessors + change events),
  `LogService`, `LocalizationService`, `NavigationService`, `Pages/ShellPage` (TitleBar + Frame).
- `Converters/*` you need (Bool→Visibility, Null→Visibility, Count→EmptyState).
- `Package.appxmanifest` (startup task with a `TraykyStartup` TaskId, `internetClient`,
  `runFullTrust`), `app.manifest`, `NativeMethods.txt`, `.editorconfig`, `.gitignore`,
  `Properties/PublishProfiles/*`, `MakeAppxBundle.ps1`, `.github/workflows/build.yml`,
  `CLAUDE.md` (the "don't run dotnet build/restore unprompted" rule).

Do **not** lift: anything under `Services/Playback`, `Services/Audio`, `Services/Metadata`,
the mini player, song-change popup (see §6 for what replaces it), station models.

---

## 3. Project layout

```
Traysky.slnx
Traysky/
  App.xaml(.cs)                 tray icon, popup window, lifecycle, unread polling wiring
  Package.appxmanifest
  Assets/
    Butterfly.ico               playing-state equivalent: signed in, nothing unread
    Butterfly-Black.ico / -White.ico   theme-aware idle variants (light / dark taskbar)
    Butterfly-Unread.ico        blue butterfly + red dot (both themes read fine)
    Butterfly-Offline.ico       greyed / signed-out
  Controls/
    TrayPopupWindow.xaml(.cs)   ported
    PostCard.xaml(.cs)          one post: avatar, name/handle/time, rich text, embed, actions,
                                 its own inline reply box
    NotificationRow.xaml(.cs)   one notification: grouped avatars, reason line, subject snippet
    ComposeBox.xaml(.cs)        text box, grapheme counter, post button, reply/quote context strip
    EmbedPresenter.xaml(.cs)    ContentControl + TemplateSelector for images / external / quote / video
  Pages/
    ShellPage.xaml(.cs)         TitleBar (🖊 🔔 ⟳ ⚙ ✕) + feed tab row (pinned feeds) + Frame
    LoginPage.xaml(.cs)         handle, app password, optional email 2FA code
    TimelinePage.xaml(.cs)
    ComposePage.xaml(.cs)       a fresh post, a quote, or a reply with no card to pop out under -
                                 reached from the titlebar's New post button or a Quote action
    NotificationsPage.xaml(.cs)
    ThreadPage.xaml(.cs)        (M4) parent chain + replies for one post
    ProfilePage.xaml(.cs)       (M4) minimal: banner, bio, follow state, recent posts
    SettingsPage.xaml(.cs)
    AboutPage.xaml(.cs)
  ViewModels/
    ShellViewModel.cs, LoginViewModel.cs, TimelineViewModel.cs, NotificationsViewModel.cs,
    ComposeViewModel.cs, SettingsViewModel.cs
    Items/PostItem.cs           UI-shaped projection of FeedViewPost (observable like/repost state)
    Items/NotificationItem.cs
  Models/
    PersistedSession.cs         service Uri, DID, handle, refresh token, auth type, (DPoP later)
    TrayClickAction.cs          ported
  Services/
    BlueskySessionService.cs    owns the single BlueskyAgent; login/restore/logout; events
    SessionStore.cs             DPAPI-encrypted read/write of PersistedSession
    NotificationPollService.cs  DispatcherQueueTimer → GetNotificationUnreadCount; raises UnreadChanged
    TimelineService.cs          thin: GetTimeline(cursor), like/unlike, repost/unrepost, post/reply
    ToastService.cs             AppNotificationManager wrapper; deep-link args → open popup on tab
    RichTextBuilder.cs          facets → RichTextBlock inlines (WinUI side)
    FacetSegmenter.cs           PURE .NET: UTF-8 byte-slice facets → string-index segments  ← tested
    RelativeTimeFormatter.cs    PURE .NET: "2m", "3h", "Yesterday"                            ← tested
    PostTextPolicy.cs           PURE .NET: 300-grapheme limit, over-limit slicing              ← tested
    UnreadBadgePolicy.cs        PURE .NET: which icon for (signedIn, unread, theme, offline)   ← tested
    NotificationGroupingPolicy.cs PURE .NET: collapse "A, B and 3 others liked your post"      ← tested
    SettingsService.cs, LogService.cs, LocalizationService.cs, NavigationService.cs, WindowPlacementService.cs, TrayClickPolicy.cs
  Strings/en-US/Resources.resw
Traysky.Tests/
  Traysky.Tests.csproj          links the PURE .NET files above, exactly like Trdo.Tests.csproj
```

Rule of thumb carried over from Traydio: anything with a branch worth testing goes in a
`*Policy` / pure class with no WinRT dependency, and gets linked into the test project.

---

## 4. Bluesky integration (idunno.Bluesky 6.0)

### 4.1 Agent setup — one agent for the app's lifetime

```csharp
_agent = new BlueskyAgent(new BlueskyAgentOptions
{
    HttpClientOptions = new HttpClientOptions
    {
        HttpUserAgent = $"Traysky/{version} (+https://github.com/TheJoeFin/Traysky)",
        Timeout = TimeSpan.FromSeconds(30),
    },
    // EnableBackgroundTokenRefresh stays true (default) — the agent refreshes for us.
});
_agent.Authenticated      += (_, e) => SessionStore.Save(FromCredentials(_agent.Credentials));
_agent.CredentialsUpdated += (_, e) => SessionStore.Save(FromCredentials(_agent.Credentials));
_agent.TokenRefreshFailed += (_, e) => { SessionStore.Clear(); SignedOut?.Invoke(...); };
_agent.Unauthenticated    += (_, e) => { SessionStore.Clear(); SignedOut?.Invoke(...); };
```

### 4.2 Login (M1) — handle + app password

- Strip a leading `@` from the handle (docs are explicit: handles never start with `@`).
- Link to <https://bsky.app/settings/app-passwords> right in the login page. Never accept the
  real password knowingly — label the field "App password" and explain why.
- Email 2FA: if `!result.Succeeded && result.AtErrorDetail?.Error == "AuthFactorTokenRequired"`,
  reveal a code field and call `Login(handle, password, code)`. (App passwords skip MFA, so
  this path is rare, but the docs call it out.)
- On success the `Authenticated` event persists the session; the password is dropped immediately.

### 4.3 Session restore on launch

```csharp
var s = SessionStore.Load();
if (s is not null)
{
    var cred = AtProtoCredential.Create(service: s.Service, authenticationType: s.AuthenticationType,
                                        refreshToken: s.RefreshToken, dPoPProofKey: s.DPoPProofKey, dPoPNonce: s.DPoPNonce);
    var ok = await _agent.RefreshCredentials(cred);
    if (!ok.Succeeded) { SessionStore.Clear(); /* show login on next popup open */ }
}
```

`SessionStore` = `JsonSerializer` (source-generated context — the app is trimmed) →
`ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)` → `session.bin` in
`ApplicationData.Current.LocalFolder`. Delete file on logout / refresh failure.

### 4.4 Notifications (M2)

- Poll `GetNotificationUnreadCount()` on a `DispatcherQueueTimer` (default 60 s, settings:
  30 s / 1 m / 2 m / 5 m). Skip a tick if the previous call is still in flight or the network
  service says offline. Back off (×2 up to 10 min) on failures, reset on success.
- `UnreadBadgePolicy` decides the icon; `App.UpdateTrayIconAsync` applies it, tooltip becomes
  "Traysky — 3 unread".
- When the count rises and the popup isn't visible, fetch `ListNotifications(limit: count)` and
  raise one toast per *mention/reply/quote*, and one grouped toast for likes/reposts/follows
  ("5 new likes, 2 new followers"). Toggle per reason in Settings.
- Notifications page: `ListNotifications(limit: 30, cursor)` with incremental load on scroll.
  Group consecutive same-subject likes/reposts (`NotificationGroupingPolicy`). Hydrate subject
  posts with `GetPosts(uris)` in one batch rather than `GetPostView` per row.
- Mark-as-seen: capture `DateTimeOffset.UtcNow` *before* the list call, show the page, then
  `UpdateNotificationSeenAt(seenAt)` when the user has had the tab open ≥ 1.5 s (so a
  stray click doesn't clear the badge). Badge goes to zero locally at the same time.

### 4.5 Timeline (M3)

- `GetTimeline(limit: 30)` on popup open if the cache is older than 2 min or empty; pull-to-refresh
  button in the header; `cursor` paging when the `ListView` nears the end
  (`ItemsStackPanel` + scroll-viewer threshold or `IncrementalLoadingCollection`).
- Project `FeedViewPost` → `PostItem` once: author, avatar Uri, display text, facets, embed,
  counts, `Viewer.Like` / `Viewer.Repost` AT-URIs (needed to *undo*), `Reason is ReasonRepost`
  ("♲ Reposted by X"), `Reply` (parent author for "Replying to @y").
- Actions: `Like(strongRef)` / `DeleteLike(likeUri)`, `Repost` / `DeleteRepost`, reply → compose
  with `ReplyTo`, quote → compose with quote embed. Optimistic UI, revert on `!Succeeded`.
- Open in browser: `https://bsky.app/profile/{handle}/post/{rkey}` (rkey = last segment of the AT URI).

### 4.6 Rich text — the one real gotcha

Facets index **UTF-8 byte offsets** (`ByteStart`/`ByteEnd`), not `string` indices. `FacetSegmenter`
converts them once (encode text to UTF-8, map byte offsets → char offsets, handle surrogate pairs)
and yields `[Plain | Link(uri) | Mention(did) | Tag(tag)]` segments. `RichTextBuilder` turns those
into `Run` / `Hyperlink` inlines. Tests cover emoji before a link, adjacent facets, and facets that
overrun the text (real-world data does this).

### 4.7 Compose (M3/M4)

- `PostTextPolicy`: 300 graphemes (`StringInfo.GetTextElementEnumerator`), counter turns red
  at 280, disable Post over 300. The library auto-detects links/mentions/hashtags → facets.
- `agent.Post(text)`, `agent.ReplyTo(strongRef, text)`, quote via the `Post` overload that takes
  an embed / the quote helper. Language from `CultureInfo.CurrentUICulture` (`language:`).
- Images and video: done — see the 2026-09-20 note in §0. `UploadBlob` for images,
  `UploadVideo` + `GetJobStatus` polling for video, alt text on both. Paste-from-clipboard would
  be the tray-native gesture to add later; picking is via `FileOpenPicker` for now.
- Draft survives popup light-dismiss (keep the VM alive, like Traydio keeps `ShellPage` alive).

### 4.8 Rate limits

`AtProtoHttpResult<T>` exposes rate-limit info; log it and if `RateLimited` comes back, freeze
polling until the reset time. Realistic load (1 unread-count call/min + a timeline fetch per open)
is far under Bluesky's limits.

### 4.9 OAuth (M6, optional)

Bluesky OAuth needs a `client_id` that *is* a URL to a hosted `client-metadata.json` (GitHub Pages
on the repo works) and a redirect. For a desktop app the loopback redirect via
`idunno.AtProto.OAuthCallback`'s `CallbackServer` is the documented path; `SessionStore` already
has fields for `DPoPProofKey` / `DPoPNonce` so switching is additive. Ship with app passwords
first — it's what the library docs lead with and it works offline-of-any-hosting.

---

## 5. UX spec for the flyout (320 × 560, Traydio's popup size class)

```
┌──────────────────────────────────────┐
│ 🦋 Traysky      🖊  🔔● ⟳   ⚙   ✕   │  TitleBar; 🖊 = New post, bell = Notifications, badge when unread
│ [ Following ] [ Discover ] [ News ]  │  Segmented (CommunityToolkit) — the account's own
│                                       │  pinned feeds, order/visibility from bsky.app prefs
│ ─────────────────────────────────── │
│ ◯ Jane Doe @jane · 2m                │  PostCard
│   Text with #tags and links…         │
│   [image thumb] [image thumb]        │  EmbedPresenter
│   💬 3   ♲ 1   ♡ 12        ⋯        │  actions; ⋯ = copy link, open in bsky.app, mute
│ ─────────────────────────────────── │
│ ◯ ♲ Reposted by Sam                  │
│ …                                    │
└──────────────────────────────────────┘
```

- Left-click tray → toggle flyout (Traydio's `ToggleNearAnchor` semantics, including the
  "same click dismissed us" rule). Right-click → MenuFlyout: Compose, Home, Notifications,
  Refresh, Settings, Quit. Middle/double-click configurable via the ported `TrayClickPolicy`.
- Toast click → activates existing instance (single-instance mutex path), opens flyout on the
  relevant tab (pass the tab in the toast's launch args; handle in `AppNotificationManager.NotificationInvoked`).
- Keyboard: `Esc` closes; `Ctrl+Enter` posts; `Ctrl+R` refresh; `Ctrl+1/2` tabs. Global hotkey
  (`Ctrl+Shift+B` → open flyout focused on compose) via `RegisterHotKey` in M5.
- Empty/error states: signed-out (login page replaces the tabs), offline banner (port
  `NetworkStatusService`), rate-limited banner with countdown.
- Avatars: `ImageSourceFactory` idea from Traydio — a small in-memory + disk cache keyed by URL
  so scrolling doesn't re-download.

---

## 6. Milestones

Each milestone ends in a runnable, sideloadable build.

**M0 — Scaffold (½ day)**
Solution, csproj (copy Traydio's, swap packages: drop LibVLC/NAudio/TagLib, add idunno.Bluesky),
manifest, icons, ported tray icon + popup + placement + click policy + settings + shell + CI + tests
project. Flyout opens with a placeholder page. Startup task works.

**M1 — Sign in (½ day)**
`BlueskySessionService`, `SessionStore`, `LoginPage`, 2FA path, restore on launch, logout in
Settings, offline/signed-out icon states.

**M2 — Notifications + badge (1 day)**
Poll service, `UnreadBadgePolicy`, tray icon + tooltip, `NotificationsPage`, grouping, seen-at
logic, toasts with deep-link activation. *This is the first version worth living with.*

**M3 — Timeline + compose (1–2 days)**
`TimelinePage`, `PostCard`, `FacetSegmenter` + `RichTextBuilder`, `EmbedPresenter` (images,
external card, quote; video = thumbnail that plays inline via `VideoViewerService`'s overlay,
using the embed's HLS playlist URI), like/repost with undo, paging, `ComposeBox` for new posts
and replies.

**M4 — Depth (1 day)**
`ThreadPage` (`GetPostThread`), `ProfilePage` (`GetProfile`, follow/unfollow), quote posting,
"⋯" menu (copy link, open in bsky.app, mute), settings for poll interval / toast reasons / click
actions.

**M5 — Polish (1 day)**
Image attach + alt text, global hotkey, avatar cache, localization scaffolding, About page,
tutorial-on-first-run (Traydio's `TutorialWindow` pattern), Store assets, Release trim check,
`MakeAppxBundle.ps1` GitHub release.

**M6 — Later / maybe**
OAuth, multiple accounts (`SessionStore` keyed by DID), custom feeds picker (`GetFeed` with
saved feed URIs from `GetPreferences`), Jetstream-driven live badge instead of polling, DMs
(`transition:chat.bsky` scope — OAuth only).

---

## 7. Test plan (Traysky.Tests, linked files)

| Test file | Covers |
|---|---|
| `FacetSegmenterTests` | ASCII, multibyte before facet, surrogate pair inside facet, overlapping/overrun facets, no facets |
| `PostTextPolicyTests` | 0 / 280 / 300 / 301 graphemes, emoji ZWJ sequences count as one |
| `RelativeTimeFormatterTests` | seconds, minutes, hours, yesterday, older → date |
| `UnreadBadgePolicyTests` | matrix of signedIn × unread × theme × offline → icon |
| `NotificationGroupingPolicyTests` | consecutive likes on same post collapse; mixed reasons don't |
| `TrayClickPolicyTests` | ported as-is |
| `SessionSerializationTests` | round-trip `PersistedSession` through the source-generated context |

---

## 8. Open decisions (defaults chosen; change if you disagree)

1. **App password first, OAuth later** — simplest, no hosting dependency, documented path.
2. **Poll, don't stream** — Jetstream is a firehose of *everyone's* commits; filtering it for
   one user's notifications is more work than a 60 s unread-count poll and gains little.
3. **Toasts via AppNotifications, not a custom popup** — Traydio's song-change popup is lovely,
   but for social notifications Action Center + Focus Assist behaviour matters more.
4. **Pre-baked badge icons** rather than rendering the count into the icon — a dot is readable at
   16 px, a number isn't. Revisit only if users ask.
5. **Name**: Traysky (`40087JoeFinApps.Traysky`, startup TaskId `TrayskyStartup`).

---

## 9. References

- idunno.Bluesky docs: <https://bluesky.idunno.dev/> — connecting, savingAndRestoringAuthentication,
  timeline, notifications, posting, requestsAndResponses, cursorsAndPagination, sourceGeneration
- Samples: <https://github.com/blowdart/idunno.Bluesky/tree/main/samples> (Timeline, Feed,
  Notifications, OAuth)
- Bluesky OAuth for clients: <https://docs.bsky.app/docs/advanced-guides/oauth-client>
- App passwords: <https://bsky.app/settings/app-passwords>
- Traydio: <https://github.com/TheJoeFin/Traydio> (`D:\source\Trdo`)
