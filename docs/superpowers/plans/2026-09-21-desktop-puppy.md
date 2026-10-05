# Desktop Puppy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a personal Windows desktop puppy with local usage statistics, persistent mischievous interactions, AI-generated content selected by Jev, 18:20 daily reports, and bounded resource usage.

**Architecture:** Use one C# WPF process. WPF owns windows, animations, bubbles, settings and report presentation. Focused Win32 adapters own native window behavior, foreground events, monitor/fullscreen detection, tray and startup. SQLite stores local usage, title intervals, content history, request outcomes and encrypted API-key blobs. DeepSeek generates candidate text; Jev (`jev-latest`) selects among locally validated candidates; local behavior and hard validation remain authoritative.

**Tech Stack:** `net10.0-windows`, WPF, Win32 P/Invoke, `Microsoft.Data.Sqlite`, `HttpClient`, Windows DPAPI, `System.Text.Json`, PNG animation frames, xUnit, and a single-process composition root.

**Spec:** `docs/superpowers/specs/2026-09-21-desktop-puppy-design.md`

## Global Constraints

- Target `net10.0-windows`; support Windows 10 22H2 and later; x64 is the first supported architecture.
- The UI uses WPF only; do not add Electron, Tauri, WebView, HTML, CSS or JavaScript.
- The application is single-instance and single-process for the first phase.
- Local behavior decides when an activity occurs; AI only generates or selects content.
- There is no daily activity cap and no daily AI cap, but each activity and request still has cooldown, timeout, retry and cancellation rules.
- DeepSeek generates six candidates per request; the candidate-pool builder calls DeepSeek serially and stops when at least four locally valid candidates exist; it makes at most five requests per scene.
- Jev uses the official TypeSafe API and model `jev-latest`; it receives all valid candidates and only returns a selection result.
- DeepSeek and Jev requests may run concurrently across independent scenes; no global AI queue or concurrency limiter is introduced.
- DeepSeek and Jev API keys are independently encrypted with Windows DPAPI before being stored in SQLite; plaintext keys never enter logs, prompts, ordinary settings or tests.
- A failed AI scene never uses a local fallback line; after five failed attempts it records the failure and shows a non-blocking notification.
- All operational history is retained for at most 15 local days; settings and encrypted API-key blobs are not part of rolling history cleanup.
- Window-title records require two seconds of stability; titles that change or disappear before two seconds are discarded.
- Fullscreen hides all pet windows and pauses new random activity, but usage and title collection continue; in-flight AI requests are not cancelled.
- Lock, sleep and hibernate close foreground sessions; unlocking starts a new session.
- No network or SQLite work runs on the WPF Dispatcher thread.
- Production code is written only after a corresponding failing test has been run and observed.
- Idle rendering is event-driven; animation timers run only while a finite clip is active.

---

## Task 1: Repository, SDK configuration and WPF skeleton

**Files:**
- Create: `DesktopPuppy.sln`
- Create: `src/DesktopPuppy/DesktopPuppy.csproj`
- Create: `src/DesktopPuppy/App.xaml`
- Create: `src/DesktopPuppy/App.xaml.cs`
- Create: `src/DesktopPuppy/ApplicationInfo.cs`
- Create: `src/DesktopPuppy/MainWindow.xaml`
- Create: `src/DesktopPuppy/MainWindow.xaml.cs`
- Create: `tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj`
- Create: `tests/DesktopPuppy.Tests/SmokeTests.cs`
- Create: `Directory.Build.props`
- Modify: `.gitignore`
- Modify: `README.md`

**Interfaces:**
- `ApplicationInfo.ProductName` is a public constant with value `Desktop Puppy`.
- The application targets `net10.0-windows` and enables WPF and nullable reference types.
- The test project references the application assembly and can run without showing a WPF window.
- `Directory.Build.props` enables deterministic builds, nullable warnings and Windows targeting settings shared by projects.

- [ ] **Step 1: Verify the SDK prerequisite.**

Install the .NET 10 SDK before running this task. The current review environment does not have `dotnet` on `PATH`, so do not treat missing-command output as a product test failure.

Run:

```text
dotnet --info
```

Expected: the .NET 10 SDK is listed and the command exits successfully.

- [ ] **Step 2: Write the failing smoke test.**

Create `SmokeTests.cs` with this assertion:

```csharp
[Fact]
public void ProductName_is_desktop_puppy()
{
    Assert.Equal("Desktop Puppy", ApplicationInfo.ProductName);
}
```

At this point `ApplicationInfo` does not exist.

- [ ] **Step 3: Run RED.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj
```

Expected: compilation fails because `ApplicationInfo.ProductName` is not defined.

- [ ] **Step 4: Create the minimal WPF solution.**

Add the Windows-targeted project, `ApplicationInfo`, `App.xaml`, and a borderless placeholder `MainWindow`. Do not add native hooks, statistics, AI or product behavior.

- [ ] **Step 5: Run GREEN.**

```text
dotnet build --configuration Release
dotnet test --configuration Release --no-restore
```

Expected: the build and smoke test pass with no new warnings.

- [ ] **Step 6: Commit the skeleton.**

```text
git add DesktopPuppy.sln src tests Directory.Build.props .gitignore README.md
git commit -m "chore: scaffold WPF desktop puppy"
```

## Task 2: Core contracts, settings, clocks and scene descriptions

**Files:**
- Create: `src/DesktopPuppy/Core/Clock/IClock.cs`
- Create: `src/DesktopPuppy/Core/Clock/SystemClock.cs`
- Create: `src/DesktopPuppy/Core/Events/PuppyEvent.cs`
- Create: `src/DesktopPuppy/Core/Events/PuppyEventType.cs`
- Create: `src/DesktopPuppy/Core/Settings/AppSettings.cs`
- Create: `src/DesktopPuppy/Core/Settings/ActivitySettings.cs`
- Create: `src/DesktopPuppy/Core/Settings/AiSettings.cs`
- Create: `src/DesktopPuppy/Core/Content/SceneCard.cs`
- Create: `src/DesktopPuppy/Core/Content/ContentCandidate.cs`
- Create: `src/DesktopPuppy/Core/Content/ContentValidationProfile.cs`
- Create: `src/DesktopPuppy/Core/Requests/AiRequestId.cs`
- Create: `tests/DesktopPuppy.Tests/Core/SettingsTests.cs`
- Create: `tests/DesktopPuppy.Tests/Core/SceneCardTests.cs`

**Interfaces:**
- `IClock.UtcNow` returns `DateTimeOffset`.
- `PuppyEvent` contains a `PuppyEventType`, `DateTimeOffset OccurredAt`, and an immutable context dictionary.
- `AppSettings` includes activity, statistics, sound, fullscreen, startup, interaction and AI settings.
- `ActivitySettings` defaults to a 20-minute global cooldown, no daily maximum, 23:00-08:00 quiet hours and enabled fullscreen suppression.
- `AiSettings` contains provider flags, DeepSeek model, Jev model `jev-latest`, per-scene timeouts, five-attempt retry policy and no daily request limit; it never contains plaintext API keys.
- `SceneCard` carries event type, application name, window title, time segment, humor style, validation profile and compact history summary.
- `ContentCandidate` contains text, source request ID, scene ID and local validation metadata.

- [ ] **Step 1: Write failing tests for defaults and clock injection.**

Cover these exact defaults:

```csharp
Assert.Equal(TimeSpan.FromMinutes(20), settings.Activity.GlobalCooldown);
Assert.Null(settings.Activity.DailyMaximum);
Assert.Equal(new TimeOnly(23, 0), settings.Activity.QuietHoursStart);
Assert.Equal(new TimeOnly(8, 0), settings.Activity.QuietHoursEnd);
Assert.Equal("deepseek-flash", settings.Ai.DeepSeekModel);
Assert.Equal("jev-latest", settings.Ai.JevModel);
Assert.Equal(5, settings.Ai.MaxAttempts);
Assert.Null(settings.Ai.DailyRequestLimit);
```

Also verify a fake clock controls `UtcNow` without touching system time.

- [ ] **Step 2: Run RED.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Core
```

