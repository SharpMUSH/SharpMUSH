# Second-pass performance, caching and DI analysis — 2026-09-06

Baseline: `origin/main` @ `26dea287` (all of the first pass — #867, #869, #870, #871 — merged).

The first pass took the **parser and the object/attribute read path** from DB-bound to cached. This
pass deliberately looked everywhere that pass did not: the **write** path, the **broadcast** path
(say / pose / channels), the **process configuration**, and the **package surface**. Everything below
was verified by reading `origin/main` and, where marked *(proven)*, by running code against the real
assemblies.

Nothing here contradicts the first pass. Several findings are the *same class of bug* the first pass
fixed, in a place it did not look.

> **Status.** Findings 1, 2, 3, 4, 11 and 12 are fixed on `claude/attribute-cache-tags-and-gc`, with
> tests. Finding 2 is fixed for the attribute reads that consult one object; the inherited reads keep
> a game-wide tag deliberately (see `CacheKeys.AttributesTag`), and closing that needs the providers
> to project the objects a read visited — the same prerequisite as the `commands:`/`listens:`
> parent-chain gap recorded on those queries. Everything else below is unstarted.

---

## 1. Attribute cache invalidation by key does not work at all — *(proven)*

Every attribute-mutating command emits an `attribute:` key with a **stray trailing `)`**, and the read
side has none. `ClearAttributeCommand` / `WipeAttributeCommand` additionally key by *each path segment*
rather than the backtick-joined path the reader uses. No command emits a `lazy-attribute:` or
`attribute-inheritance:` key at all.

Run against the built `SharpMUSH.Library`, `DBRef(7)` + `["FOO","BAR"]`:

```
READ  GetAttributeQuery.CacheKey                = [attribute:#7:FOO`BAR]
READ  GetLazyAttributeQuery.CacheKey            = [lazy-attribute:#7:FOO`BAR]
READ  GetAttributeWithInheritanceQuery.CacheKey = [attribute-inheritance:#7:FOO`BAR:True]

WRITE SetAttributeCommand.CacheKeys:
        [attribute:#7:FOO`BAR)]   == read key? False
        [commands:#7]             == read key? False
        [ancestor-commands:#7]    == read key? False
        [ancestor-listens:#7]     == read key? False
WRITE ClearAttributeCommand.CacheKeys:
        [attribute:#7:FOO)]       == read key? False
        [attribute:#7:BAR)]       == read key? False
        ...

Any write key removes the entry GetAttributeQuery stored?  False
Any write key removes the lazy entry?                      False
Any write key removes the inheritance entry?               False
```

Affected: `SetAttributeCommand`, `ClearAttributeCommand`, `WipeAttributeCommand`,
`SetAttributeFlagCommand`, `UnsetAttributeFlagCommand` — all five.

The reason nobody noticed is finding #2.

**Fix:** drop the `)`, use the joined path in Clear/Wipe, and add the `lazy-attribute:` /
`attribute-inheritance:` / `lazy-attribute-inheritance:` variants. Better: give `ICacheable` a static
key-builder that both sides call, so a reader and its invalidator cannot drift again — this is exactly
what `CacheKeys.Object/Contents/Location` already does for the keys that *do* work.

## 2. `object-attributes` is one global tag: any attribute write flushes every object's attributes

`GetAttributeQuery`, `GetLazyAttributeQuery` and both inheritance queries all carry
`CacheTags.ObjectAttributes` — a single constant, `"object-attributes"`. So does every attribute write.
One `&DESC me=…` does `RemoveByTag("object-attributes")` and drops the cached attributes of **every
object in the game**.

On a game where attribute writes are continuous (any `@trigger`, any `setq`-to-attribute pattern, mail,
channel buffers), the attribute cache's steady-state hit rate is close to zero — while `attribute:` is
the most-read key family in the engine.

This is the problem #854 already solved once, for contents: *"Per container rather than the broad
`CacheTags.ObjectContents`, because movement is the hot path."* The same reasoning applies verbatim to
attributes, and was never applied.

**Fix:** `CacheKeys.AttributesTag(int number)` → `"object-attributes:#N"`, carried by the four queries
and emitted by the five commands. Keep the global tag only for the sweeps that genuinely touch
everything (`DeleteObjectCommand` already lists it alongside others).

Fixing #1 and #2 together is safe; fixing #2 alone is **not** — the global tag is currently the only
thing making attribute invalidation work.

## 3. `listens:{dbref}` is cached and never invalidated by anything

```csharp
public record GetListenAttributesQuery(AnySharpObject SharpObject) : IQuery<ListenAttributeCache[]>, ICacheable
{
    public string CacheKey => $"listens:{SharpObject.Object().DBRef}";
    public string[] CacheTags => [];
}
```

`CacheTags` is empty, so `ICacheable.Profile` resolves to `CacheEntryProfile.Object` — 10-minute
duration, **fail-safe on**, eager refresh at 0.85. And `grep -rn '"listens:'` across the repository
returns exactly one hit: this line. No command emits it; no tag covers it.

Its sibling `GetCommandAttributesQuery` (`commands:{dbref}`) *is* invalidated by all five attribute
commands. The listen counterpart was simply forgotten — and the query's own doc-comment asserts the
opposite: *"Cache invalidated automatically via cache key when attribute commands execute."*

**User-visible effect:** setting or clearing a `^pattern:action` leaves the object listening on its old
pattern set for up to ten minutes.

**Fix:** add `$"listens:{DBRef}"` next to the `commands:{DBRef}` key the five commands already emit.

## 4. `ReassignAttributeOwnerCommand` invalidates nothing, and `SharpAttribute.Owner` memoises

```csharp
public record ReassignAttributeOwnerCommand(SharpPlayer OldOwner, SharpPlayer NewOwner) : ICommand;
```

No `ICacheInvalidating`, so `CacheInvalidationBehavior` does not apply to it. It rewrites the owner edge
of every attribute a player owns (`@probate` / player destruction, `BuildingCommands.cs:615`), and every
cached `attribute:#N:X` keeps the old owner.

Compounding it, `SharpAttribute.Owner` is an `AsyncLazy<SharpPlayer?>` on a value that lives in the
cache — the precise pattern #869 removed from `SharpObject` (`Location`/`Owner`/`Parent`/`Zone` became
`AsyncRelation`, resolved on every read, because *"lazies on a cached instance lived minutes and ignored
invalidation"*). `SharpAttribute` was not included in that change.

**Fix:** `ICacheInvalidating` on the command with the attribute tag from #2 (or the global one until #2
lands), and move `SharpAttribute.Owner` onto the `IObjectRelationLoader` seam like the object relations.

---

## 5. Room speech is O(N²) in room occupancy

`NotifyService.Notify(DBRef …)` runs listener routing **once per recipient**:

```csharp
await listenerRoutingService.ProcessNotificationAsync(notificationContext, what, sender, type);
...
await foreach (var conn in connections.Get(who)) await PublishMarkup(conn.Handle, what);
```

and `ProcessNotificationAsync` iterates the **entire room**, not the target:

```csharp
await foreach (var obj in mediator.CreateStream(new GetContentsQuery(location.Object().DBRef)))
{
    if (!await permissionService.CanInteract(actualSender, objAsObject, Hear)) continue;
    await ProcessListenPatternsAsync(objAsObject, messageText, actualSender);
    await ProcessListenAttributeAsync(objAsObject, messageText, actualSender);
    await ProcessPuppetRelayAsync(objAsObject, message, actualSender, type);
}
```

`context.Target` is **never read** in that file — the pass is recipient-independent by construction, so
running it per recipient repeats identical work.

`CommunicationService.SendToRoomAsync` calls `Notify` once per occupant. So one `say` in a room of N:

| per `say` | count |
|---|---|
| `CanInteract` (⇒ `LockType.Interact` evaluation) | N + N² |
| `LISTEN` attribute read | N² |
| `LockType.Use` + `LockType.Listen` evaluation (MONITOR objects) | 2N² |
| `new Regex(...)` construction on the LISTEN pattern | up to N² |
| `PUPPET`/`VERBOSE` flag + owner + location + connection scan | N² |

Twenty people in a room ⇒ ~400 listener evaluations and ~400 attribute reads per line typed. Each lock
evaluation is a compiled expression tree that blocks on DB calls (finding #12).

The design already anticipated the fix — `NotificationContext.IsRoomBroadcast` exists and is hardcoded
`false` at its only production call site.

**Fix:** hoist listener routing out of the per-recipient `Notify` and run it once per broadcast, from
`SendToRoomAsync` (and the other room-broadcast entry points), with `IsRoomBroadcast: true`. Cache the
compiled LISTEN regex alongside the pattern the way `GetListenAttributesQuery` already does for
`ListenAttributeCache.CompiledRegex` — `ProcessListenAttributeAsync` builds a fresh `Regex` instead of
using it.

## 6. Every ArangoDB write fsyncs — twice over

`WaitForSync = true` on **all 31 collections** in `Migration_CreateDatabase` (`WaitForSync = false`
appears zero times), *and* `waitForSync: true` passed explicitly on every individual document/edge write
in `ArangoDatabase.Attributes.cs`, `.Objects.cs`, `.ExpandedData.cs`, *and* on the `ArangoTransaction`
in `SetAttributeAsync`.

With the RocksDB engine that is a WAL sync per operation. `SetAttributeAsync` performs a lookup query,
then per path segment a document create + flag edges + a `HasAttribute` edge + a branch-flag probe + a
`HasAttributeOwner` edge — each one fsynced, inside a transaction that is itself `WaitForSync`. This is
the ~6.5 ms / ~6 round trips the first pass measured and left open; the round trips are only half the
story, the fsyncs are the other half.

ArangoDB's default `--rocksdb.sync-interval` is 100 ms, so dropping `waitForSync` bounds the crash
window at ~100 ms of writes. For a MUSH that is a generous durability guarantee — PennMUSH itself
checkpoints on a timer and loses minutes.

**Fix:** decide durability once, deliberately, and state it in `docs/design/engine-data-trunk.md`.
Suggested: `WaitForSync = false` on collections, no per-call `waitForSync`, keep it only on the
migration/seed writes and on `@dump`-equivalent operations. Then re-run the `profile` harness — the
first pass's `attr` scenario should move a long way.

Check Memgraph and SurrealDB for the equivalent knob at the same time, so durability is a stated policy
rather than three accidents.

## 7. ArangoDB fetches channel members one object at a time; Memgraph does not

`ArangoDatabase.Channels.cs`:

```csharp
var stream = arangoDb.Query.ExecuteStreamAsync<SharpChannelMemberListQueryResult>(handle,
    "FOR v,e IN 1..1 INBOUND @startVertex GRAPH … RETURN {Id: v._id, Status: e}", …);

return stream.Select(async (x, ct) =>
    new SharpChannel.MemberAndStatus((await GetObjectNodeAsync(x.Id, ct)).Known(), …));
```

One DB round trip **per member**, uncached (this is provider-internal, so it never reaches the mediator
or FusionCache). Memgraph returns the member nodes and their relations in the single Cypher query
(`MATCH (o:Object)-[r:ON_CHANNEL]->(c:Channel {name: $name}) RETURN o, r` + `RelationColumns("o")`).
SurrealDB is also per-member, but it is embedded and in-process, so the cost is small.

This is the same shape as the N+1 the first pass fixed for Arango attribute readers, in the collection
it did not visit. The multiplier is worse here:

- `SharpChannel.Members` is a `Lazy<FreshAsyncEnumerable<…>>` — **every enumeration re-runs the whole
  thing**, by design (so it cannot go stale), so nothing amortises it.
- `GetChannelQuery` is **not** `ICacheable`, so the channel document is re-read on every lookup too.
- `ChannelHelper.IsMemberOfChannel` answers a *boolean* by enumerating all members — O(members) round
  trips to decide one membership, and it is called from `CanSeeChannel`, i.e. on every channel name
  resolution.
- `ChannelMessageRequestHandler` does `Members.Value.ToArrayAsync()` per message.

**Fix:** project the member objects and their relations in the one AQL query (a `MERGE` sub-query, the
shape `RETURN {AttributeWithFlags}` already uses); make `GetChannelQuery` `ICacheable` with a
`channel:{name}` key — the write side *already emits* `channel:{Channel.Name}` invalidation keys that
currently match nothing; add a membership-test query instead of enumerating.

## 8. `ConnectionService.Get(DBRef)` is a linear scan, called once per delivered line

```csharp
public IAsyncEnumerable<IConnectionService.ConnectionData> Get(DBRef reference) =>
    _sessionState.Values.ToAsyncEnumerable()
        .Where(x => x.Ref.HasValue)
        .Where(x => x.Ref!.Value.Equals(reference));
```

Every `Notify`, every `Prompt`, every `NotifyExcept` (target *and* each exclusion), and
`ProcessPuppetRelayAsync` twice more, walks every connection on the server. With C connections and the
N² of finding #5 that is O(N²·C) scans per line of room speech.

It is also `IAsyncEnumerable` over an in-memory `ConcurrentDictionary` — an async state machine plus two
LINQ closures per call for zero I/O.

**Fix:** a `ConcurrentDictionary<DBRef, ImmutableArray<long>>` reverse index maintained in `Bind`/
`Unbind`, and a synchronous `ReadOnlySpan<long>`/array return for the callers that only need handles.

## 9. `SetAttributeAsync` reads outside the transaction it opened

```csharp
var transactionHandle = await arangoDb.Transaction.BeginAsync(handle, new ArangoTransaction { … });
…
var result = await arangoDb.Query.ExecuteAsync<string[]>(handle, query, …);   // ← `handle`, not `transactionHandle`
```

The query that decides which attribute nodes already exist — the basis for every create in the
transaction — runs on the plain handle. The transaction declares `Exclusive` on `Attributes`,
`HasAttribute`, `HasAttributeFlag`, `HasAttributeOwner` and `Read` on `Attributes`/`HasAttribute`
precisely so this read is serialised with the writes; passing the wrong handle discards that. Two
concurrent `&A\`B obj=…` can both observe "`A` does not exist" and both create it.

**Fix:** one character — pass `transactionHandle`. Worth a concurrency test.

## 10. `GetAttributeFlagAsync` defeats its own index, and runs at least twice per attribute write

```csharp
"FOR v in @@C1 FILTER UPPER(v.Name) == UPPER(@flag) RETURN v"
```

`UPPER()` on the indexed field forces a full collection scan. `AttributeFlags` is small, so the scan is
cheap — but it runs uncached on every attribute write (once per default flag, plus `"branch"` per path
level), and `cache: true` on the Arango side is a no-op (the server's `--query.cache-mode` defaults to
`off`, established by the first pass).

`ArangoDatabase.Objects.cs:902,915` have the same `LOWER(flag.Name) == LOWER(@flagName)` shape.

**Fix:** store a normalised `NameUpper` field and filter on it, or accept case-sensitive matching against
canonical names. Better still, cache the flag/entry definition tables — they are process-lifetime static
(`global:AttributeFlagsList` already exists as a cached read; `GetAttributeFlagAsync` bypasses it).

---

## 11. The game server runs Workstation GC; the telnet relay runs Server GC — *(proven)*

`SharpMUSH.Server` uses `Microsoft.NET.Sdk` with a `FrameworkReference` to `Microsoft.AspNetCore.App`.
`ServerGarbageCollection=true` comes from `Microsoft.NET.Sdk.Web`, which it does not use.
`SharpMUSH.ConnectionServer` *does* use `Microsoft.NET.Sdk.Web`. From the Release build:

```jsonc
// SharpMUSH.Server.runtimeconfig.json
"configProperties": {
  "System.Reflection.Metadata.MetadataUpdater.IsSupported": false,
  "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false
}

// SharpMUSH.ConnectionServer.runtimeconfig.json
"configProperties": {
  "System.GC.Server": true,          // ← only here
  …
}
```

The allocation-heavy process (ANTLR parse trees, `MString` graphs, every cache entry) gets the
lower-throughput collector; the thin byte relay gets the high-throughput one. Exactly inverted.

**Fix:** `<ServerGarbageCollection>true</ServerGarbageCollection>` in `SharpMUSH.Server.csproj`. On
.NET 10, Server GC brings DATAS on by default, which keeps the heap adaptive — relevant because
`deploy/docker-compose.cloudflare.yml` caps the server at `cpus: 1.5` / `mem_limit: 768m`. Measure both
ways under that cap before committing; on ≤2 CPUs the win is not automatic, which is the more reason to
have made the choice on purpose rather than by SDK accident.

## 12. Configuration validation never runs — *(proven)*

`Startup.cs` registers a custom `IOptionsFactory<SharpMUSHOptions>` (`OptionsService`, which reads the
options document out of the database), then `AddOptions<SharpMUSHOptions>().ValidateOnStart()`, then
`AddScoped<IValidateOptions<SharpMUSHOptions>, ValidateSharpOptions>()`.

`OptionsService.Create` returns the stored document directly. Unlike the framework's `OptionsFactory<T>`
it never runs `IConfigureOptions`, `IPostConfigureOptions` or `IValidateOptions`. And a *closed* generic
registration wins over the *open* generic `IOptionsFactory<>` that `AddOptions()` adds, so the custom
one is what resolves. A standalone program with the same three registrations:

```
IOptionsFactory<Opts> resolved as: CustomFactory
IOptions<Opts>.Value.Name = from-custom-factory
Validator ran during Value read: False
IStartupValidator.Validate() completed WITHOUT error   ← validator returns Fail() and is never called
Validator ran overall: False
```

So `ValidateSharpOptions` — the generated per-field validator plus the hand-written
`Wiki.DefaultLocale` culture check — is dead code, and `ValidateOnStart()` is a no-op. The Startup
comment (*"Registering the generated validator here would silently skip those"*) shows live validation
was the intent.

**Fix:** have `OptionsService` take `IEnumerable<IValidateOptions<SharpMUSHOptions>>` and run them (or
derive from `OptionsFactory<SharpMUSHOptions>` and override only the source of the value). Make the
validator a singleton while you are there — a scoped `IValidateOptions` consumed by singleton options
plumbing is a captive dependency waiting to throw under `ValidateScopes`. Add a test that a bad stored
config fails startup, otherwise this regresses silently again.

`OptionsService.Create` also does `.AsTask().ConfigureAwait(false).GetAwaiter().GetResult()` on a
database read — on whatever thread `ConfigurationReloadService`'s change token fires.

## 13. Player-supplied regex has no match timeout, on a single-threaded queue

~30 `new Regex(...)` sites outside tests build patterns from softcode — `regmatch()`, `regeditall()`,
`^listen` patterns via `getWildcardMatchAsRegex`, `@grep`, `@scan`, wildcard `lattr`. Only three sites
anywhere pass a `matchTimeout` (`UtilityFunctions.cs:779` is the one that does it right), and
`RegexOptions.NonBacktracking` is used nowhere.

Quartz is configured `UseDefaultThreadPool(tp => tp.MaxConcurrency = 1)` to preserve PennMUSH FIFO
ordering. So one catastrophic-backtracking pattern from any builder stops the entire game, not one
command.

**Fix:** a single helper that constructs every player-derived `Regex` with a bounded `matchTimeout`
(and `NonBacktracking` where the pattern has no backreferences), with `RegexMatchTimeoutException`
mapped to `#-1 REGEXP TIMEOUT`. Two nearby nits: `CommandAttributeScanner.cs:105` and
`ValidateService.cs:199` pass `RegexOptions.Compiled` on regexes built per call — compilation costs far
more than it saves unless the instance is reused.

## 14. Fire-and-forget `ValueTask`, and `.Result` on the command bus

```csharp
// ListenerRoutingService.cs:132 and :178
_ = mediator.Send(new ExecuteListenPatternCommand(listener, speaker, triggerAttrName, registers));

// LockService.cs:190
_ = med.Send(new SetLockCommand(lockee.Object(), standardType.ToString(), lockString)).AsTask().Result;
```

Discarding a `ValueTask` without awaiting is documented as invalid — the backing `IValueTaskSource` may
be pooled and recycled under you — and any exception becomes unobserved. It also drops the FIFO ordering
guarantee the queue exists to provide. The third blocks a thread on a write.

`NotifyService.Notify` additionally wraps the whole listener-routing call in `catch { }` — including
`OperationCanceledException`, so a cancelled notify looks identical to a healthy one.

**Fix:** await, or route through `ITaskScheduler` (which is what the queue is for) and log failures.

## 15. Lock evaluation is sync-over-async by design

`SharpMUSHBooleanExpressionVisitor` has ~30 `GetAwaiter().GetResult()` calls on database-touching
awaitables, because the visitor builds `Expression` trees and the tree body must be synchronous. Every
`@lock` check — movement, speech, `CanInteract`, the N² of finding #5 — blocks a thread pool thread for
however long those reads take.

The first pass made most of those reads cache hits, which is why this has not bitten yet. It is still
the largest remaining structural risk: with the compiled-expression cache cold, or a slow DB, this is a
thread-pool starvation path, and `Startup` gives the command queue exactly one thread.

**Not a quick fix** — it needs the lock expression compiled to a `Func<…, ValueTask<bool>>` rather than a
sync `Func`. Worth an ADR before anyone attempts it. Recording it here so the next pass does not
rediscover it.

---

## Packages, AOT and trimming

**Native AOT for the engine is not reachable and should not be a goal.** ANTLR's runtime, `Core.Arango`,
`Neo4j.Driver`, `SurrealDb.Net`, Quartz and `McMaster.NETCore.Plugins` (which needs a JIT to load plugin
assemblies at all) are each individually disqualifying. The plugin system is a first-class feature; AOT
would delete it. What *is* reachable and worth doing:

| Change | Why |
|---|---|
| `<ServerGarbageCollection>true</ServerGarbageCollection>` on `SharpMUSH.Server` | Finding #11. Highest ratio of impact to effort in this document. |
| `<PublishReadyToRun>true</PublishReadyToRun>` on both hosts | Cuts cold-start JIT. Costs image size; the containers already pull a full SDK-built layer. |
| `System.Text.Json` source generation (`JsonSerializerContext`) | 57 `JsonSerializer` call sites, **zero** `JsonSerializerContext`. Lets `SharpMUSH.Client` stop rooting whole assemblies (`TrimmerRootAssembly Include="SharpMUSH.Contracts"`, `"MudBlazor"`) under `PublishTrimmed`, which today gives back most of what trimming buys. |
| `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>` on `SharpMUSH.Contracts` / `.MarkupString` / `.Client` | The client already trims; nothing currently catches trim-unsafe code at build time, so failures only appear in a published build. |
| `ZiggyCreatures.FusionCache` 2.5.0 → 2.7.x + `ZiggyCreatures.FusionCache.OpenTelemetry` 2.7.2 | The instrumentation package exposes hit/miss/stale/factory counters per cache. Findings #1–#3 would have shown up as a visible hit-rate collapse the day they landed. |
| A `IPipelineBehavior` emitting a duration histogram + DB-request counter per request type | `TelemetryService` already owns a `Meter("SharpMUSH")` with function/command/notification histograms; there is no per-query or per-command-handler signal, and no `ActivitySource`/tracing at all. This is what makes the whole class of findings above measurable in production instead of only in the `profile` harness. |
| `Microsoft.Extensions.Http.Resilience` 10.9.0 on the Arango + `"api"` clients | No retry/timeout/circuit-breaker on any outbound HTTP today. Also the place to finally set `DefaultRequestVersion` (the first pass noted `Core.Arango` speaks HTTP/1.1 unless the injected `HttpClient` says otherwise). |
| `Directory.Packages.props` (central package management) | There is no CPM file. One real drift already exists — `Microsoft.CodeAnalysis.CSharp` 5.0.0 vs 5.3.0 — and it currently emits `NU1603` on every restore. |

**Package hygiene, verified:**

- `System.Data.SqlClient 4.9.1` **and** `Microsoft.Data.SqlClient 7.0.1` are both referenced from
  `SharpMUSH.Library`. The former is the deprecated one; drop it.
- `System.Private.Uri 4.3.2` and `System.Text.RegularExpressions 4.3.1` are referenced from
  `SharpMUSH.Client`, `SharpMUSH.Database` and `SharpMUSH.Library`. These are the .NET Standard 1.x
  out-of-band packages; on `net10.0` the types come from the shared framework, so the references do
  nothing but add restore noise and confuse the trimmer. (They look like CVE-audit remediations that
  are no longer needed at this TFM — worth confirming against whatever flagged them before removing.)
- `OpenTelemetry.ResourceDetectors.Container 1.0.0-beta.7` is the only OTel package still on a
  prerelease while the rest of the stack is on 1.15.x. Check whether it has a stable successor.
- `NSubstitute 5.3.0` is a `PackageReference` of the production `SharpMUSH.Server` project. Its only
  mention in that project's source is a doc-comment. It should be a test-project dependency.
- `Core.Arango` was archived upstream on 2026-08-02 at 3.12.3 (recorded by the first pass).
  `ArangoDBNetStandard` 3.1.0 is the maintained alternative. Not urgent, but the default backend now
  depends on a dead driver, and finding #6 and #7 both want changes in that layer — worth an ADR before
  more work accretes on top of it.

`System.Linq.Async` is already handled correctly (`ExcludeAssets="compile"` everywhere, so .NET 10's
built-in `System.Linq.AsyncEnumerable` wins at compile time) — noting it so a future pass does not
"fix" it.

---

## Suggested order

1. **#1 + #2 together** — cache keys and the per-object attribute tag. Correctness plus the single
   biggest cache-effectiveness win. Do not split them.
2. **#3, #4, #9** — small, isolated, each a real bug.
3. **#11** — one line, measure under the container's CPU cap.
4. **#6** — decide durability, write it down, re-run the `profile` harness's attribute scenario.
5. **#5** — the largest throughput win, and the largest change of the set. Wants its own branch.
6. **#7, #8, #10** — broadcast and channel path.
7. **#12, #13, #14** — reliability; independent of everything above.
8. **#15** — ADR first.

Every one of #1–#10 should be measured with the `profile` harness before and after, checking
`arango req/op` the way the first pass established. #5 and #7 need a scenario the harness does not yet
have: N occupants in a room, one `say`; and an N-member channel, one `@chat`. Both are small additions
next to the existing `lcon`/`loc` scenarios and would have made these findings self-evident.
