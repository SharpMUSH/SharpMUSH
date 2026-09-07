# Second-pass performance, caching and DI analysis — 2026-09-06

Baseline: `origin/main` @ `26dea287` (all of the first pass — #867, #869, #870, #871 — merged).

The first pass took the **parser and the object/attribute read path** from DB-bound to cached. This
pass deliberately looked everywhere that pass did not: the **write** path, the **broadcast** path
(say / pose / channels), the **process configuration**, and the **package surface**. Everything below
was verified by reading `origin/main` and, where marked *(proven)*, by running code against the real
assemblies.

Nothing here contradicts the first pass. Several findings are the *same class of bug* the first pass
fixed, in a place it did not look.

> **Status.** Fixed on `claude/attribute-cache-tags-and-gc`, with tests: findings 1, 3, 4, 5, 9, 11,
> 12, 13, 14, and 2 in part. Finding 2 is fixed for the attribute reads that consult one object; the
> inherited reads keep a game-wide tag deliberately (see `CacheKeys.AttributesTag`), and closing that
> needs the providers to project the objects a read visited — the same prerequisite as the
> `commands:`/`listens:` parent-chain gap recorded on those queries.
>
> **6** (`WaitForSync`): durability is unchanged and stays on. What went was the per-operation flags
> *inside* a stream transaction, which cannot sync anything — see that section. **7**: the Arango
> member N+1 is fixed; caching the channel document itself is still open. **15**: the DotNext route was
> tried and measured, and it miscompiles the boolean operators — see that section for the result and
> the route that does work. **8** and **10** were re-examined and are not worth doing.

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

It is also a correctness bug, not only a cost: each of those N passes queues an
`ExecuteListenPatternCommand` for every listener that matches, so a single `say` fires each listener N
times.

**Fixed** by scoping the pass to `context.Target` rather than hoisting it to the broadcast. Each of the
three things it does is about the object that heard the message — its `^`-patterns match what it heard,
its `LISTEN` is its own, a puppet relays what it was told — so the addressee is the right subject, the
broadcast still reaches every occupant through the notification addressed to each of them, and each is
weighed exactly once. The dead `NotificationContext.IsRoomBroadcast` (never read; hardcoded `false` at
its only call site) went with it. A targeted `@pemit` now runs only the recipient's listeners instead
of the whole room's, which is what it always should have done.

**Still open here:** `ProcessListenAttributeAsync` builds a fresh `Regex` per call instead of using the
compiled one `GetListenAttributesQuery` already caches in `ListenAttributeCache.CompiledRegex`.

## 6. `waitForSync` inside a stream transaction does nothing — *fixed, durability unchanged*

`WaitForSync = true` is set on all 31 collections in `Migration_CreateDatabase` (`false` appears zero
times), on the `ArangoTransaction` in `SetAttributeAsync`, **and** on every individual document and edge
operation inside that transaction.

The per-operation flags cannot do anything. ArangoDB's own documentation is explicit: with the RocksDB
engine, *"the actual data modification operations of a transaction are only written to the write-ahead
log on commit"*, and operations are applied in main memory until then. There is nothing written for a
per-operation sync to flush. The transaction's own `WaitForSync` is the durability point.

**Fixed** by removing the flags that were no-ops, inside the transaction only. Every collection keeps
`WaitForSync = true`, the transaction keeps it, and the standalone writes keep theirs. No durability
guarantee changes.

**What this does not fix, and where the time actually goes.** `SetAttributeAsync` is ~6 sequential HTTP
round trips to ArangoDB — a lookup, then per path segment a document create, its flag edges, a
`HasAttribute` edge, a branch-flag probe and a `HasAttributeOwner` edge. That, not fsync, is the ~6.5 ms
the first pass measured. Collapsing it into one AQL statement inside the same transaction would keep the
durability guarantee exactly as it is and remove five round trips; that is the change worth making here.

## 7. ArangoDB fetched channel members one object at a time — *fixed*

`GetChannelMembersAsync` ran one query for the membership edges and then one `GetObjectNodeAsync` **per
member**, uncached (provider-internal, so it never reaches FusionCache). Memgraph already returned the
member nodes and their relations in a single Cypher query; Arango was the outlier, as it was for the
attribute readers the first pass fixed.

The multiplier is what made it matter: `SharpChannel.Members` is a `Lazy<FreshAsyncEnumerable<…>>` that
re-runs on every enumeration by design, and `GetChannelQuery` is not `ICacheable`, so nothing amortised
it. `ChannelMessageRequestHandler` enumerates the whole member list per message. A hundred-member
channel meant a hundred round trips per line of chat.

**Fixed:** the membership edge points at the member's `Objects` document and its typed vertex is one
inbound hop away, so the traversal projects both and hands them to the same builder the single-object
load uses (extracted as `BuildObjectNode`). One round trip for the list.

**Still open, deliberately:** the channel document itself is not cached, so a lookup by name is still a
read per `@chat`; `ChannelHelper.IsMemberOfChannel` answers a boolean by enumerating every member; and
the write side already emits `channel:{name}` invalidation keys that match no read key, because there
is no cached read to match. Making `SharpChannel` object-shaped and cached is the same shape of work
#871 did for objects, and it wants a harness scenario written first.


## 8. `ConnectionService.Get(DBRef)` is a linear scan, called once per delivered line — *not worth fixing*

**Re-examined and left alone.** With the listener pass no longer quadratic (finding 5), this is O(N·C)
per broadcast — a few thousand dictionary iterations for a busy room on a busy server, which is
microseconds. A reverse index would have to be kept in step across `Register`, `Bind`, `Unbind`,
`Disconnect` and the reconciliation service, and a stale entry there hands a message to the wrong
connection. That is a bad trade for the time it saves. Recorded so the next pass does not re-derive it.



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

## 10. `GetAttributeFlagAsync` defeats its own index — *not worth fixing*

**Re-examined and left alone.** `UPPER()` on the indexed field does force a scan, but `AttributeFlags`
holds about twenty documents; the scan is a rounding error next to the fsync the same write pays
(finding 6). Making it index-using needs a normalised stored field and a migration across three
providers. Not a defect, and not where the time is.



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
`@lock` check — movement, speech, `CanInteract` — blocks a thread pool thread for however long those
reads take, and `Startup` gives the command queue exactly one thread.

### DotNext's async lambda cannot express this — measured, 2026-09-06

The obvious route is `DotNext.Metaprogramming`'s `CodeGenerator.AsyncLambda`, which does support a
`ValueTask<T>` return type and an `AwaitExpression` inside arbitrary statements. **It miscompiles the
short-circuiting boolean operators**, which is the entire structure of a lock expression.

Reproduced against `DotNext.Metaprogramming` 6.6.2 with this leaf, where each operand is an awaited
`ValueTask<bool>`:

```csharp
static Expression Answers(bool value)
{
    Func<ValueTask<bool>> f = async () => { await Task.Yield(); return value; };
    return Expression.Invoke(Expression.Constant(f)).Await();
}

var compiled = CodeGenerator
    .AsyncLambda<Func<ValueTask<bool>>>((_, result) => CodeGenerator.Assign(result, body))
    .Compile();
```

| `body` | expected | DotNext |
|---|---|---|
| `AndAlso(Answers(false), Answers(true))` | `false` | **`true`** |
| `OrElse(Answers(true), Answers(false))` | `true` | **`false`** |
| `AndAlso(Answers(true), Answers(false))` | `false` | `false` |
| `AndAlso(Answers(false), Answers(false))` | `false` | `false` |

Only the asymmetric cases are wrong, and the right-hand operand runs even when the left has already
decided — so the operands are being transposed, and `a&b` reads the database for `b` regardless. The
documented `(fun, result)` form and `Return(expression)` behave identically. Every compound lock in
the game would have silently inverted.

### The route that does work: drop the expression trees

The visitor already builds each leaf as an ordinary C# `Func<...>` and only wraps it in
`Expression.Constant` to put it in a tree — there are fifteen such sites and every one of them is
`Expression.Invoke(Expression.Constant(func), …)`. Nothing needs a tree. Changing the visitor's type
parameter from `Expression` to `Func<AnySharpObject, AnySharpObject, ValueTask<bool>>` makes the leaves
async lambdas and the combinators ordinary C#:

```csharp
// AndAlso, with the short-circuit the language already guarantees
(g, u) => await left(g, u) && await right(g, u);
```

That is correct by construction, needs no third-party rewriter, and drops `Expression.Lambda().Compile()`
— runtime IL generation, which is both a per-lock cost and the reason the lock path can never be
AOT-compatible.

**Remaining work, and why it is not in this branch:** the visitor rewrite is self-contained (686 lines,
fifteen leaf sites), but `IBooleanExpressionParser.Compile`, `ILockService.Evaluate` and
`IPermissionService.PassesLock` all become async, and that reaches about sixty call sites — most already
inside `async` methods, but `PermissionService` alone has sixteen. It is a single coherent change and
wants its own review, not a tail on this one.

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