Expected: compilation fails because the core types are missing.

- [ ] **Step 3: Implement immutable core types.**

Use records or immutable classes with explicit defaults. Represent the absence of a daily maximum as `null`, not as a large sentinel number. Keep API keys outside all settings records.

- [ ] **Step 4: Run GREEN.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Core
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj
```

- [ ] **Step 5: Commit.**

```text
git add src/DesktopPuppy/Core tests/DesktopPuppy.Tests/Core
git commit -m "feat: add core settings and scene contracts"
```

## Task 3: SQLite schema, migrations, retention and repositories

**Files:**
- Create: `src/DesktopPuppy/Storage/DatabaseConnectionFactory.cs`
- Create: `src/DesktopPuppy/Storage/DatabaseMigrator.cs`
- Create: `src/DesktopPuppy/Storage/RetentionService.cs`
- Create: `src/DesktopPuppy/Storage/Repositories/UsageRepository.cs`
- Create: `src/DesktopPuppy/Storage/Repositories/WindowTitleRepository.cs`
- Create: `src/DesktopPuppy/Storage/Repositories/InteractionRepository.cs`
- Create: `src/DesktopPuppy/Storage/Repositories/ContentHistoryRepository.cs`
- Create: `src/DesktopPuppy/Storage/Repositories/AiRequestRepository.cs`
- Create: `src/DesktopPuppy/Storage/Repositories/DailyReportRepository.cs`
- Create: `src/DesktopPuppy/Storage/Repositories/SettingsRepository.cs`
- Create: `src/DesktopPuppy/Storage/Repositories/ApiKeyBlobRepository.cs`
- Create: `src/DesktopPuppy/Storage/Models/UsageSessionRecord.cs`
- Create: `src/DesktopPuppy/Storage/Models/WindowTitleIntervalRecord.cs`
- Create: `src/DesktopPuppy/Storage/Models/InteractionRecord.cs`
- Create: `src/DesktopPuppy/Storage/Models/ContentHistoryRecord.cs`
- Create: `src/DesktopPuppy/Storage/Models/AiRequestRecord.cs`
- Create: `src/DesktopPuppy/Storage/Models/DailyReportRecord.cs`
- Create: `src/DesktopPuppy/Storage/Models/EncryptedApiKeyBlob.cs`
- Create: `tests/DesktopPuppy.Tests/Storage/DatabaseMigratorTests.cs`
- Create: `tests/DesktopPuppy.Tests/Storage/RepositoryTests.cs`
- Create: `tests/DesktopPuppy.Tests/Storage/RetentionServiceTests.cs`

**Interfaces:**
- `DatabaseConnectionFactory.Open()` opens `%LOCALAPPDATA%\DesktopPuppy\desktop-puppy.db` with foreign keys enabled.
- `DatabaseMigrator.Migrate(connection)` creates and upgrades schema versions transactionally.
- `UsageRepository.AppendSession(UsageSessionRecord)` persists closed foreground sessions.
- `WindowTitleRepository.AppendStableInterval(WindowTitleIntervalRecord)` persists titles stable for two seconds.
- `ContentHistoryRepository.AddDisplayed(ContentHistoryRecord)` records only displayed content for duplicate prevention; candidate history is stored separately.
- `AiRequestRepository.AddOrUpdate(AiRequestRecord)` stores provider, model, attempt, outcome, display state and redacted error kind, never credentials.
- `DailyReportRepository.Upsert(DailyReportRecord)` keeps one status record per local report date.
- `RetentionService.DeleteBefore(localDateExclusive)` deletes all operational history older than 15 local days in one transaction while preserving settings and encrypted key blobs.
- `SettingsRepository` stores ordinary settings without secrets.
- `ApiKeyBlobRepository` stores provider-specific DPAPI ciphertext and metadata only.

Schema version 1 includes:

```text
schema_version
app_settings
api_key_blobs
usage_sessions
window_title_intervals
interactions
content_candidates
content_history
ai_requests
daily_reports
```

All persisted instants use UTC. Local report dates and local-day cutoffs are stored explicitly so DST and timezone changes do not change historical report identity.

- [ ] **Step 1: Write failing migration tests.**

Assert that migration creates every table above, creates indexes on UTC timestamps and local report date, and can be run twice without changing data.

- [ ] **Step 2: Write failing repository and retention tests.**

Cover:

```text
usage session round-trip with application, title, start, end and duration
stable title interval round-trip
content candidate stored without becoming displayed history
AI request stores provider/model/attempt/status without key text
one daily report row per local date
retention deletes records older than 15 local days
retention preserves settings and encrypted key blobs
clear-history deletes operational history but not settings
```

- [ ] **Step 3: Run RED.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Storage
```

