# Desktop Puppy Implementation Status

> This file is the handoff point for future conversations. Read it before changing code.

**Last updated:** 2026-09-23
**Current phase:** Phase 1 complete — WPF skeleton and offline pet
**Current branch:** `codex/desktop-puppy-foundation`
**Worktree:** `C:\Users\ton20\.codex\worktrees\desktop-puppy-foundation\desktop-puppy`
**Milestone commit:** first runnable offline pet foundation (branch HEAD after commit)
**Base commit:** `c609a5f`

## Completed

- [x] Reviewed the PRD, design spec, and implementation plan.
- [x] Confirmed the repository initially contained documentation only.
- [x] Created an isolated implementation worktree and `codex/desktop-puppy-foundation` branch.
- [x] Installed .NET SDK `10.0.401` and confirmed Windows Desktop runtime availability.
- [x] Added `global.json` to pin the SDK family with `latestFeature` roll-forward.
- [x] Created `DesktopPuppy.sln`, the WPF app project, and the xUnit test project.
- [x] Wrote and observed the RED smoke test for `ApplicationInfo.ProductName`.
- [x] Wrote and observed the RED hit-cooldown test for `PetInteractionPolicy`.
- [x] Implemented `ApplicationInfo.ProductName` as `Desktop Puppy`.
- [x] Implemented the transparent, always-on-top offline placeholder pet window.
- [x] Implemented delayed single-click local lines using the Windows double-click interval.
- [x] Implemented double-click hit reaction with a one-second local cooldown.
- [x] Implemented right-click local bubble, bubble close button, drag movement, and a visible exit button.
- [x] Updated `.gitignore` and `README.md` with the current phase and resume entry point.
- [x] Verified 2/2 tests pass in Release configuration.
- [x] Verified Release build succeeds with 0 warnings and 0 errors.
- [x] Verified the desktop puppy process starts successfully in a startup smoke check.
- [x] Verified `git diff --check` is clean.

## Current milestone contents

This milestone is offline-only. It intentionally does **not** include:

- SQLite or local usage history;
- Win32 foreground hooks, fullscreen detection, monitor placement, or tray;
- DeepSeek/Jev, API keys, network calls, or AI fallback;
- real animation assets or continuous animation loops;
- settings persistence or startup registration.

`MainWindow.xaml` remains a compatibility placeholder. The actual startup surface is `src/DesktopPuppy/Pet/PetWindow.xaml`.

## Verification commands

Run from the worktree with .NET 10 SDK:

```powershell
$env:DOTNET_CLI_HOME = "C:\Users\ton20\Documents\works\projects\desktop-puppy\.dotnet-home"
& "C:\Program Files\dotnet\dotnet.exe" test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --no-restore --configuration Release
& "C:\Program Files\dotnet\dotnet.exe" build DesktopPuppy.sln --configuration Release --no-restore
& "C:\Program Files\dotnet\dotnet.exe" run --project src/DesktopPuppy/DesktopPuppy.csproj --configuration Release --no-build
```

Observed on 2026-09-23:

```text
Tests: 2 passed, 0 failed
Release build: 0 warnings, 0 errors
Startup smoke check: process stayed alive after launch and was then stopped by the verification command
```

## Important environment notes

- `dotnet` was not available from the existing PowerShell PATH when this work started. Use `C:\Program Files\dotnet\dotnet.exe` or add that directory to PATH.
- The machine's default NuGet configuration contains a missing Visual Studio offline source. Restore with the explicit source `https://api.nuget.org/v3/index.json` until the project adds a local NuGet configuration.
- The original checkout has uncommitted documentation edits that predate this worktree. Do not overwrite them; reconcile or cherry-pick deliberately later.
- The managed worktree is outside the original project directory. Future conversations should use this worktree/branch or inspect this status file before making changes.

## Next resume action

1. Read this file and confirm the worktree/branch.
2. Add the next failing tests for the core clock/settings contracts and pet placement policy.
3. Implement local settings defaults, an injectable clock, and a pure monitor/work-area placement boundary.
4. Keep the app offline and runnable while adding those boundaries.
5. Update this file immediately after each verified milestone.

