# Test isolation and execution

## Share infrastructure, isolate test-owned state

Keep Testcontainers, server hosts, backing databases, and seed content shared for the test session. Use `ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)` for the existing host. Do not create a container, host, or database for each test: that exhausts runner memory and CPU.

A test that mutates objects or asserts notifications creates its own player and, where needed, room and objects using `TestIsolationHelpers`. Use `CommandParserFor(player.DbRef, player.Handle)` and pass that same handle to `CommandParse`. Disconnect test-owned handles in teardown. Never clear a shared notification substitute or reset the shared database as test cleanup. A private player in a shared room can still receive other tests' broadcasts and disconnect announcements; exact output-order assertions need a private room or an exact sender filter. Session-store tests must create their own account instead of assuming a fixed account ID belongs to them.

`GenerateUniqueName` combines a compact random run identifier and atomic sequence with a readable prefix. Do not replace this with timestamps plus a small random suffix. When strings alone cannot disambiguate activity, create a private recipient/object and assert the intended recipient and sender. Read created objects through their returned DBRefs and relationships; never assume adjacent operations receive consecutive object numbers.

## Wait for completion

An awaited command may complete its writes directly, or it may queue work. For directly awaited writes, assert the result without adding a delay. For queued work, capture the recipient's notification window before issuing the command and wait for its completion output:

```csharp
var start = factory.Notifications.DeliveryCountFor(player.DbRef);
await parser.CommandParse(player.Handle, connections, command);
await factory.Notifications.WaitForDeliveryAsync(
    player.DbRef, expectedText, sender,
    cancellationToken: cancellationToken, startIndex: start);
```

A completion check must observe state/output, not repeat a mutating command. DEBUG traces can quote an expected message before delivery: use the actual message shape when debugging is enabled. If output precedes trailing writes, also await the operation's owned queue entries or final state. An arbitrary quiet period does not prove delayed work completed.

Pass test cancellation tokens to asynchronous operations. On TUnit 1.66 the context accessor is `TestContext.Current!.Execution.CancellationToken`; test methods can also accept a `CancellationToken` parameter. Notification helpers default to the current test's execution token.

## Fixture ownership and scheduling

The outer server fixtures own their existing inner TUnit web factories and dispose them asynchronously. `TestDatabaseStorage` is a shared dependency of all server variants, so its temporary directory is removed only after every consuming host stops. Caller-supplied database paths remain untouched. Each existing host variant uses its own Quartz scheduler name because Quartz indexes schedulers process-wide; one host must not stop another host's queue when it is disposed. Quartz also stores its logger provider process-wide, so the test hosts use the existing shared diagnostics logger factory; an individual host must not own its disposal.

The existing secondary host depends on the primary fixture and reuses its database provider, object cache, and invalidation version ledger. Register shared disposable instances as externally owned in the secondary container. Opening another LMDB environment on the same path in one process is [explicitly unsupported by LMDB](https://github.com/LMDB/lmdb/blob/mdb.master/libraries/liblmdb/lmdb.h); a second host must not reopen the shared world.

Prefer independent setup over `DependsOn` chains. Keep genuine global configuration tests serialized unless all affected readers participate in another correct isolation mechanism. Keyed `NotInParallel` only excludes tests with matching keys. Retain the measured concurrency cap until a resource-specific limiter demonstrates an improvement; sharding is not a substitute for correct isolation.

Use data-source factories (`Func<T>`) when each execution needs a fresh mutable reference. Make disposal ownership explicit, including handlers and clients built outside dependency injection.

## Runner and diagnostics

All test projects use `TUnitVersion` from `Directory.Build.props`; CI cache keys include that file. Build once, then run tests with `--no-build --no-restore`. TUnit 1.66 removed `--parallelism-strategy`; `--maximum-parallel-tests 8` retains the current concurrency cap.

CI keeps detailed pass/fail output and TRX files. Routine application logging and optional HTML reporting stay disabled. Enable `SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING=true` only for a focused investigation. Run full suites serially on development machines so separate test processes do not multiply infrastructure resource use.

When changing fixtures, validate both a filtered run and a full run, including teardown. For timing comparisons, use the same provider, concurrency, logging, and build mode. On Linux, keep the temporary path short enough for Unix-domain sockets; a short symlink can point at disk-backed storage.

## TUnit references

- [Tips and pitfalls](https://tunit.dev/docs/guides/best-practices/): share expensive infrastructure and keep test state independent.
- [Test lifecycle](https://tunit.dev/docs/writing-tests/lifecycle/): initialization and disposal follow fixture sharing and dependency ownership.
- [Async assertions](https://tunit.dev/docs/assertions/tasks-and-async/): use `WaitsFor` to observe eventual state with bounded waits.
- [Test context](https://tunit.dev/docs/writing-tests/test-context/): use the executing test's context for cancellation, not a context captured by a shared fixture.

Examples above are compiled against this repository's pinned TUnit release; the website can describe a different version.