Expected: compilation or schema failures.

- [ ] **Step 4: Implement parameterized SQLite access.**

Use parameterized SQL only. Use UTC timestamps for instant columns, explicit local dates for daily records, indexes for retention and report lookup, and a transaction for each retention/delete operation. Never put a key into exception text or test output.

- [ ] **Step 5: Run GREEN.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Storage
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj
```

Verify no database file is created inside the repository.

- [ ] **Step 6: Commit.**

```text
git add src/DesktopPuppy/Storage tests/DesktopPuppy.Tests/Storage
git commit -m "feat: add sqlite schema retention and repositories"
```

## Task 4: Native Windows boundaries and single-instance startup

**Files:**
- Create: `src/DesktopPuppy/Native/INativeWindowService.cs`
- Create: `src/DesktopPuppy/Native/Win32WindowService.cs`
- Create: `src/DesktopPuppy/Native/IForegroundWindowWatcher.cs`
- Create: `src/DesktopPuppy/Native/WinEventForegroundWatcher.cs`
- Create: `src/DesktopPuppy/Native/IFullscreenDetector.cs`
- Create: `src/DesktopPuppy/Native/Win32FullscreenDetector.cs`
- Create: `src/DesktopPuppy/Native/IMonitorService.cs`
- Create: `src/DesktopPuppy/Native/Win32MonitorService.cs`
- Create: `src/DesktopPuppy/Native/ISystemTrayService.cs`
- Create: `src/DesktopPuppy/Native/SystemTrayService.cs`
- Create: `src/DesktopPuppy/Native/IStartupRegistrationService.cs`
- Create: `src/DesktopPuppy/Native/RegistryStartupRegistrationService.cs`
- Create: `src/DesktopPuppy/Native/ISingleInstanceGuard.cs`
- Create: `src/DesktopPuppy/Native/SingleInstanceGuard.cs`
- Create: `tests/DesktopPuppy.Tests/Native/FullscreenDetectorTests.cs`
- Create: `tests/DesktopPuppy.Tests/Native/MonitorPlacementTests.cs`
- Create: `tests/DesktopPuppy.Tests/Native/StartupRegistrationTests.cs`
- Create: `tests/DesktopPuppy.Tests/Native/SingleInstanceTests.cs`

**Interfaces:**
- `INativeWindowService.ConfigurePetWindow(Window, PetWindowOptions)` applies borderless, transparent, topmost and hit-test behavior.
- `IForegroundWindowWatcher.Start(Action<ForegroundWindowSnapshot>)` and `Stop()` manage native foreground events and release hook handles/delegates.
- `IFullscreenDetector.IsFullscreen(ForegroundWindowSnapshot, MonitorInfo)` recognizes exclusive fullscreen and borderless windows covering the monitor, but not ordinary maximized windows.
- `IMonitorService.GetWorkAreas()` returns monitor identity, bounds, work area and DPI scale.
- `ISystemTrayService` creates immediate tray UI and exposes show/hide, pause/resume, settings, today report, clear history and exit commands.
- `IStartupRegistrationService.SetEnabled(bool)` uses `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` and the `--startup` argument.
- `ISingleInstanceGuard.TryAcquire(Action activateExisting)` prevents duplicate processes and activates the existing instance on a second launch.

- [ ] **Step 1: Write failing pure geometry and policy tests.**

Test that:

```text
exclusive fullscreen is true
borderless monitor-sized window is true
ordinary maximized work-area window is false
any fullscreen monitor suppresses all pet windows
pet position is clamped with at least 20 logical pixels visible
missing saved monitor falls back to primary monitor
startup registration writes/removes only the app-owned value
second-instance activation does not create a second guard
```

- [ ] **Step 2: Run RED.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Native
```

- [ ] **Step 3: Implement native adapters.**

Keep P/Invoke declarations inside `Native`. Use WinEvent foreground hooks with an explicit message-pump lifetime, keep delegates rooted until `Stop`, and release hooks deterministically. Use monitor work areas and DPI-aware coordinates for placement. Use a per-user registry Run value and no elevation.

- [ ] **Step 4: Run GREEN and perform manual native checks.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Native
```

Manually verify foreground callbacks, fullscreen detection, monitor removal, startup registration, tray commands and second-launch activation.

- [ ] **Step 5: Commit.**

```text
git add src/DesktopPuppy/Native tests/DesktopPuppy.Tests/Native
git commit -m "feat: add native window monitor and startup boundaries"
```

## Task 5: Foreground usage collection and title debounce

**Files:**
- Create: `src/DesktopPuppy/Usage/ForegroundSessionTracker.cs`
- Create: `src/DesktopPuppy/Usage/UsageCollector.cs`
- Create: `src/DesktopPuppy/Usage/WindowTitleDebouncer.cs`
- Create: `src/DesktopPuppy/Usage/ApplicationClassifier.cs`
- Create: `src/DesktopPuppy/Usage/DailyUsageAggregator.cs`
- Create: `tests/DesktopPuppy.Tests/Usage/ForegroundSessionTrackerTests.cs`
- Create: `tests/DesktopPuppy.Tests/Usage/WindowTitleDebouncerTests.cs`
- Create: `tests/DesktopPuppy.Tests/Usage/DailyUsageAggregatorTests.cs`
- Create: `tests/DesktopPuppy.Tests/Usage/ApplicationClassifierTests.cs`

**Interfaces:**
- `ForegroundSessionTracker.OnForegroundApplicationChanged(ForegroundWindowSnapshot snapshot, DateTimeOffset at)` closes the previous session and opens the next one immediately.
- `ForegroundSessionTracker.OnSessionBoundary(SessionBoundary boundary, DateTimeOffset at)` closes sessions for lock, sleep, hibernate and shutdown.
- `WindowTitleDebouncer.OnTitleChanged(snapshot, at)` emits an interval only after the title is stable for two seconds; it emits nothing for a title abandoned earlier.
- `UsageCollector` connects the native watcher, fullscreen detector, title debouncer and repository.
- `DailyUsageAggregator.Build(localDate, sessions, interactions)` returns immutable facts for report planning.
- `ApplicationClassifier.Classify(processName)` returns Work, Entertainment, Utility, Communication, Development or Unknown using local rules and user overrides.

- [ ] **Step 1: Write failing tracker tests.**

Cover:

```text
first foreground event opens a session
changing application closes the prior session immediately
same foreground event is ignored
lock closes the session
sleep closes the session
unlock starts a new session
shutdown closes the open session
Desktop Puppy itself is excluded from usage totals
```

- [ ] **Step 2: Write failing title debounce tests.**

Cover:

```text
title stable for 2 seconds emits one interval
title changes before 2 seconds emit nothing
same title does not emit duplicate intervals
leaving a window before 2 seconds discards the pending title
new title closes the previous interval
```

- [ ] **Step 3: Write failing aggregation tests.**

Cover foreground duration, application totals, switch count, longest session, work/entertainment ratio, deep-night usage, title examples and interactions. Treat foreground time as active time without an idle threshold. Split sessions at local midnight for reports.

- [ ] **Step 4: Run RED.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Usage
```

