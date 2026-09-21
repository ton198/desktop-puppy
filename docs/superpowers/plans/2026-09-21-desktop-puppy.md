# Desktop Puppy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Build a personal Windows desktop puppy that is a mischievous always-on-top companion with local usage statistics, daily humorous reports, optional DeepSeek-generated dialogue, and strict low-power behavior.

**Architecture:** Use one C# WPF process. WPF owns UI and animation surfaces; focused Win32 PInvoke adapters own transparent-window behavior and foreground/fullscreen events; SQLite stores local usage and content history; a local behavior engine decides when the puppy acts; DeepSeek only generates or rewrites candidate lines inside a validated scene and never controls behavior.

**Tech Stack:** C#, current .NET LTS, WPF, Win32 PInvoke, SQLite, PNG sprite frames, HttpClient, Windows protected local storage, System.Text.Json, and xUnit.

**Spec:** docs/superpowers/specs/2026-09-21-desktop-puppy-design.md

## Global Constraints

- The application is for personal, non-commercial use.
- Do not use Electron, Tauri, WebView, HTML, CSS, or JavaScript as the UI technology.
- The default personality is mischievous and teasing, with occasional genuine concern.
- Use hybrid AI mode: local content for high-frequency reactions; DeepSeek for selected greetings, important activities, and daily reports.
- DeepSeek is the first provider and deepseek-flash is the default model.
- The behavior engine, cooldowns, schedules, and animation choices remain local and deterministic.
- The AI provider must never block the WPF UI thread.
- If the API key is absent or any network/content validation step fails, fall back to local content without an error modal.
- Do not collect keystrokes, mouse trajectories, screenshots, document contents, browser page bodies, or clipboard contents.
- Store usage and content history locally; do not add a project-owned cloud backend.
- Do not write production code until a corresponding failing test has been written and observed.
- Keep rendering event-driven: no continuous high-FPS loop while idle.
- Never put API keys in source control, logs, ordinary plaintext settings, or test fixtures.

## Confirmed requirements and decisions

### Product behavior

- Personality: B, mischievous/troll-like.
- The puppy can initiate low-frequency activities, tease the user, and react to desktop usage.
- The puppy must remain configurable and must not become continuously disruptive.
- Required interactions include transparent desktop presence, click/drag/hover, hit reaction, daily greeting, random activities, local background statistics, and daily humorous reporting.

### AI behavior

- AI mode: B, hybrid.
- Local hand-written lines handle frequent click, hit, and basic reactions.
- DeepSeek handles selected greetings, important activities, user-requested extra lines, and daily reports.
- AI does not decide when a behavior occurs.
- Candidate generation returns multiple options; local validators select one.
- Persona rules, prohibited phrases, normalized text, n-gram similarity, recent history, and style cooldowns reduce AI-like repetition.
- Daily report numbers and facts are calculated locally; the model can only turn those facts into prose.
- Missing key, timeout, invalid JSON, request limit, provider error, or rejected content must degrade to local content.

### Technology

- C# with the current .NET LTS.
- WPF for all UI.
- Win32 PInvoke for window styles, transparent/always-on-top behavior, foreground events, fullscreen detection, tray, work areas, startup, and click-through behavior.
- SQLite for local persistence.
- PNG sprite frames or independent PNG frames for animation.
- One process for the first phase; no Windows Service.

---

## Task 1: Repository and WPF application skeleton

**Files:**
- Create: DesktopPuppy.sln
- Create: src/DesktopPuppy/DesktopPuppy.csproj
- Create: src/DesktopPuppy/App.xaml
- Create: src/DesktopPuppy/App.xaml.cs
- Create: src/DesktopPuppy/MainWindow.xaml
- Create: src/DesktopPuppy/MainWindow.xaml.cs
- Create: tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj
- Create: tests/DesktopPuppy.Tests/SmokeTests.cs
- Modify: .gitignore
- Modify: README.md

**Interfaces:**
- ApplicationInfo.ProductName is a public constant with value Desktop Puppy.
- The WPF project targets Windows and enables nullable reference types.
- The test project can run without launching a WPF window.

- [ ] Step 1: Write the failing smoke test.

