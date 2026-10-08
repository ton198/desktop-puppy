# Current-parameter live AI stage timing — 2026-10-08

Measured the current production request contract without changing model, thinking settings, prompt, six-candidate requirement, validation, Jev selection, retries or deadlines. Timing wrappers were added only to the local, disposable WPF fixture. Three fresh processes and fresh isolated databases were used; the HTTP connection starts cold in each sample. Real configured keys were copied as DPAPI ciphertext and removed from each fixture database in its finally block. The original user database was not modified by the test.

The fixture triggers the actual pet's routed right-button event, uses the production coordinator and providers, and timestamps the real view's text update. The context is a fixed integration-test app/title with empty recent history, rather than private foreground data. The click-to-bubble metric excludes fixture startup, screenshot capture, history polling and cleanup; it timestamps bubble text assignment, not a physical display-present signal. Physical mouse input and production App/tray/fullscreen integration remain outside this fixture.

| Stage (milliseconds) | Sample 1 | Sample 2 | Sample 3 |
| --- | ---: | ---: | ---: |
| Click to DeepSeek provider start: configuration/context/storage scheduling | 32.376 | 30.625 | 32.113 |
| DeepSeek provider, including HTTP and parsing | 6152.284 | 6239.246 | 5697.071 |
| Local validation/candidate persistence/scheduling gap before Jev | 647.084 | 19.984 | 26.929 |
| Jev provider, including HTTP and parsing | 223.617 | 224.221 | 248.494 |
| Decision completion to real bubble text update | 6.522 | 6.006 | 13.924 |
| **Click to bubble total** | **7061.882** | **6520.081** | **6018.531** |

Median total: **6520.081 ms**, range **6018.531–7061.882 ms**. These three samples are not a percentile SLA or a two-second guarantee. Each produced six accepted candidates, one DeepSeek attempt, one Jev attempt, one request despite a duplicate in-flight right click, status `Displayed`, and one display-history row matching the visible bubble.

DeepSeek breakdown (nested within its provider duration; do not add twice):

| Measurement | Sample 1 | Sample 2 | Sample 3 |
| --- | ---: | ---: | ---: |
| Request send to response headers, ms | 524.219 | 491.475 | 606.468 |
| Headers to complete response body, ms | 5607.036 | 5723.528 | 5071.025 |
| Completion tokens | 1316 | 1172 | 1012 |
| Reasoning tokens | 1161 | 1042 | 840 |
| Non-reasoning completion tokens by subtraction | 155 | 130 | 172 |

The headers measurement includes connection/network/server work; it is not time to first model token. Body wait includes server inference and transfer, so this client trace cannot separate pure inference time from network time. The production request is non-streaming. API-reported reasoning tokens account for approximately **83–89%** of completion tokens, directly confirming that reasoning is active. Its share of token count does not prove an equal share of latency. Provider key decryption took approximately 0.93–1.37 ms per access.

The local validation/persistence/scheduling gap was 20–27 ms in two samples and 647 ms in one. The wrappers do not separately instrument validation versus SQLite writes, and the cause of that one larger gap is not established. It remains a secondary item for closer measurement if it repeats.

Recommendation: first compare explicit `thinking: { type: "disabled" }` against this baseline, preserving six candidates and Jev. Jev contributes only about a quarter second here, so removing it would not address the main delay. The two-second target remains unverified pending that comparison.

## Evidence and setup limitations

### Explicit non-thinking comparison

The user subsequently requested testing with thinking disabled. Only the disposable fixture's HTTP handler added `thinking: { type: "disabled" }` to DeepSeek requests; production source, running application parameters, prompt, max_tokens, candidate count and Jev path were unchanged.

| Measurement | Sample 1 | Sample 2 | Sample 3 |
| --- | ---: | ---: | ---: |
| Click to bubble, ms | 1654.473 | 1907.766 | 2225.746 |
| DeepSeek provider, ms | 1335.525 | 1566.269 | 1783.828 |
| Jev provider, ms | 256.544 | 269.130 | 376.079 |
| Completion tokens | 113 | 132 | 149 |
| Reasoning content characters | 0 | 0 | 0 |
| Accepted candidates | 6 | 6 | 6 |
| Displayed with matching history | yes | yes | yes |

Median dropped from **6.520 seconds to 1.908 seconds**, approximately **71% lower**. Two of three samples completed within two seconds; one took 2.226 seconds. This small comparison supports adopting explicit non-thinking for short speech, but does not establish a stable two-second SLA or broader language-quality parity. All three used one generation attempt and one selection attempt. API output omitted the reasoning-token detail field and contained no reasoning text. All fixture key copies were removed and confirmed absent afterward.

Raw evidence: `.local/live-ai-e2e/no-thinking-20261008-{1,2,3}/result.json`, summary `.local/live-ai-e2e/no-thinking-summary.json` in the primary checkout. Final fixture build succeeded with zero errors/warnings. At the time of these three samples, only the fixture changed the request; production adoption is recorded below.

Subsequently, for the user's requested manual test, the production DeepSeek request was updated to include `thinking: { type: "disabled" }`. The existing HTTP contract test was extended and observed failing before this change, then the complete deterministic suite passed **260/260** after the change. Release app compilation succeeded with two existing WFO0003 DPI warnings. The worktree executable was started with the existing user configuration. On 2026-10-08 the user reported that the manual right-click test passed and requested local integration into main. This is a basic manual interaction pass; the three non-thinking timings above remain fixture evidence, and full Shell/hardware acceptance remains pending.

- Final fixture build: exit 0, zero errors/warnings, `dotnet build .local/live-ai-e2e/LiveAiE2e.csproj -c Release --nologo -p:BuildProjectReferences=false`.
- Raw safe timing metadata: `C:/Users/ton20/Documents/works/projects/desktop-puppy/.local/live-ai-e2e/timing-clean-20261008-{1,2,3}/result.json`.
- Computed summary: `C:/Users/ton20/Documents/works/projects/desktop-puppy/.local/live-ai-e2e/timing-summary.json`.
- The wrapper buffers at most 64 KiB, as the product does, and captures only counters, stage names and fixed outcome kinds. It does not persist reasoning text, HTTP headers or response bodies. Generated displayed speech is included in the fixture output as in the previous live smoke test.
- Three initial instrumented runs were excluded from final totals because the test's polling reader held a SQLite read lock across an asynchronous delay and interfered with completion writes. Two ended `Failed/Unavailable`; one displayed. Closing the reader and connection before waiting fixed the fixture. Those outcomes do not establish a product failure. One initial DeepSeek attempt independently hit its real eight-second timeout before a successful retry; that confirms that slower upstream responses can occur, even though the three clean samples did not retry.
- During the baseline and comparison measurements, product source and configuration were unchanged; the non-thinking production adoption and deterministic rerun occurred afterward as recorded above. Existing UI results were not rerun during the fixture-only measurement.
