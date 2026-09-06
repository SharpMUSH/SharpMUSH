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
`HasFlag`, `IsWizard`, `HasPower`, `IsGuest` each cost a graph traversal, and `VisitFunction`
checks the executor's DEBUG flag on every call.

Fix: the object is the unit. Every provider loads an object's flag and power documents in the same
round trip as the object (an AQL `MERGE` sub-query, a Cypher pattern comprehension, a SurrealQL
graph projection) and materialises them on the `SharpObject`; the `Lazy<IAsyncEnumerable<>>`
property shape is unchanged, so no call site moved. Because the object node is already cached under
`object:#N` and every flag, power and lock write removes that key, the relations are cached and
invalidated with it, with no second cache entry to keep coherent. A loaded object is therefore a
snapshot, as its locks already were: the set/unset flag and power handlers update the instance
the caller holds, the way `SetLockCommandHandler` does. Construction paths that still return a
bare document (a few cold Memgraph lookups) fall back to a direct read on first use; none of
them involve a mediator. The providers no longer take an `IMediator` for this.

One consequence needed its own rule. The same object also sits inside *other* cached results, a
room's contents list, an occupant's location answer (`Where()` is the cached `GetCertainLocationQuery`,
and its answer is the container's instance), a player-by-name lookup, and a flag write does not
remove those entries; a cached contents list would keep showing an object as it was before
`@set obj=DARK` (a test caught exactly that). So every cached result now carries one `obj:#N` tag
per object it embeds, stamped by the caching behaviours from the value itself (`EmbeddedObjects`),
and the invalidation behaviour expires `obj:#N` whenever a command removes `object:#N`. One rule,
invalidating an object expires everything that embeds it, covers flags, powers, locks and names in
every shape, with no code path that mutates cached instances. It is the same mechanism #854
established: a tag is resolved against when the reading factory started. An entry that gains these
tags loses fail-safe and eager refresh, the rule the Tagged profile encodes statically; the object
node's own query is left untagged since its key is what a write removes. The read-side cost is one
tag check per embedded object per hit: a fifty-object room's contents list pays fifty dictionary
lookups, a few microseconds.

The tags alone were not enough, because a loaded object also *memoised* other objects: `Location`,
`Owner`, `Parent` and `Zone` were DotNext `AsyncLazy` fields resolved once per instance, and a
cached node instance lives for minutes. The location entry was expired correctly by `obj:#room`,
and the occupant's node kept handing back the old room from its memo regardless. Those four
relations are now `AsyncRelation` (same `WithCancellation` shape, resolved on every read) and
resolve through an `IObjectRelationLoader` seam owned by `SharpMUSH.Library`: the host implements
it over the Mediator's cached queries (`GetCertainLocationQuery`, and new `GetOwnerOfQuery`,
`GetParentOfQuery`, `GetZoneOfQuery`, each tagged with the subject object so a re-parent expires
the answer, and with the embedded object so a write to the parent does too). Providers ask through
the seam and know nothing of the cache; `ArangoDatabase` no longer takes an `IMediator`. Every
`Where()` is now a cache hit that follows invalidation, rather than a memo that ignores it.

