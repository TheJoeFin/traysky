<p align="center">
  <img width="128" align="center" src="Traysky/Assets/Wings-Light.svg">
</p>
<h1 align="center">Traysky</h1>
<p align="center">Bluesky in your Windows system tray</p>

Traysky is a tiny Bluesky client that lives in the tray. Click the butterfly and a flyout opens
with your **Home** timeline, your **Notifications**, and a compose box. The icon shows a dot when
you have unread notifications, and Windows toasts tell you about mentions and replies. That's it —
the fastest way to post, the calmest way to keep up.

Built with .NET 10, WinUI 3, the Windows App SDK and [idunno.Bluesky](https://bluesky.idunno.dev/),
on the tray plumbing of [Traydio](https://github.com/TheJoeFin/Traydio).

## Features

- 🦋 Tray icon with an unread dot; theme-aware when signed out
- 🏠 Home timeline with rich text (links, mentions, hashtags), images, link cards and quotes
- 🔔 Notifications grouped like the official app ("A, B and 3 others liked your post")
- ✍️ Compose, reply and quote from the flyout — Ctrl+Enter to post, 300-grapheme counter
- ❤️ Like and repost with instant feedback
- 🔐 Sign in with an [app password](https://bsky.app/settings/app-passwords); the refresh token is
  stored DPAPI-encrypted, never your password
- ⚙️ Configurable tray clicks, poll interval, toast types, start with Windows
- ⌨️ Ctrl+1 / Ctrl+2 tabs, Ctrl+N compose, Ctrl+R refresh, Esc to hide

## How to build

- Install [Visual Studio 2026](https://visualstudio.microsoft.com/) with the **.NET desktop development**
  and **Windows application development** (Windows App SDK) workloads, or the .NET 10 SDK.
- Open `Traysky.slnx` and press F5 (Local Machine), or:

```
dotnet build Traysky.slnx -p:Platform=x64
dotnet test Traysky.Tests/Traysky.Tests.csproj -p:Platform=x64
```

### Previewing without an account

Debug builds can show sample data: create an empty `preview.flag` file in the package's
`LocalState` folder (`%LOCALAPPDATA%\Packages\40087JoeFinApps.Traysky_*\LocalState\`) and launch.

## Project layout

See [PLAN.md](PLAN.md) for the architecture, decisions and milestones. In short:

| Folder | What |
|---|---|
| `Traysky/Services` | `BlueskySessionService` (the one agent), `NotificationPollService`, `SessionStore` (DPAPI), `FeedMapper`, plus pure `*Policy` classes |
| `Traysky/ViewModels` | Timeline, Notifications, Compose, Login, Settings, Shell |
| `Traysky/Controls` | `TrayPopupWindow` (ported from Traydio), `PostCard`, `EmbedPresenter`, `ComposeBox` |
| `Traysky/Pages` | Shell, Login, Timeline, Notifications, Settings |
| `Traysky.Tests` | MSTest over the pure policy files, linked in without WinUI |

## Packages

- [idunno.Bluesky](https://www.nuget.org/packages/idunno.Bluesky) — AT Protocol / Bluesky client
- [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm), [CommunityToolkit.WinUI.Controls.Segmented](https://www.nuget.org/packages/CommunityToolkit.WinUI.Controls.Segmented), [CommunityToolkit.WinUI.Animations](https://www.nuget.org/packages/CommunityToolkit.WinUI.Animations)
- [Microsoft.WindowsAppSDK](https://www.nuget.org/packages/Microsoft.WindowsAppSDK), [WinUIEx](https://www.nuget.org/packages/WinUIEx), [Microsoft.Windows.CsWin32](https://www.nuget.org/packages/Microsoft.Windows.CsWin32)

## License

MIT — see [LICENSE.txt](LICENSE.txt).
