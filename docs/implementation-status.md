# Desktop Puppy Implementation Status

> This file is the handoff point for future conversations. Read it before changing code.

**Last updated:** 2026-09-23
**Current phase:** Phase 2 complete — core contracts and placement policy
**Current branch:** `codex/desktop-puppy-foundation`
**Worktree:** `C:\Users\ton20\.codex\worktrees\desktop-puppy-foundation\desktop-puppy`
**Milestone commit:** core contracts and placement policy (branch HEAD after commit)
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
- [x] Added `IClock`/`SystemClock` and immutable offline settings defaults.
- [x] Added pure monitor selection and 20-pixel-visible pet placement clamping.
- [x] Updated `.gitignore` and `README.md` with the current phase and resume entry point.
- [x] Verified the new core/placement tests pass (4 focused tests).


## Test coverage audit — 2026-09-23

Current automated test count: **6 passed, 0 failed**.

Covered:

- `ApplicationInfo.ProductName` smoke contract;
- hit cooldown policy;
- default settings policy;
- injectable clock contract;
- pet position clamping with a minimum visible edge;
- missing-monitor fallback to the primary monitor.

Verified outside unit tests:

- Release compilation with 0 warnings and 0 errors;
- application process starts and remains alive during a short startup smoke check;
- `git diff --check` is clean.

Not yet verified:

- single-click delay versus double-click cancellation;
- drag behavior and click suppression during drag;
- right-click bubble display and bubble close button;
- exit button behavior;
- actual WPF transparency, topmost behavior, DPI scaling, multi-monitor movement, and monitor removal;
- single-instance behavior;
- tray, startup registration, fullscreen detection, foreground hooks, SQLite, usage statistics, AI, reports, settings persistence, and data deletion;
- the full PRD acceptance matrix.

The current 6-test suite must not be described as full MVP coverage. The next implementation milestone must add testable interaction controllers before relying on manual UI checks.

## Current milestone contents

The current implementation remains offline-only. It intentionally does **not** include:

- SQLite or local usage history;
- Win32 foreground hooks, fullscreen detection, native monitor enumeration, or tray;
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

Observed on 2026-09-23 for the previous milestone:

```text
Tests: 2 passed, 0 failed
Release build: 0 warnings, 0 errors
Startup smoke check: process stayed alive after launch and was then stopped by the verification command
```

The Phase 2 focused verification observed 4 passed tests and 0 failures. The full Release verification observed 6 passed tests, 0 warnings, 0 errors, and a successful startup smoke check.

## Important environment notes

- `dotnet` was not available from the existing PowerShell PATH when this work started. Use `C:\Program Files\dotnet\dotnet.exe` or add that directory to PATH.
- The machine's default NuGet configuration contains a missing Visual Studio offline source. Restore with the explicit source `https://api.nuget.org/v3/index.json` until the project adds a local NuGet configuration.
- The original checkout has uncommitted documentation edits that predate this worktree. Do not overwrite them; reconcile or cherry-pick deliberately later.
- The managed worktree is outside the original project directory. Future conversations should use this worktree/branch or inspect this status file before making changes.

## Next resume action

1. Read this file and confirm the worktree/branch.
2. Add the next failing tests for the native window boundary and single-instance policy.
3. Connect `PetPlacementPolicy` to a monitor adapter without moving Win32 calls into the WPF view.
4. Keep the app offline and runnable while adding the native boundary.
5. Update this file immediately after each verified milestone.

