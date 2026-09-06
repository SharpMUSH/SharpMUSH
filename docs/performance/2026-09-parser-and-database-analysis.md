# Parser and database performance analysis, September 2026

Scope: the MUSH parser hot path (`MUSHCodeParser`, `SharpMUSHParserVisitor`, `MarkupString`) and the
database read path behind it (`ISharpDatabase` providers, the Mediator query cache). Every number
below was measured on this branch with the tooling described in "Method"; nothing here is inferred
from reading code alone.

## Summary

The parser was not CPU-bound. It was doing two things per evaluation that had nothing to do with
parsing:

1. **Rebuilding the command trie on every command.** `MUSHCodeParser` is a record whose
   `CommandTrie` field initialiser walked every registered command; `FromState` constructed a new
   record for every command a player typed. That was **74–86% of all bytes allocated** by a trivial
   evaluation (about 200 KB of 245 KB for `[add(1,2)]`).
2. **One ArangoDB round trip per function call.** `VisitFunction` checks the executor's DEBUG flag,
   `HasFlag` enumerates `SharpObject.Flags`, and that lazy re-queried the graph on every enumeration.
   Ten nested `add()` calls were ten HTTP requests. The same held for powers (`IsGuest`, `IsSee_All`).

With those two and a third tier of parser-internal allocations fixed, a bare `[add(1,2)]` went from
389 µs and 245 KB with one database request to **4.2 µs and 11 KB with none**; a typed
`think Hello World` from 403 µs to **12.9 µs**. On the database side, an uncached attribute listing was
N+1 queries (one per attribute for its flags); it is now one.

Full suite after the changes: 6353 tests, 0 failed (189 skipped as before).

## Method

- **Harness.** `SharpMUSH.Benchmarks` gained a `profile` mode that boots the production host
  (ArangoDB and NATS in Testcontainers) and runs fixed scenarios in a loop, reporting ops/s, µs/op,
  managed KB/op, and **ArangoDB HTTP requests per op** read from the server's own
  `/_admin/statistics` counter, so the last column is wire truth, not an estimate.

  ```bash
  SHARPMUSH_CI_BENCHMARK=true TESTCONTAINERS_RYUK_DISABLED=true \
    dotnet run -c Release --project SharpMUSH.Benchmarks -- profile [scenario,...] [--seconds N] [--wait N]
  ```

- **Allocation attribution.** `dotnet-trace collect -p <pid> --profile dotnet-sampled-thread-time,gc-verbose`
  attached to the harness at steady state (`--wait` prints the PID and pauses), then a small
  TraceEvent-based analyser grouped `GCAllocationTick` samples by allocating frame and by type. That
  ranking, not intuition, chose every fix below. CPU sampling was uninformative for this workload: a
  single-threaded await loop shows idle thread-pool waits, and the real cost was the network wait.

- **Two benchmark-validity bugs found first.** The existing BenchmarkDotNet suite could not have
  found any of this:
  - `BenchmarkHelpers.CreateTestParser` built a `ParserState` with no invocation counters, so
    `FunctionParse` pushed a tracking frame with a null executor and **every function call threw**;
    the function benchmarks were timing an exception per call (900k `CallFunction` errors in one
    4-second run).
  - Benchmarks reused one parser across iterations. The invocation counter lives on the state and is
    cumulative, so after `function_invocation_limit` (25,000) every op short-circuited on the limit
    error and reported 2 µs for `u()`.

  Both are fixed (`BenchmarkHelpers.FreshState`, `BaseBenchmark.FreshParser()`), and the benchmark
  host now uses a plain `IOptionsWrapper` instead of an NSubstitute proxy, which was itself 5% of
  the measured allocations.

## Findings and fixes, ranked by measured payoff

### 1. Command trie rebuilt per parser copy (parser)

`private readonly CommandTrie _commandTrie = BuildCommandTrie(CommandLibrary);` on a record, plus
`FromState => new MUSHCodeParser(...)`. Every `FromState` also re-resolved seven services from DI.

Fix: `CommandTrie.For(library)` caches one trie per `LibraryService` in a `ConditionalWeakTable`.
Staleness is caught by comparing the library's count on every lookup (any add or remove changes it,
so a plugin or a test that registers a command straight into the library is seen without knowing a
trie exists), and by an explicit `CommandTrie.Invalidate` from `PluginManager` for the equal-count
reload case. `FromState` is now a record `with` copy.

`[add(1,2)]`: 245 KB → 28 KB per op. Time unchanged at this step, because the DB round trip remained.

### 2. Object flags and powers queried per check (database, hit from the parser)

`SharpObject.Flags` / `.Powers` were `Lazy<FreshAsyncEnumerable>` over a direct provider query, so
`HasFlag`, `IsWizard`, `HasPower`, `IsGuest` each cost a graph traversal. `GetObjectFlagsQuery`
already existed as a cached Mediator stream query with invalidation wired from the flag commands,
and had **zero callers**.