- [ ] **Step 5: Implement collection and aggregation.**

Use the native watcher for foreground events, explicit lock/sleep boundaries, a two-second title debounce timer and batched repository writes. Do not store window titles that did not reach stability. Keep all UI updates off the collector thread.

- [ ] **Step 6: Run GREEN and perform a manual session check.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Usage
```

Manually switch applications, lock/unlock, sleep/wake, use a fullscreen application, change window titles rapidly and verify the persisted intervals.

- [ ] **Step 7: Commit.**

```text
git add src/DesktopPuppy/Usage tests/DesktopPuppy.Tests/Usage
git commit -m "feat: collect foreground usage and stable window titles"
```

## Task 6: Pet window, animation state machine and interaction bubbles

**Files:**
- Create: `src/DesktopPuppy/Pet/PetWindow.xaml`
- Create: `src/DesktopPuppy/Pet/PetWindow.xaml.cs`
- Create: `src/DesktopPuppy/Pet/Animation/AnimationClip.cs`
- Create: `src/DesktopPuppy/Pet/Animation/AnimationState.cs`
- Create: `src/DesktopPuppy/Pet/Animation/AnimationController.cs`
- Create: `src/DesktopPuppy/Pet/Interaction/InteractionController.cs`
- Create: `src/DesktopPuppy/Pet/Interaction/HitReactionService.cs`
- Create: `src/DesktopPuppy/Pet/Interaction/PetInteractionEvent.cs`
- Create: `src/DesktopPuppy/Pet/Bubbles/SpeechBubble.cs`
- Create: `src/DesktopPuppy/Pet/Bubbles/SpeechBubbleManager.cs`
- Create: `assets/pet/.gitkeep`
- Create: `tests/DesktopPuppy.Tests/Pet/AnimationControllerTests.cs`
- Create: `tests/DesktopPuppy.Tests/Pet/InteractionControllerTests.cs`
- Create: `tests/DesktopPuppy.Tests/Pet/HitReactionServiceTests.cs`
- Create: `tests/DesktopPuppy.Tests/Pet/SpeechBubbleManagerTests.cs`

**Interfaces:**
- `AnimationController.Play(AnimationClip)` starts a finite clip and raises `Completed`.
- `AnimationController.SetIdle(AnimationState)` selects idle without a permanent high-FPS loop.
- `HitReactionService.RegisterHit(DateTimeOffset)` returns whether a hit is accepted after cooldown.
- `InteractionController` emits single click, double-click, right-click, drag and hit events using the system double-click time.
- `SpeechBubbleManager.AddPlaceholder(requestId, text, expiresAt)` and `ReplacePlaceholder(requestId, text)` manage right-click AI bubbles.
- `SpeechBubbleManager.Close(requestId)` closes one bubble; bubbles are laid out around and moved with the pet.

- [ ] **Step 1: Write failing animation and interaction tests.**

Test:

```text
idle -> hit -> idle after clip duration
hit during cooldown is rejected
single click is delayed until system double-click window expires
double click cancels single-click and emits hit
right click emits special interaction
right click does not open a pet context menu
drag emits movement without emitting click or hit
```

- [ ] **Step 2: Write failing bubble tests.**

Test that:

```text
placeholder expires after 15 seconds
successful result replaces the placeholder
late result is marked not displayed
closing one bubble leaves others open
bubbles keep relative placement when the pet moves
```

- [ ] **Step 3: Run RED.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Pet
```

- [ ] **Step 4: Implement the WPF rendering and interaction surface.**

Use frame swapping or `WriteableBitmap` updates. Start a Dispatcher timer only while a finite clip is active. Configure the transparent window through the native adapter. Display placeholder bubbles immediately for right-click AI scenes; do not write placeholders to displayed content history.

- [ ] **Step 5: Run GREEN and manually verify the window.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Pet
```

Verify click/double-click/right-click/drag, multi-monitor placement, edge clamping, DPI changes, bubble close buttons and persistent bubble movement.

- [ ] **Step 6: Commit.**

```text
git add src/DesktopPuppy/Pet assets tests/DesktopPuppy.Tests/Pet
git commit -m "feat: add pet interactions animations and bubbles"
```

## Task 7: Local content library, hard validator and candidate pool primitives

**Files:**
- Create: `src/DesktopPuppy/Content/PersonaProfile.cs`
- Create: `src/DesktopPuppy/Content/SceneCardFactory.cs`
- Create: `src/DesktopPuppy/Content/LocalPhraseBank.cs`
- Create: `src/DesktopPuppy/Content/ContentValidator.cs`
- Create: `src/DesktopPuppy/Content/ContentSelector.cs`
- Create: `src/DesktopPuppy/Content/CandidatePool.cs`
- Create: `src/DesktopPuppy/Content/Similarity/NormalizedText.cs`
- Create: `src/DesktopPuppy/Content/Similarity/NgramSimilarity.cs`
- Create: `src/DesktopPuppy/Content/Phrases/seed-phrases.json`
- Create: `tests/DesktopPuppy.Tests/Content/ContentValidatorTests.cs`
- Create: `tests/DesktopPuppy.Tests/Content/ContentSelectorTests.cs`
- Create: `tests/DesktopPuppy.Tests/Content/CandidatePoolTests.cs`
- Create: `tests/DesktopPuppy.Tests/Content/NgramSimilarityTests.cs`

**Interfaces:**
- `ContentValidator.Validate(ContentCandidate candidate, SceneCard scene, IReadOnlyList<ContentHistoryRecord> recent)` returns accepted/rejected plus explicit reasons.
- `CandidatePool.AddValidated(ContentCandidate candidate)` adds only valid, non-duplicate candidates.
- `CandidatePool.HasMinimum(int minimum)` returns true at four valid candidates.
- `ContentSelector.SelectLocalTieBreak(...)` ranks valid candidates by display history, similarity, scene relevance, style cooldown and length.

- [ ] **Step 1: Write failing validator tests.**

Cover empty text, scene length ranges, Markdown, multiple paragraphs, prohibited AI phrases, excessive emoji, repeated punctuation, unsupported facts, exact duplicates, high n-gram similarity, repeated humor style and acceptance of a short concrete mischievous line.

Use these profiles:

```text
ClickHit: 8-30
Greeting: 10-50
Activity: 10-60
UserRequested: 10-80
Report: 200-500 with basic format checks only
```

- [ ] **Step 2: Write failing candidate-pool tests.**

Cover six candidates per batch, rejected candidates not entering the pool, current-batch deduplication, 15-day history comparison and stopping when four valid candidates exist without imposing a five-candidate cap.

- [ ] **Step 3: Run RED.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Content
```