Create a test that references the application assembly and asserts ApplicationInfo.ProductName equals Desktop Puppy. The type does not exist yet, so the test must fail because the symbol is missing.

- [ ] Step 2: Run RED.

Run:
~~~text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --no-restore
~~~
Expected: FAIL because ApplicationInfo is not defined.

- [ ] Step 3: Create the minimal WPF solution and implementation.

Create the Windows-targeted WPF project, define ApplicationInfo.ProductName, and keep MainWindow as a minimal borderless placeholder. Do not add product behavior in this task.

- [ ] Step 4: Run GREEN.

Run the focused test and then the complete test project. Expected: all tests pass with no warnings introduced by the new project.

- [ ] Step 5: Commit.

~~~text
git add DesktopPuppy.sln src tests .gitignore README.md
git commit -m "chore: scaffold WPF desktop puppy"
~~~

## Task 2: Domain events, settings, and clock abstractions

**Files:**
- Create: src/DesktopPuppy/Core/Clock/IClock.cs
- Create: src/DesktopPuppy/Core/Clock/SystemClock.cs
- Create: src/DesktopPuppy/Core/Events/PuppyEvent.cs
- Create: src/DesktopPuppy/Core/Events/PuppyEventType.cs
- Create: src/DesktopPuppy/Core/Settings/AppSettings.cs
- Create: src/DesktopPuppy/Core/Settings/ActivitySettings.cs
- Create: src/DesktopPuppy/Core/Settings/AiSettings.cs
- Create: tests/DesktopPuppy.Tests/Core/SettingsTests.cs

**Interfaces:**
- IClock.UtcNow returns DateTimeOffset.
- PuppyEvent contains PuppyEventType Type, DateTimeOffset OccurredAt, and a read-only context dictionary.
- AppSettings contains local behavior, statistics, sound, fullscreen, and AI options.
- AiSettings contains Enabled, Provider, Model, DailyRequestLimit, and QuietHoursEnabled; it does not contain the plaintext API key.

- [ ] Step 1: Write failing settings tests.

Test that defaults select hybrid AI mode, provider DeepSeek, model deepseek-flash, local statistics enabled, sounds disabled, and the proposed quiet-hours default. Test that a fixed clock can replace the system clock.

- [ ] Step 2: Run RED.

Run:
~~~text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~SettingsTests
~~~
Expected: FAIL because the settings and clock types are missing.

- [ ] Step 3: Implement the minimal domain types.

Use immutable records or classes with explicit defaults from the spec. Keep API-key storage out of plain settings objects.

- [ ] Step 4: Run GREEN.

Run the focused tests and all tests. Expected: all tests pass.

- [ ] Step 5: Commit.

~~~text
git add src/DesktopPuppy/Core tests/DesktopPuppy.Tests/Core
git commit -m "feat: add puppy domain settings and events"
~~~

## Task 3: Local SQLite storage and migrations

**Files:**
- Create: src/DesktopPuppy/Storage/DatabaseConnectionFactory.cs
- Create: src/DesktopPuppy/Storage/DatabaseMigrator.cs
- Create: src/DesktopPuppy/Storage/Repositories/UsageRepository.cs
- Create: src/DesktopPuppy/Storage/Repositories/ContentHistoryRepository.cs
- Create: src/DesktopPuppy/Storage/Repositories/InteractionRepository.cs
- Create: src/DesktopPuppy/Storage/Models/UsageSessionRecord.cs
- Create: src/DesktopPuppy/Storage/Models/ContentHistoryRecord.cs
- Create: src/DesktopPuppy/Storage/Models/InteractionRecord.cs
- Create: tests/DesktopPuppy.Tests/Storage/DatabaseMigratorTests.cs
- Create: tests/DesktopPuppy.Tests/Storage/RepositoryTests.cs

**Interfaces:**
- DatabaseMigrator.Migrate(connection) creates schema version 1.
- UsageRepository.AppendSession(record) persists a closed foreground session.
- ContentHistoryRepository.Add(record) and GetRecent(since) support duplicate prevention.
- InteractionRepository.Add(record) persists hits, clicks, activities, and AI outcomes.

- [ ] Step 1: Write failing migration and repository tests.

