# MVP completion execution ledger

Source: user-approved seven-milestone plan in this chat, 2026-10-09.

Latest regression follow-up: user authorized testing; fresh 365/365 deterministic tests and 32/32 complete published-app UI/E2E passed. The post-display-fix UI gap is closed. See `docs/verification/2026-10-13-post-fix-regression.md`; operator-driven OS/hardware acceptance remains pending. Earlier non-disruptive-only and post-fix-UI-pending notes below are historical.

Latest follow-up: user requests non-disruptive checks only. AI display facts now include actual report views and use actual display date/cutoff; 3 regression tests confirmed RED then complete 365-test suite passed. Published the fix and packaged the manual acceptance controller without launching either. Previous 32 UI/E2E tests remain the pre-fix baseline; post-fix UI and operator transitions are pending. See `docs/verification/2026-10-13-remaining-acceptance.md`.

- [x] Baseline implementation, event-driven animations, edge grab anchoring, tray discovery diagnosis.
- [x] Unified settings, runtime state and placement persistence implementation.
- [x] User-scoped startup, delayed reveal and lifecycle coordination implementation.
- [x] Extended statistics, classified usage, retention, safe CSV and history clearing implementation.
- [x] Scene-aware AI, one daily greeting and deterministic activity scheduling implementation.
- [x] Fixed-cutoff daily reports, planning, notifications and history UI implementation.
- [x] Integrated automated verification and explicit manual acceptance record.
- [ ] Operator-driven real login/fullscreen/lock/sleep/DPI/unplug/Explorer/Excel acceptance and user visual approval.

2026-10-13 checkpoint: 362 unit/database/HTTP tests passed; published EXE passed all 32 regular WPF UI / long-running statistics refresh E2E tests in one complete run; 15:03 real idle frame scheduling test passed separately. All five scenes passed real DeepSeek/Jev service acceptance with synthetic context. Published app in `artifacts/mvp` has the same DLL SHA-256 as the UI-tested app. See `docs/verification/2026-10-13-mvp-acceptance.md` for exact evidence and unrun operator items. Do not describe all real Windows acceptance as complete.

Ruling: preserve and extend the original checkout without staging or committing pre-existing changes. A binary pre-change diff is saved in ignored `.local/mvp-execution/preexisting.patch`; existing untracked assets remain untouched. Native worktrees start from committed HEAD and would omit the approved uncommitted visual baseline. No merge/push will be performed.

Ruling: independent domain and UI services are delegated under subagent-driven-development; App/PetWindow/tray integration remains with the root worker to prevent overlapping edits. Build/test execution is serialized.

Contract: ai_settings is the single authority for existing AI enabled/right-click settings; unified settings migration must preserve it and encrypted keys. Newly added historical tables participate in transactional history deletion and 15-local-day retention. Clear-history first drains/cancels writers, then clears, then reopens tracking.

Manual lock/sleep/display removal/Explorer restart and real login are operator-driven. No simulated pass will be reported as real acceptance.
