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
- [ ] Review diff, commit, create/attach PR and report exact validation.

Decision ledger: root approved focused grammar and weekday/calendar choices, caller TZ fallback, bounded slash-prefix strategy with IANA typo/title/pitch regression requirements. User authorized inline execution without further design gates.

## Validation

Both `dotnet build SharpMUSH.Tests -m:2 -nr:false` and the Integration build passed with zero warnings/errors under the shared `flock --close` build lock. Formatting converged in two passes. Every run used `--no-build -- --maximum-parallel-tests 8` with these filters:

- `/*/*/Date*/*`: 120/120 passed on final source (library/function, shared 38-expression corpus, ambiguity, DST, bounds, metadata, conflicting zones and malformed Unicode input).
- `/*/*/SceneVerbRulesIntegrationTests/*`: 69/69 passed on final source, including the same corpus, actual commands, DST offsets, legacy elapsed-day compatibility, epoch-zero reference, title/pitch delimiters and no-write refusals.
- `/*/*/TimeFunctionUnitTests/*`: 73/73 passed; `convtime()` remains unchanged.
- `/*/*/FunctionArityParityTests/*`: 21/21 passed.
- Broader `/*/*/Scene*/*` baseline: 260/260 passed before the final isolated ISO-clock/contradictory-input guards and extra corpus row; the affected scene rules were then rerun as above.

Behavioral RED preceded implementation through the actual missing function and actual schedule command. Additional observed REDs covered DST reply offsets, explicit-year metadata, ignored/conflicting zones, epoch zero, Unicode numeric exceptions, the ISO/12-hour false range and contradictory year/direction components. No runtime dependency was added. Root independent review and PR handoff follow this checkpoint.