Use a temporary SQLite database. Assert that migration creates schema_version, usage_sessions, content_history, and interactions. Assert that a usage session can be inserted and read back with the same application name, start, end, and duration.

- [ ] Step 2: Run RED.

Run:
~~~text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Storage
~~~
Expected: FAIL because the database types and schema do not exist.

- [ ] Step 3: Implement minimal SQLite storage.

Use parameterized SQL only. Add indexes for usage date and content-history timestamp. Keep writes explicit so later batching can wrap multiple inserts in one transaction.

- [ ] Step 4: Run GREEN.

Run focused storage tests and all tests. Expected: no database files are created inside the repository.

- [ ] Step 5: Commit.

~~~text
git add src/DesktopPuppy/Storage tests/DesktopPuppy.Tests/Storage
git commit -m "feat: add local sqlite storage"
~~~

## Task 4: Native Windows integration

**Files:**
- Create: src/DesktopPuppy/Native/INativeWindowService.cs
- Create: src/DesktopPuppy/Native/Win32WindowService.cs
- Create: src/DesktopPuppy/Native/IForegroundWindowWatcher.cs
- Create: src/DesktopPuppy/Native/WinEventForegroundWatcher.cs
- Create: src/DesktopPuppy/Native/IFullscreenDetector.cs
- Create: src/DesktopPuppy/Native/Win32FullscreenDetector.cs
- Create: src/DesktopPuppy/Native/SystemTrayService.cs
- Create: src/DesktopPuppy/Native/StartupRegistrationService.cs
- Create: tests/DesktopPuppy.Tests/Native/FullscreenDetectorTests.cs
- Create: tests/DesktopPuppy.Tests/Native/ForegroundSessionTests.cs

**Interfaces:**
- INativeWindowService.ConfigurePetWindow(window, options) applies borderless, transparency, topmost, hit-test, and work-area behavior.
- IForegroundWindowWatcher.Start(callback) and Stop() manage native foreground events.
- IFullscreenDetector.IsFullscreen(window) returns whether a foreground application occupies the active monitor.

- [ ] Step 1: Write failing tests for session transitions and fullscreen decisions.

Test a pure ForegroundSessionTracker helper with a fake clock: starting an application creates an open session, changing applications closes the previous session, and a duplicate foreground event does not create a zero-length extra session. Test fullscreen decision logic with supplied monitor rectangles rather than requiring a real desktop in unit tests.

- [ ] Step 2: Run RED.

Run:
~~~text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Native
~~~
Expected: FAIL because the tracker and native abstractions are not defined.

- [ ] Step 3: Implement the pure tracker and Win32 adapters.

Use Win32 event hooks for foreground changes with a low-frequency fallback timer. Ensure hook handles and delegates are released on Stop. Keep PInvoke declarations at the native boundary; do not spread raw calls across UI code.

- [ ] Step 4: Run GREEN and perform a manual native check.

Run focused and complete tests. Manually launch the WPF shell and verify that a foreground-change callback fires when switching between two applications.

- [ ] Step 5: Commit.

~~~text
git add src/DesktopPuppy/Native tests/DesktopPuppy.Tests/Native
git commit -m "feat: add native windows integration boundaries"
~~~

## Task 5: Pet window, animation state machine, and interactions

**Files:**
- Create: src/DesktopPuppy/Pet/PetWindow.xaml
- Create: src/DesktopPuppy/Pet/PetWindow.xaml.cs
- Create: src/DesktopPuppy/Pet/Animation/AnimationClip.cs
- Create: src/DesktopPuppy/Pet/Animation/AnimationState.cs
- Create: src/DesktopPuppy/Pet/Animation/AnimationController.cs
- Create: src/DesktopPuppy/Pet/Interaction/InteractionController.cs
- Create: src/DesktopPuppy/Pet/Interaction/HitReactionService.cs
- Create: src/DesktopPuppy/Pet/Interaction/PetInteractionEvent.cs
- Create: tests/DesktopPuppy.Tests/Pet/AnimationControllerTests.cs
- Create: tests/DesktopPuppy.Tests/Pet/HitReactionServiceTests.cs
- Create: assets/pet/.gitkeep