Fix: all three providers build `Flags` over `mediator.CreateStream(new GetObjectFlagsQuery(id, type))`
and `Powers` over a new `GetObjectPowersQuery(id)` (key `object-powers:{id}`). Memgraph and SurrealDB
take an optional `IMediator` (bare test construction still works). `SetObjectPowerCommand` /
`UnsetObjectPowerCommand` now invalidate the per-object key; both queries carry a tag that
`DeleteObjectCommand` clears, because dbref numbers are reused. `PackageInstallService` wrote flags
and powers straight to the database, bypassing invalidation; it now sends the commands.

`[add(1,2)]`: 389 µs → 7.6 µs, 1 → 0 requests. Ten nested calls: 2.55 ms → 48 µs.

### 3. Parser internals (parser)

From the post-fix trace:

- `EvaluateCommands` scanned the entire command library twice per command with LINQ
  (`.Where(...).ToList()`) for the socket and single-token checks: 17% of `think`'s bytes. The
  library is keyed `OrdinalIgnoreCase`, so both are now one dictionary lookup.
- `BufferedTokenSpanStream` allocated a 256-slot `List<IToken>` (2 KB) per parse regardless of input
  and then copied it into an array at EOF: 12–14% of bytes. It is now sized from the input and
  exposes the list's own storage via `CollectionsMarshal.AsSpan`.
