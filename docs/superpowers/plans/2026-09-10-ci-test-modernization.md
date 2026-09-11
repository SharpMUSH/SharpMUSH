# CI test modernization implementation plan

> Execute tasks in this worktree using subagent-driven-development, with builds and test runs serialized.

**Goal:** Upgrade TUnit and remove avoidable test scheduling and waiting costs while retaining deterministic assertions and diagnostic results.

**Architecture:** Share TestContainers, server hosts, backing databases, and seed content across the session. Isolate only test-owned identities and objects. Test fixtures own their connections and data; host fixtures own host shutdown and temporary storage cleanup.

**Tech stack:** .NET 10.0.400, TUnit, Testcontainers, Lightning, NSubstitute.

**Spec:** User-approved recommendations in this task, including the explicit prohibition on per-test containers, hosts, or database instances.

## Constraints

- No sharding workaround, new per-test container/host/database, or blanket retry additions.
- Preserve recipient/enactor/executor assertions, shared seed data, pass/fail output, and TRX reports.
- SurrealDB testing remains disabled in CI; support is not removed.
- Keep normal application logging disabled below fatal/critical.
- Run builds and test suites serially to bound memory and CPU pressure.

## Tasks

- [x] Build and capture the unmodified unit baseline using the existing CI filter and concurrency 8. Store logs outside tracked source.
- [x] Centralize TUnitVersion in Directory.Build.props; update all five TUnit package references together. Include Directory.Build.props in setup-dotnet dependency cache keys. Build all projects and resolve actual upgrade diagnostics.
- [x] Make shared ServerWebAppFactory and ConnectionServerWebAppFactory plain owning fixtures around their existing TUnit web factories. Dispose owned hosts before deleting owned database directories. Add lifecycle regression coverage; validate a filtered fixture run and full teardown.
- [x] Replace timestamp/four-digit names with a run identifier and atomic sequence. Add concurrency/name-format coverage without creating a host or database.
- [x] Give BuildingCommandTests private actors, rooms, and connection lifetime using shared infrastructure. Remove DependsOn and global NotInParallel; identify exits by relationships rather than consecutive global object numbers. Run independently and alongside the rest of the suite.
- [x] Replace unnecessary post-command sleeps in AccountAdminCommandTests with awaited command completion; give mutable account data unique names. Add cancellation to central notification waits using the current TUnit polling assertion API and preserve recipient windows.
- [x] Inspect BBS readiness sleeps and replace only those for which completion can be observed deterministically. Preserve deliberate time-behavior tests.
- [x] Run unit, integration, bUnit, and provider-independent scene suites and compare durations with baseline. Keep concurrency 8 unless measured evidence supports a safe change; introduce no speculative limits.
- [x] Run formatting and review the complete diff.
- [ ] Commit and publish a PR with measured results and any material limitations.

## Validation commands

Build: `dotnet build -m:2 -p:SkipFormatVerification=true`

Unit: `TUNIT_DISABLE_HTML_REPORTER=true SHARPMUSH_DATABASE_PROVIDER=lightning dotnet run --project SharpMUSH.Tests --no-build --no-restore -- --output Detailed --report-trx --maximum-parallel-tests 8 --treenode-filter '/*/(*)&(!SharpMUSH.Tests.Database.SurrealDB)/(*)&(!Surreal*)/*'`

Integration: same environment, `dotnet run --project SharpMUSH.Tests.Integration --no-build --no-restore -- --output Detailed --report-trx --maximum-parallel-tests 8`.

bUnit: `dotnet run --project SharpMUSH.Tests.BUnit --no-build --no-restore -- --output Detailed --report-trx`.

Scene: `dotnet run --project SharpMUSH.Tests.ScenePlugin --no-build --no-restore -- --output Detailed --report-trx --treenode-filter '/*/*/(*)&(!SurrealSceneMembershipTests)/*'`.

Formatting: `dotnet format whitespace --folder . --exclude '**/bin/**' --exclude '**/obj/**' --exclude '**/TestResults/**' --verify-no-changes`.

## Full-suite findings addressed

- Correct fixture disposal exposed two Quartz process-wide ownership conflicts: scheduler names collided, and a disposed sibling host's logger factory broke delayed job construction. Existing hosts now have separate scheduler names and share the existing diagnostics logger factory.
- Five configuration tests were permanently replacing the shared options callback. They now use async-flow scopes and own their handles/accounts.
- Zone graphs use private actors and rooms instead of shared God state and dependency chains. Examine output-order assertions use private rooms to exclude unrelated disconnect announcements.
- Session-store tests create real private accounts rather than assuming `node_accounts/1` is theirs.
- The two existing server variants were independently opening the same LMDB path. The secondary now depends on the primary and reuses its provider, cache, and version ledger; a cross-host write/read regression verifies invalidation.
- The capture identified routine ASP.NET request logs in rate-limiter smoke tests; those use the common fatal-only diagnostics configuration.

## Measurements

Same-checkout baseline: .NET 10.0.400, Lightning, Debug, no rebuild, concurrency 8, routine logging disabled. The TUnit 1.19.16 baseline completed 9,473 tests (9,297 passed, 176 skipped) in 238.905 seconds, 240.554 seconds wall time.

Final integration: 429 passed, zero failed/skipped, 79.167 seconds test duration and 80.072 seconds wall time. The preceding green run took 56.140 seconds (57.024 wall), so local timing varied materially. Thirteen unconditional ten-second BBS waits were replaced with recipient/sender-scoped completion and owned queue-state checks. No fresh integration baseline was taken, so this is an absolute measurement rather than a before/after claim.

Final bUnit: 655 passed, 5.306 seconds. Provider-independent scene tests: 21 passed, 0.196 seconds.

Final unit: 9,479 total, 9,319 passed, 160 skipped, zero failures; 261.212 seconds test duration and 262.709 seconds wall time. This is 22.155 seconds slower than the baseline wall time (about 9.2%), with 22 additional passing cases. This run does not demonstrate a unit throughput improvement. Correct fixture shutdown and the additional active tests are part of the changed workload; no causal breakdown of the timing difference was measured. The capture contained only test results and runner artifact/summary lines, with no routine application logging.