**Interfaces:**
- AnimationController.Play(clip) starts a finite clip and raises Completed.
- AnimationController.SetIdle(state) selects the idle state without a permanent high-FPS loop.
- HitReactionService.RegisterHit(occurredAt) returns whether a reaction is allowed after cooldown.
- InteractionController publishes click, double-click, hover, drag, and hit events.

- [ ] Step 1: Write failing animation and hit-cooldown tests.

Test that a hit is accepted when no recent hit exists, rejected during cooldown, and accepted after cooldown. Test that the controller transitions idle -> hit -> idle after the clip duration.

- [ ] Step 2: Run RED.

Run:
~~~text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Pet
~~~
Expected: FAIL because the animation and hit services do not exist.

- [ ] Step 3: Implement the state machine and WPF rendering surface.

Use image-frame swapping or WriteableBitmap updates. Start a dispatcher timer only while an animation clip is active; stop it on completion. Configure the pet window through the native adapter. Do not add blur or heavy effects.

- [ ] Step 4: Run GREEN and perform a manual animation check.

Run focused and complete tests. Manually verify drag, hit, idle-to-hit-to-idle, and multi-monitor positioning.

- [ ] Step 5: Commit.

~~~text
git add src/DesktopPuppy/Pet assets/pet tests/DesktopPuppy.Tests/Pet
git commit -m "feat: add pet window animation and hit reactions"
~~~

## Task 6: Local content library, persona rules, and duplicate prevention

**Files:**
- Create: src/DesktopPuppy/Content/PersonaProfile.cs
- Create: src/DesktopPuppy/Content/SceneCard.cs
- Create: src/DesktopPuppy/Content/LocalPhraseBank.cs
- Create: src/DesktopPuppy/Content/ContentCandidate.cs
- Create: src/DesktopPuppy/Content/ContentValidator.cs
- Create: src/DesktopPuppy/Content/ContentSelector.cs
- Create: src/DesktopPuppy/Content/Similarity/NormalizedText.cs
- Create: src/DesktopPuppy/Content/Similarity/NgramSimilarity.cs
- Create: src/DesktopPuppy/Content/Phrases/seed-phrases.json
- Create: tests/DesktopPuppy.Tests/Content/ContentValidatorTests.cs
- Create: tests/DesktopPuppy.Tests/Content/ContentSelectorTests.cs
- Create: tests/DesktopPuppy.Tests/Content/NgramSimilarityTests.cs

**Interfaces:**
- SceneCard carries event type, application context, time context, mood, humor style, maximum length, and allowed facts.
- ContentValidator.Validate(candidate, scene, recent) returns a validation result with reasons.
- ContentSelector.Select(candidates, scene, recent) returns the best accepted candidate or null.

- [ ] Step 1: Write failing validator tests.

Cover empty text, prohibited AI phrases, Markdown/multi-paragraph output, unsupported facts, exact duplicates, high n-gram similarity, and acceptance of a short concrete mischievous line.

- [ ] Step 2: Run RED.

Run:
~~~text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Content
~~~
Expected: FAIL because the validator and selector are not defined.

- [ ] Step 3: Implement the local content pipeline.

Add persona rules, prohibited phrases, text normalization, n-gram threshold, recent-style tracking, and hand-written seed phrases. The validator only accepts or rejects; it never silently rewrites a candidate.

- [ ] Step 4: Run GREEN.

Run focused and complete tests. Use a fixed random seed in selector tests so they are deterministic.

- [ ] Step 5: Commit.

~~~text
git add src/DesktopPuppy/Content tests/DesktopPuppy.Tests/Content
git commit -m "feat: add local persona content pipeline"
~~~

## Task 7: DeepSeek provider and secure API-key settings

**Files:**
- Create: src/DesktopPuppy/Ai/IAiContentProvider.cs
- Create: src/DesktopPuppy/Ai/DeepSeek/DeepSeekContentProvider.cs
- Create: src/DesktopPuppy/Ai/DeepSeek/DeepSeekRequest.cs
- Create: src/DesktopPuppy/Ai/DeepSeek/DeepSeekResponse.cs
- Create: src/DesktopPuppy/Ai/AiContentService.cs
- Create: src/DesktopPuppy/Security/IApiKeyStore.cs
- Create: src/DesktopPuppy/Security/WindowsApiKeyStore.cs
- Create: src/DesktopPuppy/Settings/SettingsRepository.cs
- Create: tests/DesktopPuppy.Tests/Ai/DeepSeekContentServiceTests.cs
- Create: tests/DesktopPuppy.Tests/Security/ApiKeyStoreTests.cs

