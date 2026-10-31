# Project Instructions

- Do not run `dotnet build`, `dotnet test`, `dotnet restore`, or any command that automatically restores packages unless the user explicitly requests it.
- Pure decision logic goes in `*Policy` / helper classes with no WinRT dependency, linked into `Traysky.Tests` (see the test csproj for the pattern).
- See PLAN.md for the architecture and milestones.

## Tray app conventions

This is a tray-only WinUI 3 app (the `winui-tray-app` skill's pattern). Load that skill before changing app lifetime, the flyout, the tray icon, polling, settings, or secrets.

- `App.xaml.cs` owns the tray icon, single instance, and the only exit path (the Exit menu). There is no main window.
- The flyout is hidden on dismiss and closed after a minute hidden; anything it starts, it must stop when hidden.
- Network work runs only while visible, through the foreground poller. Background work the tray icon itself displays needs a documented reason.
- Tokens live in DPAPI/credential storage, never in LocalSettings or git.
- Releases: bump `Identity/@Version`, run `build-msix-for-gh.ps1`, publish the draft without changing its tag, then run `build-msix-for-gh.ps1 -VerifyPublished`. The script is a copy of the `winui-tray-app` skill's; fix it there and recopy, never here.