- [ ] **Step 4: Implement local content rules.**

Use normalized text and n-gram similarity. The validator rejects; it never silently rewrites a candidate. Store rejected counts and reasons without storing rejected full text.

- [ ] **Step 5: Run GREEN.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Content
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj
```

- [ ] **Step 6: Commit.**

```text
git add src/DesktopPuppy/Content tests/DesktopPuppy.Tests/Content
git commit -m "feat: add content validation and candidate pools"
```

## Task 8: DPAPI key store, DeepSeek provider and candidate generation

**Files:**
- Create: `src/DesktopPuppy/Security/IApiKeyProtector.cs`
- Create: `src/DesktopPuppy/Security/DpapiApiKeyProtector.cs`
- Create: `src/DesktopPuppy/Security/ApiKeyService.cs`
- Create: `src/DesktopPuppy/Ai/IAiContentProvider.cs`
- Create: `src/DesktopPuppy/Ai/AiProviderException.cs`
- Create: `src/DesktopPuppy/Ai/DeepSeek/DeepSeekContentProvider.cs`
- Create: `src/DesktopPuppy/Ai/DeepSeek/DeepSeekRequest.cs`
- Create: `src/DesktopPuppy/Ai/DeepSeek/DeepSeekResponse.cs`
- Create: `src/DesktopPuppy/Ai/CandidatePoolBuilder.cs`
- Create: `tests/DesktopPuppy.Tests/Security/DpapiApiKeyProtectorTests.cs`
- Create: `tests/DesktopPuppy.Tests/Security/ApiKeyServiceTests.cs`
- Create: `tests/DesktopPuppy.Tests/Ai/DeepSeekContentProviderTests.cs`
- Create: `tests/DesktopPuppy.Tests/Ai/CandidatePoolBuilderTests.cs`

**Interfaces:**
- `IApiKeyProtector.Protect(string plaintext)` and `Unprotect(byte[] ciphertext)` use current-user DPAPI.
- `ApiKeyService.SaveAsync(provider, key)`, `GetAsync(provider)` and `DeleteAsync(provider)` use the encrypted blob repository; key values never enter log scopes or exceptions.
- `IAiContentProvider.GenerateCandidatesAsync(SceneCard scene, IReadOnlyList<ContentCandidate> existing, int variation, CancellationToken cancellationToken)` returns exactly six parsed candidates or a typed failure.
- `CandidatePoolBuilder.FillAsync(SceneCard scene, CancellationToken cancellationToken)` makes serial DeepSeek requests, validates every candidate locally, persists valid candidates, stops at four valid candidates and fails after five total attempts if fewer than four exist.

- [ ] **Step 1: Write failing security tests.**

Test that:

```text
protect then unprotect round-trips the key
wrong user/context cannot unprotect the blob
save/get/delete uses provider-specific blobs
ordinary settings never contain the key
exceptions and test output do not contain the key text
```

Use a fake protector for non-Windows service tests and a Windows integration test for the real DPAPI adapter.

- [ ] **Step 2: Write failing DeepSeek contract tests.**

Use a fake `HttpMessageHandler` to test:

```text
valid JSON with six candidates parses
empty body fails
invalid JSON fails
wrong candidate count fails
HTTP error fails
request body contains compact scene context and existing candidates
request body never contains an API key
```

- [ ] **Step 3: Write failing candidate-pool retry tests.**

Cover:

```text
first batch yields four valid candidates and stops
invalid candidates are not persisted as full text
candidate batches are requested serially
existing candidates are included in the next prompt
same prompt is not sent unchanged; variation number changes
fewer than four candidates after five attempts returns failure
backoff receives 1, 2, 4 and 8 second delays through an injected delay service
```

- [ ] **Step 4: Run RED.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Ai
```

- [ ] **Step 5: Implement the provider and pool builder.**

Use one shared `HttpClient`, scene-specific timeouts, `System.Text.Json`, response-size limits, structured JSON prompts and cancellation tokens. The provider default is `deepseek-flash`. Do not retry on the WPF Dispatcher. Count the initial request as attempt one.