**Interfaces:**
- IAiContentProvider.GenerateCandidatesAsync(scene, cancellationToken) returns structured candidates.
- IApiKeyStore.SaveAsync(provider, apiKey), GetAsync(provider), and DeleteAsync(provider) never expose the key to logs.
- AiContentService.GenerateOrFallbackAsync(scene, cancellationToken) returns an accepted candidate or local fallback.

- [ ] Step 1: Write failing service tests without real network calls.

Use a fake provider to test successful validation, timeout fallback, malformed candidate fallback, missing-key fallback, and daily-limit fallback. Use an in-memory fake key store for save/get/delete semantics.

- [ ] Step 2: Run RED.

Run:
~~~text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Ai
~~~
Expected: FAIL because provider boundaries and service do not exist.

- [ ] Step 3: Implement the provider and secure storage.

Use a shared HttpClient factory, short timeout, cancellation tokens, structured JSON output, explicit response-size limits, and deepseek-flash as the default model. Use Windows-protected local storage for the key. Redact credentials from exceptions and logs.

- [ ] Step 4: Run GREEN.

Run focused and complete tests. Add only an opt-in manual test path that receives a key from a runtime environment variable; never commit a key or place one in a test file.

- [ ] Step 5: Commit.

~~~text
git add src/DesktopPuppy/Ai src/DesktopPuppy/Security src/DesktopPuppy/Settings tests/DesktopPuppy.Tests/Ai tests/DesktopPuppy.Tests/Security
git commit -m "feat: add optional deepseek content provider"
~~~

## Task 8: Usage collection and daily aggregation

**Files:**
- Create: src/DesktopPuppy/Usage/ForegroundSessionTracker.cs
- Create: src/DesktopPuppy/Usage/UsageCollector.cs
- Create: src/DesktopPuppy/Usage/IdleTimeService.cs
- Create: src/DesktopPuppy/Usage/ApplicationClassifier.cs
- Create: src/DesktopPuppy/Usage/DailyUsageAggregator.cs
- Create: tests/DesktopPuppy.Tests/Usage/ForegroundSessionTrackerTests.cs
- Create: tests/DesktopPuppy.Tests/Usage/DailyUsageAggregatorTests.cs
- Create: tests/DesktopPuppy.Tests/Usage/ApplicationClassifierTests.cs

**Interfaces:**
- ForegroundSessionTracker.OnForegroundApplicationChanged(application, at) closes the prior session and starts the next one.
- DailyUsageAggregator.Build(day, sessions, interactions) returns immutable report facts.
- ApplicationClassifier.Classify(processName) returns Work, Entertainment, Utility, or Unknown using local rules.

- [ ] Step 1: Write failing usage tests.

Test session closing, duplicate foreground events, idle-gap exclusion, midnight splitting, application totals, longest continuous session, work/entertainment ratio, hit count, and activity count.

- [ ] Step 2: Run RED.

Run:
~~~text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Usage
~~~
Expected: FAIL because collector and aggregator are not defined.

- [ ] Step 3: Implement collection and aggregation.

Use the native foreground watcher, exclude Desktop Puppy itself from usage totals, treat the configured idle threshold as inactive time, close open sessions on shutdown, and flush batched records periodically. Store process names locally; do not store window titles by default.

- [ ] Step 4: Run GREEN and perform a manual session check.

Run focused and complete tests. Manually switch between two applications, lock/unlock the workstation, leave the computer idle, and verify the sessions.

- [ ] Step 5: Commit.

~~~text
git add src/DesktopPuppy/Usage tests/DesktopPuppy.Tests/Usage
git commit -m "feat: collect and aggregate local usage statistics"
~~~

## Task 9: Activity scheduler, greetings, and daily reports

