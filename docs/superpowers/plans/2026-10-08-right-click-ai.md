# Right-click AI implementation ledger

Source: user-approved plan in this chat, 2026-10-08.

- [x] Configuration, DPAPI keys, SQLite history and retention.
- [x] DeepSeek generation, validation, Jev selection and retry contracts.
- [x] Single-bubble lifecycle and right-click integration.
- [x] Fullscreen suppression and independent code review.
- [x] Final desktop regression and handoff record (14/14 Category=Ui; private DB fixtures).

Defaults: right-click 10–80 text elements; 15-second display window; one in-flight request; deep night 23:00–08:00; original spec superseded only for single-bubble behavior.
Ruling: Work in a managed worktree from 726d3da; preserve main and existing untracked directories. No automatic push or merge.
Ruling: Pure content validation uses explicit syntactic rules and duplicate heuristics; it cannot prove factual truth of generated language. Grounding is also enforced in prompts and assessed during live acceptance.

Validation defaults: NFKC letter/digit normalization with bigram Dice similarity >= 0.75 rejected; six explicit humor styles; at most one Unicode OtherSymbol; punctuation runs of three rejected. These are conservative initial heuristics, not a semantic truth guarantee.
Final review: two P2 lifecycle findings reproduced by four failing regression cases, then fixed. Notifications obey the same request ownership/deadline as results; placeholder visibility is tracked separately from request ownership so preflight cannot erase prior speech.
Evidence: 260/260 unit tests (including actual 8-second timeout cancellation); clean Release build 0 errors / 2 WFO0003 DPI-manifest warnings. Online provider calls and real Shell/power/mixed-DPI/fullscreen acceptance are Not run.
Final Category=Ui: 14/14 passed in 25 seconds. One earlier discovery timeout passed on isolated and full reruns; its cause is unproven. No blanket claim of UI startup stability is made.