- [ ] **Step 6: Run GREEN.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Ai
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj
```

- [ ] **Step 7: Commit.**

```text
git add src/DesktopPuppy/Security src/DesktopPuppy/Ai tests/DesktopPuppy.Tests/Security tests/DesktopPuppy.Tests/Ai
git commit -m "feat: add encrypted keys and DeepSeek candidate generation"
```

## Task 9: Jev provider and candidate selection

**Files:**
- Create: `src/DesktopPuppy/Ai/Jev/IJevDecisionProvider.cs`
- Create: `src/DesktopPuppy/Ai/Jev/JevDecisionProvider.cs`
- Create: `src/DesktopPuppy/Ai/Jev/JevRequest.cs`
- Create: `src/DesktopPuppy/Ai/Jev/JevResponse.cs`
- Create: `src/DesktopPuppy/Ai/Jev/JevSelectionService.cs`
- Create: `tests/DesktopPuppy.Tests/Ai/JevDecisionProviderTests.cs`
- Create: `tests/DesktopPuppy.Tests/Ai/JevSelectionServiceTests.cs`

**Interfaces:**
- `IJevDecisionProvider.SelectBestAsync(SceneCard scene, IReadOnlyList<ContentCandidate> candidates, CancellationToken cancellationToken)` calls the official TypeSafe API with model `jev-latest`.
- `JevSelectionService.SelectAsync(...)` retries only Jev, not DeepSeek, with a five-second per-attempt timeout, five total attempts and `1,2,4,8` second backoff.
- `JevSelectionResult` contains `BestCandidateId`, `Confidence`, `Probabilities` and raw provider status without raw secrets.

The Jev request includes compact structured context and all valid candidates, but not the complete DeepSeek prompt, all 15-day title history or rejected candidate text. The decision question is one `Choice`; no extra Score/Noul questions are sent.

- [ ] **Step 1: Write failing provider tests.**

Use a fake HTTP handler to test:

```text
request uses the official System One endpoint and jev-latest
valid Choice response maps to candidate ID and probabilities
empty response fails
invalid JSON fails
unknown candidate ID fails
HTTP error fails
request body excludes API keys and rejected candidates
```

- [ ] **Step 2: Write failing selection tests.**

Cover:

```text
valid Jev result selects the requested candidate
confidence below .55 invokes local tie-breaking but does not retry
first/second probability gap below .10 invokes local tie-breaking
unknown candidate triggers Jev retry
Jev failure does not regenerate DeepSeek candidates
five Jev failures return a typed failure
```

- [ ] **Step 3: Run RED.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Jev
```

- [ ] **Step 4: Implement Jev integration.**

Use the independently stored Jev API key, a shared `HttpClient`, five-second cancellation, structured request serialization and explicit model `jev-latest`. Keep local tie-breaking deterministic under an injected random source.

- [ ] **Step 5: Run GREEN.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Jev
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj
```

- [ ] **Step 6: Commit.**

```text
git add src/DesktopPuppy/Ai/Jev tests/DesktopPuppy.Tests/Ai
git commit -m "feat: add Jev candidate selection"
```

## Task 10: Activity scheduler, greetings and right-click AI orchestration

**Files:**
- Create: `src/DesktopPuppy/Activities/ActivityDefinition.cs`
- Create: `src/DesktopPuppy/Activities/ActivityScheduler.cs`
- Create: `src/DesktopPuppy/Activities/CooldownStore.cs`
- Create: `src/DesktopPuppy/Activities/GreetingService.cs`
- Create: `src/DesktopPuppy/Activities/RightClickSpecialInteractionService.cs`
- Create: `src/DesktopPuppy/Activities/AiSceneOrchestrator.cs`
- Create: `tests/DesktopPuppy.Tests/Activities/ActivitySchedulerTests.cs`
- Create: `tests/DesktopPuppy.Tests/Activities/GreetingServiceTests.cs`
- Create: `tests/DesktopPuppy.Tests/Activities/RightClickSpecialInteractionTests.cs`
- Create: `tests/DesktopPuppy.Tests/Activities/AiSceneOrchestratorTests.cs`

**Interfaces:**
- `ActivityScheduler.TrySchedule(ActivityContext)` returns a local decision or no-op because of cooldown, quiet hours, fullscreen, pause or disabled state; it never calls AI.
- `GreetingService.GetGreetingAsync(GreetingContext, CancellationToken)` uses local phrases or the AI scene pipeline according to policy.
- `RightClickSpecialInteractionService.HandleAsync(RightClickContext, CancellationToken)` plays local animation immediately, evaluates the right-click AI conditions, creates a 15-second placeholder and replaces it only when a result returns in time.
- `AiSceneOrchestrator.GenerateAndSelectAsync(SceneCard, CancellationToken)` composes `CandidatePoolBuilder`, `JevSelectionService`, repositories and notification service.

Right-click AI is allowed only when the setting is enabled and at least one condition is true: title changed since the prior special interaction, no similar displayed right-click line exists in the 15-day window, at least five minutes elapsed since the last AI right-click, or the current time is a special period such as late night.

- [ ] **Step 1: Write failing scheduler tests.**

Cover global cooldown, per-activity cooldown, no daily cap, quiet hours, fullscreen suppression, pause suppression, blacklisted applications, and local deterministic decisions.

- [ ] **Step 2: Write failing greeting and right-click tests.**

Cover:

```text
every ordinary click and hit uses local content without AI
right-click always plays local animation
right-click condition evaluation is local
a right-click can create an independent AI request
multiple right-click requests do not cancel each other
placeholder is removed after 15 seconds
late AI result is recorded but not displayed
five failed attempts remove the placeholder and notify
```

- [ ] **Step 3: Run RED.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Activities
```

- [ ] **Step 4: Implement local orchestration.**

Keep trigger decisions deterministic and independent from provider availability. Use separate request IDs for concurrent scenes. Persist displayed content only after the final bubble is actually shown.

