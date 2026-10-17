# Natural date expression implementation plan

Goal: implement #2000 across a shared deterministic parser, MUSH function and actual scene commands.
Architecture: pure Library.Time parser with typed resolution/failure, thin function adapter, scene delimiter orchestration with no duplicate grammar.
Stack: C#/.NET11, existing TimeZones/TimePrecisions, TUnit, existing scene softcode.
Spec: ../specs/2026-10-16-natural-date-expressions.md
Global constraints: preserve convtime and scene pitch/permissions; bounded full consumption; explicit fixed-reference tests; no runtime network/dependency; formatter convergence; shared build flock and maximum eight parallel tests.
Review focus: DST gap/fold handling, bare/this/next weekday differences, literal delimiter preservation and failure-before-write.

- [x] Observe behavioral RED for dateparse through actual function parser.
- [x] Implement typed parser and function with fixed reference/zone corpus, ambiguity and bounds tests.
- [x] Observe scene behavioral RED, integrate schedule/reschedule/pause and preserve legacy offsets/pitches.
- [x] Document function and scene policies/examples; run focused parser/time and real scene regressions.
- [x] Review diff, commit, create/attach PR and report exact validation.

Decision ledger: root approved focused grammar and weekday/calendar choices, caller TZ fallback, bounded slash-prefix strategy with IANA typo/title/pitch regression requirements. User authorized inline execution without further design gates.

## Validation

Both `dotnet build SharpMUSH.Tests -m:2 -nr:false` and the Integration build passed with zero warnings/errors under the shared `flock --close` build lock. Formatting converged in two passes. Every run used `--no-build -- --maximum-parallel-tests 8` with these filters:

- `/*/*/Date*/*`: 120/120 passed on final source (library/function, shared 38-expression corpus, ambiguity, DST, bounds, metadata, conflicting zones and malformed Unicode input).
- `/*/*/SceneVerbRulesIntegrationTests/*`: 69/69 passed on final source, including the same corpus, actual commands, DST offsets, legacy elapsed-day compatibility, epoch-zero reference, title/pitch delimiters and no-write refusals.
- `/*/*/TimeFunctionUnitTests/*`: 73/73 passed; `convtime()` remains unchanged.
- `/*/*/FunctionArityParityTests/*`: 21/21 passed.
- Broader `/*/*/Scene*/*` baseline: 260/260 passed before the final isolated ISO-clock/contradictory-input guards and extra corpus row; the affected scene rules were then rerun as above.

Behavioral RED preceded implementation through the actual missing function and actual schedule command. Additional observed REDs covered DST reply offsets, explicit-year metadata, ignored/conflicting zones, epoch zero, Unicode numeric exceptions, the ISO/12-hour false range and contradictory year/direction components. No runtime dependency was added. Root independent review and PR handoff follow this checkpoint.

Independent-review follow-up: synchronized the scene browse index with manifest 1.15.0, and refuse signed offsets combined with contradictory textual directions. The existing index-agreement regression failed at 1.14.0 before the fix; actual parser probes reproduced `in -2 hours` and `-2 hours ago` resolving backward before the refusal guard. Four permanent direction regressions now pass. The affected DateExpression classes passed 79/79 and ExamplePackageTests passed 12/12; the test-project build had zero warnings/errors and strict PackageValidator passed all 57 bundled packages/index entries. An attempted combined pipe-separated TUnit filter was stopped after selecting unrelated tests; those two verified groups used separate supported filters.

Resolved-zone review fix: actual UTC-player scene scheduling of `tomorrow 7pm America/Chicago` first failed because the confirmation showed midnight UTC. Added `normalized` output to `dateparse`, preserving all numeric output values, and a shared scene display helper using the same expression/reference as the instant parse. Natural schedule/reschedule/pause confirmations now preserve written zones and numeric offsets; legacy single h/d/w displays retain their elapsed adapter. Actual IANA scheduling, explicit offset plus IANA reschedule/pause, and both fold-offset replies pass. Final builds: zero warnings/errors for Tests and Integration. Final affected suites: Date* 126/126; SceneVerbRulesIntegrationTests 69/69; strict PackageValidator 57 entries passed.

Help-navigation CI follow-up: initial e012 CI identified the new function topic missing from the Time Functions category. Focused HelpNavigationTests reproduced 2/4 failures (reachability/category membership); linking DATEPARSE() in the existing table restored 4/4 passing without changing runtime code.

Final e2e119ebf CI: engine 18,277 total / 18,227 passed / 50 skipped / zero failures; integration 999/1,000 passed, with only an old weekday-formatted confirmation assertion failing after the intentional normalized-output change. Updated that assertion to require the normalized date plus numeric UTC offset. The complete existing reader-zone scenario then passed locally (1/1), with Integration build zero warnings/errors; no runtime source changed.