- `CallFunction` evaluated arguments through `ToAsyncEnumerable().Select(async ...).DefaultIfEmpty().ToListAsync()`;
  a plain loop removes the adapters and a state machine per argument (8% of a nested call's bytes).

`think`: 29 → 12.9 µs and 29 → 18.6 KB. `[add(1,2)]`: 7.6 → 4.2 µs.

### 4. Attribute reads were N+1 (database)

Every Arango attribute reader returned bare documents and `SharpAttributeQueryToSharpAttribute` then
ran `GetAttributeFlagsAsync` per attribute. The SurrealDB provider already fetched flags inline.

Fix: the 21 reader AQL sites (including the self/parent/zone sub-queries of the inheritance lookup)
return `MERGE(v, { FlagDocs: (sub-query over edge_has_attribute_flag) })`, and the converters use
`FlagDocs` when present. An attribute write followed by an uncached `lattr(me)` on an object with
three attributes went from 11 to 8 requests, and write plus uncached `get()` from 8.8 to 6.9; the
remaining ~6 are the write.

### 5. `cache: true` on AQL queries is a no-op (database)

The provider passes `cache: true` on 20 queries. ArangoDB's `--query.cache-mode` defaults to `off`
(confirmed against the running container: `/_api/query-cache/properties` → `"mode":"off"`), and in
that mode the server never consults the cache. The FusionCache layer is the real cache; no code
change was made for this, but the flag should not be mistaken for one.

## Results

Profile harness, ArangoDB in a local container, Intel Core Ultra 7 265F, .NET 10.0.8, 4 s per scenario. "Before" is the corrected
baseline (after fixing the benchmark-validity bugs, before any engine change).

| scenario | before µs/op | after µs/op | before KB/op | after KB/op | DB req/op before → after |
|---|---:|---:|---:|---:|---|
| `think Hello World` | 403 | 12.9 | 259 | 18.6 | 0.96 → 0 |
| `think %#` | 705 | 10.2 | 273 | 21.0 | 2.0 → 0 |
| `@pemit me=[add(1,2)]` | 1,223 | 18.0 | 302 | 36.8 | 3.0 → 0 |
| `[add(1,2)]` | 389 | 4.2 | 245 | 11.4 | 1.0 → 0 |
| `add` nested ×10 | 2,554 | 31.1 | 434 | 74.8 | 9.9 → 0 |
| `[cat(%#,%#,%#)]` | 344 | 5.8 | 249 | 16.3 | 1.0 → 0 |
| `iter(lnum(50),%i0)` | 794 | 28.9 | 357 | 111.2 | 2.0 → 0 |
| `switch`+`iter`+`add` mix | 3,897 | 63.1 | 576 | 153.1 | 12.9 → 0 |
| 100-char prose in `cat()` | 390 | 6.5 | 251 | 17.9 | 1.0 → 0 |
| `[u(me/FN,5)]` | 853 | 12.0 | 289 | 27.0 | 3.0 → 0 |
| `[get(me/FN)]` | 364 | 5.1 | 245 | 11.5 | 1.0 → 0 |
| `[haspower(me,see_all)]` | — | 5.3 | — | 12.3 | (1 per call by code) → 0 |
| `&ATTR me=x` (write) | — | 6,548 | — | 85 | 6.2 |
| write then uncached `lattr(me)` | — | — | 170 | 143 | 11.0 → 8.0 |
| write then uncached `get(me/FN)` | — | — | 165 | 149 | 8.8 → 6.9 |

Step-by-step for `[add(1,2)]`: baseline 245 KB / 389 µs → trie fix 28 KB / 267 µs → flag cache
15 KB / 7.6 µs → parser internals 11.4 KB / 4.2 µs.

BenchmarkDotNet, `CommandParseBenchmarks`, ShortRun (3 iterations), same machine. The "before"
column comes from the run with the fixed benchmark state; two of its cells were not captured before
the baseline run was stopped as too slow to wait on.

| benchmark | before | after |
|---|---:|---:|
| `think` with literal text | 359 µs, 42.1 KB | 7.6 µs, 18.7 KB |
| `think %#` | 642 µs, 57.1 KB | 9.1 µs, 21.6 KB |
| `think %N` | 607 µs, 58.5 KB | 9.6 µs, 22.9 KB |
| `@pemit me=Hello World` | —, 49.8 KB | 11.6 µs, 25.3 KB |
| `@pemit me=[add(1,2)]` | — | 17.3 µs, 37.7 KB |
| `@set me=SAFE` | — | 12.6 µs, 27.2 KB |

## What remains, ranked

1. **Attribute write path: ~6 HTTP round trips and ~6.5 ms per `&ATTR obj=value`.** `SetAttributeAsync`
   is a streaming transaction with one request per step (begin, path lookup, entry and flag lookups,
   document create, two edge creates, commit). A single AQL statement or a JavaScript transaction
   would make it one. Writes are rarer than reads, which is why it was not done in this pass.
2. **FusionCache entry lifetime is the 30 s default** (`AddFusionCache().TryWithAutoSetup()`, no
   `DefaultEntryOptions`). Every object, flag set, and attribute is refetched 30 s after it was last
   loaded, so a busy object costs a round trip every 30 s per cached shape. Invalidation is explicit
   everywhere, so a much longer default is safe once the team is confident no writer bypasses the
   commands; the 30 s TTL currently masks any that do. Raise it deliberately, with a grep for direct
   `database.*Async` writes in `SharpMUSH.Library/Services` first.
3. **`Core.Arango` was archived on 2026-08-02; 3.12.3 is the final release.** Not a performance
   problem today, but a maintenance cliff. Two config-only wins while it lasts: inject an `HttpClient`
   with `DefaultRequestVersion = HttpVersion.Version20` (the driver speaks HTTP/1.1 otherwise) and
   keep the System.Text.Json serializer already configured. `ArangoDBNetStandard` is maintained but
   Json.NET-first and lower level; no protocol-level speedup from switching.
4. **`ParserState.ArgumentsOrdered`** builds an `ImmutableSortedDictionary` per function call for
   functions that use it (about 3.5% of a nested call's bytes). A sorted array or a cached
   `CallState[]` keyed on the `Arguments` reference would remove it; it is an API used across the
   function library, so it wants its own change.
5. **ANTLR per-parse floor.** After the fixes, what remains in `[add(1,2)]` is one `CommonToken`
   object per token, one context plus a `List<IParseTree>` per rule, and the async state machines
   of the visitor. There is no newer `Antlr4.Runtime.Standard` to upgrade to (4.13.1 is the last
   C# runtime; antlr-ng changes only the tool). Two-stage SLL/LL prediction is already in place.
   The credible alternative is a hand-written or source-generated parser (Parlot 2.0's published
   numbers are ~300–600 B per small expression), which is a grammar rewrite and a project of its own.
6. **`NotifyService.Notify`** is 2.4% of `think`'s bytes and sits outside this scope; the output
   path (serialising the `MString` and publishing to NATS) is the next thing a `think`-heavy profile
   would show.
7. **GC configuration.** Server GC with DATAS is the .NET 10 default and costs 2–3% throughput for
   a much smaller working set. With allocations now an order of magnitude lower there is no
   evidence-based reason to change it; if gen0 rate ever dominates a trace, raise
   `System.GC.DGen0GrowthPercent` before disabling DATAS.
8. **FusionCache 2.5.0 → 2.7.2** and **Mediator 3.0.2** are fine as they are: the newer FusionCache
   releases carry no performance changes, and Mediator's pipeline chain is built once under the
   Singleton lifetime this app uses.

## Reproducing

```bash
# Harness, all scenarios
SHARPMUSH_CI_BENCHMARK=true TESTCONTAINERS_RYUK_DISABLED=true \
  dotnet run -c Release --project SharpMUSH.Benchmarks -- profile --seconds 4

# Attach a trace to one scenario at steady state
SHARPMUSH_CI_BENCHMARK=true TESTCONTAINERS_RYUK_DISABLED=true \
  dotnet run -c Release --project SharpMUSH.Benchmarks -- profile fn1 --seconds 30 --wait 12 &
# wait for "READY pid=..." then:
dotnet-trace collect -p <pid> --duration 00:00:15 --profile dotnet-sampled-thread-time,gc-verbose
```

The allocation analyser used for attribution is a ~100-line console app over
`Microsoft.Diagnostics.Tracing.TraceEvent` (`TraceLog.CreateFromEventPipeDataFile`, then
`GCAllocationTickTraceData.CallStack()` walked to the first managed frame). It is not checked in;
`dotnet-trace report topN` gives the CPU view, and PerfView opens the same `.nettrace` for the
allocation view.