- [ ] **Step 5: Run GREEN.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Activities
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj
```

- [ ] **Step 6: Commit.**

```text
git add src/DesktopPuppy/Activities tests/DesktopPuppy.Tests/Activities
git commit -m "feat: add local activities and right-click AI scenes"
```

## Task 11: Daily report planner, generation and history

**Files:**
- Create: `src/DesktopPuppy/Reports/DailyReportFacts.cs`
- Create: `src/DesktopPuppy/Reports/DailyReportPlan.cs`
- Create: `src/DesktopPuppy/Reports/DailyReportPlanner.cs`
- Create: `src/DesktopPuppy/Reports/DailyReportService.cs`
- Create: `src/DesktopPuppy/Reports/ReportNoveltyScorer.cs`
- Create: `src/DesktopPuppy/Reports/ReportNotificationService.cs`
- Create: `tests/DesktopPuppy.Tests/Reports/DailyReportPlannerTests.cs`
- Create: `tests/DesktopPuppy.Tests/Reports/ReportNoveltyScorerTests.cs`
- Create: `tests/DesktopPuppy.Tests/Reports/DailyReportServiceTests.cs`

**Interfaces:**
- `DailyReportFacts Build(localDate, cutoffLocal, sessions, interactions)` returns immutable local facts.
- `DailyReportPlanner.Create(facts, recentDisplayedReports)` chooses a theme, format, style and fixed cutoff without fabricating facts.
- `ReportNoveltyScorer.Score(candidate, recentDisplayedReports, plan)` rejects high similarity and ranks unused themes, formats, styles and openings.
- `DailyReportService.GenerateAsync(plan, cancellationToken)` uses the same candidate pool and Jev selection pipeline with a 200-500 character report profile.
- `ReportNotificationService.NotifySuccess(reportId)` and `NotifyFailure(reportId)` are non-blocking and suppressed while the pet is hidden or fullscreen.

- [ ] **Step 1: Write failing report-facts tests.**

Cover local-day cutoff at 18:20, foreground duration threshold greater than 30 minutes, midnight splitting, no activity, insufficient activity, and a report record that remains tied to its original local date and cutoff.

- [ ] **Step 2: Write failing novelty tests.**

Cover:

```text
theme is not reused within five prior report days
format is not reused within five prior report days
style is not repeated within the prior two report days
recent opening and n-gram similarity lower novelty score
low-data day produces no plan
same plan is returned for manual retry
```

- [ ] **Step 3: Write failing report service tests.**

Cover:

```text
18:20 generation uses 00:00-18:20 facts
late use after 18:20 cannot make a skipped report eligible
startup after missed schedule checks the original cutoff
multiple shutdown days skip days with insufficient activity
six candidates per DeepSeek request and four-valid threshold
200-500 character report profile
successful report stores AI prose and exact local facts separately
failed report offers manual retry and does not auto-retry on next startup
```

- [ ] **Step 4: Run RED.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Reports
```

- [ ] **Step 5: Implement the planner and report pipeline.**

Use recent displayed report summaries, not all raw report text, for novelty context. Keep the exact facts in the database and collapsed report UI; allow AI prose to be creative without treating it as a canonical statistical record.

- [ ] **Step 6: Run GREEN.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Reports
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj
```

- [ ] **Step 7: Commit.**

```text
git add src/DesktopPuppy/Reports tests/DesktopPuppy.Tests/Reports
git commit -m "feat: add novel scheduled daily reports"
```

## Task 12: Settings, tray commands, notifications and report UI

**Files:**
- Create: `src/DesktopPuppy/Settings/SettingsWindow.xaml`
- Create: `src/DesktopPuppy/Settings/SettingsWindow.xaml.cs`
- Create: `src/DesktopPuppy/Settings/SettingsViewModel.cs`
- Create: `src/DesktopPuppy/Settings/ApiKeyViewModel.cs`
- Create: `src/DesktopPuppy/Reports/ReportWindow.xaml`
- Create: `src/DesktopPuppy/Reports/ReportWindow.xaml.cs`
- Create: `src/DesktopPuppy/Reports/ReportHistoryViewModel.cs`
- Create: `src/DesktopPuppy/Notifications/WindowsNotificationService.cs`
- Modify: `src/DesktopPuppy/Pet/PetWindow.xaml`
- Modify: `src/DesktopPuppy/App.xaml.cs`
- Create: `tests/DesktopPuppy.Tests/Settings/SettingsViewModelTests.cs`
- Create: `tests/DesktopPuppy.Tests/Reports/ReportHistoryViewModelTests.cs`

**Interfaces:**
- `SettingsViewModel` exposes behavior, statistics, quiet hours, fullscreen, AI toggles, request settings, startup, key state, clear-history, delete-key and export commands.
- `ApiKeyViewModel` exposes only key presence and masked input state; it never exposes the decrypted key after save.
- `ReportWindow` displays AI prose and a collapsed local-facts panel, plus the last 15 local days and skip/failure statuses.
- `WindowsNotificationService` sends non-blocking success/failure notifications and exposes retry callbacks without blocking the Dispatcher.

- [ ] **Step 1: Write failing view-model tests.**

Cover:

```text
AI toggles persist
right-click AI toggle persists
entering a key saves only through the encrypted key service
key presence is shown without returning plaintext
clear history deletes operational history but keeps settings
delete API keys calls both provider-specific deletes
changing activity frequency updates scheduler
```

- [ ] **Step 2: Write failing report-history tests.**

Cover last-15-day listing, skip reasons, failure status, manual retry using the stored plan, expanded local facts and report date ordering.

- [ ] **Step 3: Run RED.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Settings
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~ReportHistory
```

- [ ] **Step 4: Implement settings, notifications and report UI.**

Use compact native WPF controls. Keep the AI prose prominent and exact facts collapsed. Provide tray commands for show/hide, pause/resume, settings, today’s report, clear history, delete keys and exit. Use the Windows system double-click setting and saved logical pet position.

- [ ] **Step 5: Run GREEN and manually verify the UI.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj
git diff --check
```

Manually verify keyboard navigation, DPI scaling, key masking, report notifications, report retry, report history, tray commands, bubble close buttons and clear-history confirmation.

- [ ] **Step 6: Commit.**

```text
git add src/DesktopPuppy/Settings src/DesktopPuppy/Reports src/DesktopPuppy/Notifications src/DesktopPuppy/Pet src/DesktopPuppy/App.xaml.cs tests/DesktopPuppy.Tests/Settings tests/DesktopPuppy.Tests/Reports
git commit -m "feat: add settings notifications and report history UI"
```

## Task 13: Runtime coordination, startup delay and graceful shutdown

**Files:**
- Create: `src/DesktopPuppy/Runtime/PowerPolicy.cs`
- Create: `src/DesktopPuppy/Runtime/BackgroundWorkCoordinator.cs`
- Create: `src/DesktopPuppy/Runtime/StartupDelayCoordinator.cs`
- Create: `src/DesktopPuppy/Runtime/ShutdownCoordinator.cs`
- Create: `src/DesktopPuppy/Runtime/SingleInstanceActivationService.cs`
- Create: `tests/DesktopPuppy.Tests/Runtime/PowerPolicyTests.cs`
- Create: `tests/DesktopPuppy.Tests/Runtime/StartupDelayTests.cs`
- Create: `tests/DesktopPuppy.Tests/Runtime/ShutdownCoordinatorTests.cs`

**Interfaces:**
- `PowerPolicy.ShouldAnimate(state, context)` suppresses animation during hidden, paused and fullscreen states.
- `BackgroundWorkCoordinator` schedules retention, 18:20 report checks, usage flushes and activity evaluation without blocking UI.
- `StartupDelayCoordinator.StartAsync(bool startupArgument, CancellationToken)` delays only `--startup` launches by 10 seconds; tray Show cancels the delay immediately.
- `ShutdownCoordinator.StopAsync()` stops hooks, closes sessions, flushes repositories, cancels AI, persists settings, disposes tray and closes the database.
- `SingleInstanceActivationService` forwards second-launch activation to the existing process.

- [ ] **Step 1: Write failing runtime tests.**

Cover:

```text
hidden and paused states do not animate
fullscreen suppresses new activity but does not cancel existing AI
startup argument waits 10 seconds
manual launch does not wait
tray Show cancels startup delay
tray is available during delay
shutdown cancels all pending requests
shutdown closes open sessions
shutdown flushes retention and settings before database close
```

- [ ] **Step 2: Run RED.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Runtime
```

