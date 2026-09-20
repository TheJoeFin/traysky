# Project Instructions

- Do not run `dotnet build`, `dotnet test`, `dotnet restore`, or any command that automatically restores packages unless the user explicitly requests it.
- Pure decision logic goes in `*Policy` / helper classes with no WinRT dependency, linked into `Traysky.Tests` (see the test csproj for the pattern).
- See PLAN.md for the architecture and milestones.