**Files:**
- Create: src/DesktopPuppy/Activities/ActivityDefinition.cs
- Create: src/DesktopPuppy/Activities/ActivityScheduler.cs
- Create: src/DesktopPuppy/Activities/CooldownStore.cs
- Create: src/DesktopPuppy/Activities/GreetingService.cs
- Create: src/DesktopPuppy/Reports/DailyReportFacts.cs
- Create: src/DesktopPuppy/Reports/DailyReportService.cs
- Create: src/DesktopPuppy/Reports/LocalReportFallback.cs
- Create: tests/DesktopPuppy.Tests/Activities/ActivitySchedulerTests.cs
- Create: tests/DesktopPuppy.Tests/Activities/GreetingServiceTests.cs
- Create: tests/DesktopPuppy.Tests/Reports/DailyReportServiceTests.cs

**Interfaces:**
- ActivityScheduler.TrySchedule(context) returns an activity decision or no-op due to cooldown, quiet hours, fullscreen, or daily limit.
- GreetingService.GetGreeting(context) returns a local or AI-backed line while consulting content history.
- DailyReportService.BuildAsync(facts, cancellationToken) returns a validated report or local fallback.

- [ ] Step 1: Write failing scheduler and report tests.

Test global cooldown, per-activity cooldown, daily maximum, quiet hours, fullscreen suppression, once-per-day greeting, and report-fact preservation. Test that a report cannot change a duration or application name supplied in facts.

- [ ] Step 2: Run RED.

Run:
~~~text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Activities
~~~
Expected: FAIL because scheduler and report services are not defined.

- [ ] Step 3: Implement local behavior and orchestration.

Keep trigger decisions local and deterministic. Build scene cards, ask the AI service only when hybrid policy allows it, persist accepted lines, and use local seed phrases when a request fails or is suppressed.

- [ ] Step 4: Run GREEN.

Run focused and complete tests. Use an injected clock to verify greeting and report rollover without waiting for a real date boundary.

- [ ] Step 5: Commit.

~~~text
git add src/DesktopPuppy/Activities src/DesktopPuppy/Reports tests/DesktopPuppy.Tests/Activities tests/DesktopPuppy.Tests/Reports
git commit -m "feat: add activities greetings and daily reports"
~~~

## Task 10: Settings UI, report window, and tray controls

**Files:**
- Create: src/DesktopPuppy/Settings/SettingsWindow.xaml
- Create: src/DesktopPuppy/Settings/SettingsWindow.xaml.cs
- Create: src/DesktopPuppy/Settings/SettingsViewModel.cs
- Create: src/DesktopPuppy/Reports/ReportWindow.xaml
- Create: src/DesktopPuppy/Reports/ReportWindow.xaml.cs
- Modify: src/DesktopPuppy/Pet/PetWindow.xaml
- Modify: src/DesktopPuppy/App.xaml.cs
- Create: tests/DesktopPuppy.Tests/Settings/SettingsViewModelTests.cs

**Interfaces:**
- The settings view model exposes bindable properties for behavior, statistics, quiet hours, AI, key state, request limits, and data deletion.
- The report window consumes DailyReportFacts and a validated report result; it does not calculate usage numbers itself.

- [ ] Step 1: Write failing view-model tests.

Test that changing the AI toggle persists, entering a key updates only the secure key store, deleting history calls the storage boundary, and changing activity frequency updates scheduler configuration.

- [ ] Step 2: Run RED.

Run:
~~~text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Settings
~~~
Expected: FAIL because the view model is not defined.

- [ ] Step 3: Implement WPF settings and reports.

Add compact native WPF controls. Add tray commands for show/hide, pause, settings, today’s report, reset data, and exit. Mask API key input and never bind the actual key into a visible text property after save.

- [ ] Step 4: Run GREEN and manually verify the UI.

Run all tests. Manually verify keyboard navigation, DPI scaling, settings persistence, tray commands, report display, and key deletion.

- [ ] Step 5: Commit.

~~~text
git add src/DesktopPuppy/Settings src/DesktopPuppy/Reports src/DesktopPuppy/Pet src/DesktopPuppy/App.xaml.cs tests/DesktopPuppy.Tests/Settings
git commit -m "feat: add settings report and tray controls"
~~~

## Task 11: Low-power behavior and graceful shutdown