The follow-up (#868) finished the shape: `Home`, a room's drop-to and an exit's destination go
through the same seam (`GetHomeOfQuery`, `GetDropToOfQuery`, `GetExitDestinationOfQuery`), so no
provider-built object memoises another object anywhere; the cold Memgraph construction paths
return their relation columns too, and a bare document is now an error rather than a slow path
(which surfaced a bug the fallback had hidden: the Cypher projection's comprehension variables
were `f` and `p`, so any query that had already bound `p` - the player queries bind it to the
Player node - got an empty power list, and `connect guest` on Memgraph found no guests);
the snapshot rule lives on `SharpObject` as `WithFlag`/`WithoutFlag`/`WithPower`/`WithoutPower`/
`WithLock`/`WithoutLock`, which the handlers call; and the unused per-object flags query, its key
and its tag are gone. What remains open there is list-shaped results caching object instances
rather than dbrefs.

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

### 5. Cache entry profiles (database cache)

The engine cache ran on FusionCache's defaults: every entry lived 30 s, no fail-safe, no factory
timeouts, no memory bound. Invalidation is explicit (every write is a command that removes its keys
or tags), so the lifetime is a safety net, not the freshness mechanism, and can be minutes.

Each cached query now carries a `CacheEntryProfile`, derived from its tags unless overridden:

| profile | when | lifetime | fail-safe | refresh | priority / size |
|---|---|---|---|---|---|
| Object | invalidated by key only (object nodes, command/listen caches, lock delegates) | 10 min, 60 s jitter | on: 1 h max, 15 s throttle, 150 ms soft timeout | eager at 85% | normal, 1 |
| Tagged | any tag (contents, location, attributes, flag/power sets, definition lists) | 10 min, 60 s jitter | **off** | **none** | normal, 1 per element |
| Scan | large, rarely read listings (zone members, log pages) | 60 s | off | none | low, 1 per element |

Why fail-safe is off for tagged entries: FusionCache's `RemoveByTag` is an expire, not a delete, and
the documentation is explicit that tag removal "does not interfere with the fail-safe mechanism". A
contents entry with fail-safe on would come back as a stale fallback while the database was slow or
down, showing a room's pre-move contents, which is the exact loss the per-container contents tag
(#854) exists to prevent. A key removal deletes the entry outright, so the key-only shapes can have
fail-safe safely; no write can leave one behind. `CacheEntryProfileTests` pins both behaviours
against a real FusionCache instance, and checks that no query declares tags while asking for the
Object profile.

A second FusionCache detail, read from its source rather than its documentation: a foreground
factory's entry is stamped when the factory *starts* (which is what lets a tag removed mid-read
expire the result, the mechanism #854 relies on), but an entry stored by a background completion,
an eager refresh or a timed-out factory allowed to finish, is stamped when it is *stored*. Such an
entry outlives a tag removed while its factory ran, holding pre-write data. So tagged entries get no
eager refresh, and no profile lets a timed-out factory complete in the background
(`AllowTimedOutFactoryBackgroundCompletion = false`); a timed-out read is discarded and the next
read retries.

The cache's *registered* default is the Tagged profile, so an ad-hoc caller that only sets a
duration (the account-claims cache, tag-invalidated on bans and role changes) can never inherit
fail-safe; only the caching behaviour hands out the Object profile, to key-invalidated queries.

The memory cache is bounded at 250,000 units (one per document, one per element of a cached list,
compacting the least recently used tenth), so a full-database sweep fills the cache instead of
growing the process without limit. Every factory runs under FusionCache's token with a 5 s hard
timeout, so a hung database call is cut loose from the command and from the per-key lock it holds.

### 6. `cache: true` on AQL queries is a no-op (database)

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
2. **Cache lifetimes now mask nothing.** With entries living 10 minutes, a writer that bypasses the
   commands leaves a stale entry for 10 minutes rather than 30 seconds. Two such writers were found
   and fixed in this pass (flags and powers in the package installer). Any new direct
   `database.*Async` write outside `Handlers/Database` is a bug; a grep for them belongs in review.
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

## Architecture: what the data trunk still needs

The Mediator is the data trunk: every read and write of game state is a request type that carries
its own cache policy, and the caching and invalidation behaviours apply it. #867 and #869 made the
providers pure storage behind that trunk. What follows is the structural work that fences it, in
the order it pays off.

1. **Split `ISharpDatabase` along its existing seams.** The provider files are already partitioned
   into objects, navigation, mail, channels, flags and powers. Per-aggregate interfaces registered
   explicitly replace the casts in Startup and let a handler depend on the store it uses. Two
   vocabularies, request types and provider methods, are kept in sync by hand today. Smaller
   interfaces make the drift visible.
2. **Retire the static service locator in `Commands` and `Functions`.** DI constructs each class
   once and copies 27 services into static properties, which is why `Mediator!` appears everywhere
   and why an optional `IMediator? mediator = null` felt natural in a provider. Every command
   already receives the parser. A services context carried by the parser or `CallState` lets
   commands take what they need as a parameter and removes the null-forgiving operators. Migrate
   one command file at a time.
3. **Move migration out of the DI factory.** The provider singleton runs `Migrate()` with
   sync-over-async inside the factory lambda. A hosted service that migrates before the app serves
   keeps the container free of blocking work and makes first-resolve ordering explicit.
4. **Close the remaining coherence window with versions, not more invalidation.** A read that
   issues its query before a commit and stores after the second invalidation caches a pre-write
   answer. Tagged entries do not have this window: FusionCache stamps a foreground factory's entry
   at factory start, so a tag removed mid-read expires the result (section 5). The key-invalidated
   object node does, because a key removal during the factory says nothing about the entry stored
   after it. A per-object version counter bumped on every invalidation closes it, but not as a
   check before the store: an invalidation can land between that check and FusionCache's set.
   The behaviour captures the version before the factory runs and compares again *after*
   `GetOrSet` returns, removing the key if it moved. The removal then happens after the store,
   whichever order the write and the read interleaved, so a stale entry never outlives the
   comparison. This pairs with #868 item 3, since dbref-only lists shrink what a version covers.
5. **Keep the single-process assumptions named.** The design is single-process today: Startup
   registers the in-memory FusionCache only, with no distributed cache and no backplane, so a
   write on a second engine node would leave the first node's entries stale. `AsyncRelation`, the
   snapshot rule and the in-memory tag index share that assumption. The path to more than one
   node is a configured backplane, which carries key and tag removals between nodes and keeps the
   design's shape, plus the version counter in point 4 moved into the distributed cache. Until
   that is configured, run one engine process per database.

Two smaller fences belong with these: the create handlers remove the new object's key themselves
because `ICacheInvalidating.CacheKeys` cannot name a dbref the write allocates, so the contract wants
a result-derived hook and no handler should hold an `IFusionCache`; and the four service-shaped
requests (`GetAttributeServiceQuery`, `LocateObjectQuery`, `EvaluateLockQuery`,
`EvaluateAttributeForLockQuery`) exist only to reach across the attribute/permission/lock
constructor cycle, which lazy injection or a passed evaluator dissolves without a bus.

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