- [ ] **Step 3: Implement background coordination and shutdown order.**

Initialize the database, tray, foreground watcher, fullscreen detector and usage collector immediately on `--startup`; show the pet after the delay. Keep AI requests independent across scenes and use linked cancellation only for application shutdown.

- [ ] **Step 4: Run GREEN and inspect idle behavior.**

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Runtime
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj
```

Leave the app idle for at least 15 minutes, inspect CPU activity, test fullscreen suppression, lock/unlock, sleep/wake, startup delay and exit during an in-flight request.

- [ ] **Step 5: Commit.**

```text
git add src/DesktopPuppy/Runtime tests/DesktopPuppy.Tests/Runtime
git commit -m "feat: add runtime coordination and graceful shutdown"
```

## Task 14: Composition root, end-to-end verification and privacy documentation

**Files:**
- Create: `tests/DesktopPuppy.Tests/EndToEnd/LocalModeScenarioTests.cs`
- Create: `tests/DesktopPuppy.Tests/EndToEnd/AiCandidateSelectionScenarioTests.cs`
- Create: `tests/DesktopPuppy.Tests/EndToEnd/ReportScenarioTests.cs`
- Create: `tests/DesktopPuppy.Tests/EndToEnd/PrivacyAndDeletionScenarioTests.cs`
- Create: `docs/testing/manual-test-matrix.md`
- Create: `docs/privacy/data-collection.md`
- Modify: `README.md`
- Modify: `src/DesktopPuppy/App.xaml.cs`

**Interfaces:**
- `App.xaml.cs` composes concrete adapters and services exactly once for the single process.
- End-to-end tests use fake DeepSeek and Jev providers to avoid network calls and API keys.

- [ ] **Step 1: Write failing end-to-end tests.**

Cover:

```text
local mode with no AI calls
right-click hybrid mode with six candidates and Jev selection
candidate-pool retry and five-attempt failure
Jev-only retry without regenerating candidates
hidden/fullscreen result recorded but not displayed
18:20 report with >30 minutes of foreground time
18:20 report skipped at <=30 minutes even if later usage exceeds threshold
startup catch-up for a missed eligible report
multiple shutdown days skip inactive days
report novelty rotates theme/form/style
clear-history removes all 15-day operational history
DPAPI key deletion removes both provider blobs
```

- [ ] **Step 2: Run RED.**

```text
dotnet test
```

Expected: failures because composition and end-to-end services are not wired.

- [ ] **Step 3: Compose the application.**

Register the clock, database, retention service, native adapters, usage collector, content validator, DeepSeek provider, Jev provider, candidate pool, activity engine, report planner, settings view models, tray, notification service and shutdown coordinator in `App.xaml.cs`. Use one shared application cancellation token and one database lifecycle.

- [ ] **Step 4: Write the manual test matrix.**

The matrix must include:

```text
single-instance activation
HKCU startup enable/disable and 10-second delay
multiple monitors, DPI and monitor removal
edge drag and saved position
single click, double-click hit, right-click special interaction and drag
persistent bubbles, replacement, late-result suppression and close buttons
fullscreen exclusive and borderless behavior
lock, sleep, wake and session boundaries
stable-title debounce and 15-day retention
DeepSeek JSON/timeout/empty/invalid/validation failure
Jev timeout/invalid selection/low confidence
18:20 report generation, skipped days and manual retry
report history and collapsed exact facts
DPAPI key presence, deletion and log redaction
clear-history and export
15-minute idle CPU inspection
```

- [ ] **Step 5: Document the privacy boundary.**

Document exactly what is stored locally, what is sent to DeepSeek and Jev, the 15-day retention rule, the fact that app names and window titles are sent, the fact that key blobs are DPAPI-protected, and the separate clear-history/delete-key operations.

- [ ] **Step 6: Run the complete verification suite.**

```text
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release --no-restore
git diff --check
```

Expected: Release build succeeds, all automated tests pass, no whitespace errors are reported, and the manual matrix has recorded results.

- [ ] **Step 7: Commit the MVP verification package.**

```text
git add src tests docs README.md
git commit -m "chore: document desktop puppy MVP verification"
```

## Final completion checklist

- [ ] Release build succeeds on the supported Windows target.
- [ ] All automated tests pass with zero failures.
- [ ] The application is single-instance and startup registration is user-scoped.
- [ ] The pet restores position, survives monitor changes and remains partially visible.
- [ ] Single click, double-click hit, right-click special interaction and drag work.
- [ ] Persistent bubbles can be individually closed and follow the pet.
- [ ] Fullscreen hides all pets and pauses new random activities while statistics continue.
- [ ] Lock, sleep and hibernate close foreground sessions.
- [ ] Window titles require two seconds of stability and are retained for 15 local days.
- [ ] There is no daily activity or AI request cap.
- [ ] DeepSeek candidate pools use six candidates per serial request and require four valid candidates.
- [ ] Jev uses `jev-latest`, local tie-breaking and five independent selection attempts.
- [ ] AI failures do not use fallback; failure notifications are non-blocking and report retry is manual.
- [ ] Right-click placeholders expire after 15 seconds and late results are not displayed.
- [ ] Daily reports run at 18:20, use the 30-minute cutoff threshold and rotate novelty dimensions.
- [ ] Daily report history covers the last 15 local days.
- [ ] API keys are DPAPI-encrypted before SQLite storage and absent from logs, prompts and source control.
- [ ] Clear-history removes operational history while separate actions manage settings and keys.
- [ ] Idle rendering stops and background work remains bounded.
- [ ] No secrets or generated personal usage data are committed.