**Files:**
- Create: src/DesktopPuppy/Runtime/PowerPolicy.cs
- Create: src/DesktopPuppy/Runtime/BackgroundWorkCoordinator.cs
- Create: src/DesktopPuppy/Runtime/ShutdownCoordinator.cs
- Create: tests/DesktopPuppy.Tests/Runtime/PowerPolicyTests.cs
- Create: tests/DesktopPuppy.Tests/Runtime/ShutdownCoordinatorTests.cs

**Interfaces:**
- PowerPolicy.ShouldAnimate(state, context) controls active rendering.
- BackgroundWorkCoordinator schedules collection flushes, activity evaluation, and report generation without blocking UI.
- ShutdownCoordinator.StopAsync() stops hooks, flushes usage sessions, persists settings, and closes the database.

- [ ] Step 1: Write failing power and shutdown tests.

Test no animation work while idle, fullscreen suppression, shutdown closing open sessions, and cancellation of pending AI requests.

- [ ] Step 2: Run RED.

Run:
~~~text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --filter FullyQualifiedName~Runtime
~~~
Expected: FAIL because runtime services are not defined.

- [ ] Step 3: Implement low-power coordination.

Use event-driven wakeups for animations and foreground changes, bounded timers for periodic work, cancellation tokens for shutdown, and one final transactional flush. Network and database work must not run on the WPF dispatcher thread.

- [ ] Step 4: Run GREEN and inspect idle behavior.

Run all tests. Leave the app idle for at least 15 minutes and inspect Task Manager or Performance Monitor for continuous CPU activity. Test fullscreen suppression and suspend/resume.

- [ ] Step 5: Commit.

~~~text
git add src/DesktopPuppy/Runtime tests/DesktopPuppy.Tests/Runtime
git commit -m "feat: add low-power runtime coordination"
~~~

## Task 12: End-to-end verification and privacy documentation

**Files:**
- Create: tests/DesktopPuppy.Tests/EndToEnd/LocalModeScenarioTests.cs
- Create: tests/DesktopPuppy.Tests/EndToEnd/AiFallbackScenarioTests.cs
- Create: docs/testing/manual-test-matrix.md
- Create: docs/privacy/data-collection.md
- Modify: README.md

**Interfaces:**
- End-to-end tests exercise local mode, hybrid mode with a fake provider, AI timeout fallback, daily rollover, repeated-line rejection, fullscreen suppression, and data deletion.

- [ ] Step 1: Write failing scenario tests.

Cover a local-mode day, a hybrid-mode day with accepted candidates, timeout fallback, invalid JSON fallback, a repeated sentence rejected by history, fullscreen suppression, and clear-history behavior.

- [ ] Step 2: Run RED.

Run:
~~~text
dotnet test
~~~
Expected: FAIL until all previous services are composed in the application host.

- [ ] Step 3: Wire the composition root and write manual checks.

Register dependencies in App.xaml.cs. Add the manual matrix for multi-monitor, DPI, startup, tray, idle, fullscreen, audio, API-key deletion, long-running stability, and content quality. Document exactly what is collected and what is not.

- [ ] Step 4: Run the full verification suite.

Run:
~~~text
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release --no-restore
git diff --check
~~~
Expected: Release build succeeds, all tests pass, and git diff --check reports no whitespace errors. Complete the manual matrix and record results.

- [ ] Step 5: Commit the MVP verification package.

~~~text
git add src tests docs README.md
git commit -m "chore: document desktop puppy MVP verification"
~~~

## Final completion checklist

- [ ] Release build succeeds.
- [ ] All automated tests pass with zero failures.
- [ ] Multi-monitor and DPI checks pass.
- [ ] The pet can be dragged, clicked, hit, hidden, paused, and exited.
- [ ] Activity cooldowns, quiet hours, and fullscreen suppression work.
- [ ] Greetings do not repeat within the configured history window.
- [ ] Usage totals survive restart and shutdown flush.
- [ ] Reports preserve all locally calculated facts.
- [ ] AI fallback works for missing key, timeout, invalid JSON, request limit, and rejected content.
- [ ] The API key is absent from logs, ordinary config, crash output, and source control.
- [ ] Idle rendering stops and background work is bounded.
- [ ] Data deletion removes usage, interactions, content history, and stored settings as specified.
- [ ] No secrets or generated personal usage data are committed.
