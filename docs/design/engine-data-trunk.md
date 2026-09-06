# Engine Data Trunk

Binding decisions for how the game engine reads and writes state, how services are wired, and
what the cache may assume. The portal decisions live in `architectural-decisions.md`; this
document covers the engine underneath them. Performance evidence for these choices is in
`../performance/2026-09-parser-and-database-analysis.md`.

## 1. The Mediator is the data trunk

**Decision:** Every read and write of game state is a request type in `SharpMUSH.Library`
(`Queries/Database`, `Commands/Database`), handled by a handler in `SharpMUSH.Implementation`
that calls a store. Caching policy is declared on the request, not written in the handler:

- a query implements `ICacheable` and states its `CacheKey`, `CacheTags` and, derived from the
  tags, its `Profile` (`Object` for key-invalidated entries, `Tagged` for everything a tag can
  expire, `Scan` for bounded listings);
- a write implements `ICacheInvalidating` and states the keys and tags it removes; a write whose
  keys are only known from its result, such as a create allocating a dbref, also implements
  `ICacheInvalidatingByResult<T>`.

Three pipeline behaviours apply that policy: `QueryCachingBehavior`, `StreamQueryCachingBehavior`
and `CacheInvalidationBehavior`. No handler holds an `IFusionCache`. Anything that reads an
aggregate through the Mediator writes it only through the Mediator; a direct store write leaves
the cache stale for the entry's whole lifetime.

The object is the cache unit. Flags, powers and locks load with the object, and every cached
result carries an `obj:#N` tag per object it embeds, so a write to `object:#N` expires every
snapshot of that object anywhere in the cache. A loaded `SharpObject` is a snapshot; a handler
that mutates one calls its `With…`/`Without…` methods and invalidates the key.

**Why not decorators or per-method policy:** a caching decorator over the provider surface puts
keys and tags on 174 methods instead of 21 request types, and the audit that pins the fail-safe
rule (`CacheEntryProfileTests`) is only possible because policy is data on the request.

## 2. Stores, not one database interface

**Decision:** `ISharpDatabase` is a composite of per-aggregate store interfaces in
`SharpMUSH.Library/Stores`: `IDatabaseLifecycle`, `IObjectStore`, `IFlagAndPowerStore`,
`INavigationStore`, `IAttributeStore`, `IMailStore`, `IExpandedDataStore`, `IChannelStore`,
`IAccountStore`, `IServerStateStore`, `ISessionRecordStore`. Each provider implements the
composite; each handler and service depends on the store it uses. The concrete provider is the
one registered singleton, and every interface it serves (the stores, `IWikiService`,
`IRoleRegistryService`, the package, application and layout registries, the storage accessor)
is forwarded from it by a compile-checked registration, never by a cast.

Requests and store methods are two vocabularies kept in sync by hand. Small stores make drift
visible, and a handler that needs two stores says so in its constructor.

## 3. Providers know nothing above them

**Decision:** A provider maps storage to an aggregate. It takes no `IMediator` and no cache.
Relations another actor can change under an object (location, owner, parent, zone, home, a
room's drop-to, an exit's destination) resolve through `IObjectRelationLoader`, a seam owned by
`SharpMUSH.Library` and implemented in the host over the Mediator's cached, tagged queries. A
provider-built object memoises nothing about another object (`AsyncRelation<T>`), and an object
built by a query that did not project its flags and powers throws on first use rather than
falling back to a read.

## 4. No static service locator

**Decision:** `Commands` and `Functions` are ordinary DI-constructed classes whose command and
function methods are instance methods; the source generators emit a `Create(instance)` factory
that binds each entry to the instance. Services are non-null members, so no `Service!` appears
in a command. Plugin modules keep static methods and the generators keep emitting a static table
for those, which `PluginBase` reads by reflection.

## 5. Cycles resolve lazily, not through the bus

**Decision:** The lock service owns the boolean-expression parser, and the locate and attribute
services reach the lock service through permissions. A compiled lock reaches those services
through `ILockEvaluationServices`, whose implementation takes `Lazy<T>` for each and resolves
them on first use. `Lazy<>` is registered as an open generic (`LazyService<T>`). Requests whose
handler only called a service (`GetAttributeServiceQuery`, `LocateObjectQuery`,
`EvaluateLockQuery`, `EvaluateAttributeForLockQuery`) do not exist; the Mediator carries data
requests and events, not service calls.

## 6. Migration is awaited once, after the host is built

**Decision:** The provider's factory constructs it and nothing else. `Program` awaits
`IDatabaseLifecycle.Migrate()` immediately after `builder.Build()`, before any service that reads
the database is resolved. A hosted lifecycle hook is too late: the host constructs every hosted
service before it runs `StartingAsync`, and constructing them resolves the options factory,
which reads server data. Test hosts and benchmarks run `Program`, so they migrate the same way
and call nothing themselves.

## 7. Coherence is by version where a tag cannot reach

**Decision:** Tagged entries are stamped by FusionCache at factory start, so a tag removed
during a read expires the result. A key-invalidated object entry has no such stamp, so
`ObjectVersions` keeps a monotonic version per object number: every removal of `object:#N`
bumps it before the key goes, and `QueryCachingBehavior` compares the version after its store
and removes the entry it just stored if the version moved. The comparison is after the store,
not before it, so the removal happens after the store in every interleaving.

## 8. One engine process, stated

**Decision:** The engine cache is the in-memory FusionCache only. `ObjectVersions`, the in-memory
tag index, `AsyncRelation` and the snapshot rule all assume one engine process per database, and
a second engine node would leave the first node's entries stale. The path to more than one node
is a configured FusionCache backplane, which carries key and tag removals between nodes, with
`ObjectVersions` moved into the shared cache. Until that is configured, run one engine process.
