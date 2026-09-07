# LMDB storage provider (`SharpMUSH.Database.Lightning`) — design

**Date:** 2026-09-06
**Status:** Approved for implementation (Option 1: full parity; Approach A: direct key-value provider)
**ADR:** `docs/design/adr-storage-engine.md` (appendix: substrates). Dossier: the SharpMUSH Storage Dossier artifact.

## 1. Goal

Add a fourth `SHARPMUSH_DATABASE_PROVIDER` value, `lightning`, backed by LMDB 1.0.1 through the
`LightningDB` NuGet package (Lightning.NET 0.23.x), implementing every interface the other providers
implement: `ISharpDatabase`, `IStagingDatabase`, `IWikiService`, `IPackageRegistryService`,
`IRoleRegistryService`, `ILayoutRegistryService`, `IApplicationRegistryService`, and the Scene plugin's
`ISceneStorage` through a new `ILightningStorageAccessor`. Done means the whole test suite (unit and
integration, including wiki and scene) is green under `lightning`, and the benchmark project reports the
existing seven operations plus an extended suite for all four providers.

Also in scope: the shared `SharpMUSH.Database` project stops referencing `Core.Arango` and
`Core.Arango.Migration`; the ArangoDB provider references them itself.

Not a data migration: there is no SurrealDB-to-LMDB path. The project is pre-production; a lightning
database starts from the initial seed.

## 2. Non-goals (deferred, see ADR action items)

- In-memory adjacency index and bounded object cache (route finding, grid reachability). The interface
  today needs neither; `IsReachableViaParentOrZoneAsync` is a bounded loop over two edge tables.
- Group commit (several write jobs per fsync). One job per commit meets the ArangoDB provider's
  `waitForSync` bar; revisit with benchmark numbers.
- Switching ArangoDB and Memgraph to the shared seed data (they keep their copies).
- `ISharpDatabaseWithLogging` (only ArangoDB implements it; the handler degrades gracefully).

## 3. Constraints that shape the design

- **Disk-resident by construction.** The store is the source of truth. No method loads the whole game.
  Scan-shaped members stream; nothing materialises a full table into a list.
- **Every commit durable.** LMDB default flags: data pages written and fdatasynced, then meta page.
- **Single writer.** LMDB serialises writers and binds a write transaction to the thread that began it.
  The provider therefore owns one writer thread. Callers are concurrent (portal controllers, Quartz,
  NATS consumers, the command queue); they never see a conflict, they wait in a queue.
- **Read transactions never span an `await`.** A pinned snapshot stops page reuse and grows the file.
- **Values up to tens of KB.** Page size 16 KB keeps such values inline (LMDB stores values up to about
  half a page in-node; larger go to overflow pages). Key limit at 16 KB pages is a few KB, ample for
  `dbref + long attribute name`.
- **Repo conventions.** net10.0, tabs indent 2, `TreatWarningsAsErrors`, `OneOf` returns, TUnit,
  `FreshAsyncEnumerable` for every `IAsyncEnumerable` member (issue #798), FORMAT001 gate.

## 4. Project layout

```
SharpMUSH.Database.Lightning/
  SharpMUSH.Database.Lightning.csproj     LightningDB, System.Linq.Async (ExcludeAssets=compile);
                                          refs SharpMUSH.Database, SharpMUSH.Contracts
  GlobalUsings.cs                         MModule / MString aliases (copy of SurrealDB's)
  Store/
    LightningStore.cs                     environment lifetime, sub-database handles, open/close/swap
    LightningStoreOptions.cs              Path, MapSize (default 64 GiB), MaxReaders (256), PageSize (16 KB)
    LightningWriter.cs                    dedicated writer thread + Channel<WriteJob>
    LightningReader.cs                    read-transaction scope helpers, chunked range enumeration
    Keys.cs                               big-endian key encoders/decoders, prefix helpers
    Tables.cs                             the sub-database catalogue (names, flags, key/value shapes)
    Codec.cs                              System.Text.Json source-generated context for record types
    LightningStoreException.cs            map full / txn full / readers full mapped to named settings
  Records/                                provider-private storage records (ObjectRecord, AttrMeta, …)
  LightningDatabase.cs                    ctor, Migrate(), WipeDatabaseAsync, CreateStagingAsync, shared helpers
  LightningDatabase.Objects.cs            create/get/delete/set*, GetFilteredObjectsAsync, counts
  LightningDatabase.Attributes.cs         get/set/clear/wipe, patterns, inheritance, attribute entries
  LightningDatabase.FlagsAndPowers.cs
  LightningDatabase.Navigation.cs         location/contents/exits/parents/zones/reachability/move
  LightningDatabase.Channels.cs
  LightningDatabase.Mail.cs
  LightningDatabase.Accounts.cs
  LightningDatabase.Sessions.cs
  LightningDatabase.ServerState.cs
  LightningDatabase.ExpandedData.cs
  LightningDatabase.Wiki.cs
  LightningDatabase.Layouts.cs
  LightningDatabase.Applications.cs
  LightningDatabase.Roles.cs
  LightningDatabase.Packages.cs
  LightningDatabase.StorageAccessor.cs    ILightningStorageAccessor
  LightningStagingDatabase.cs
  Migration/
    LightningMigration.cs                 schema (open all tables), idempotent seeds, migration ids
SharpMUSH.Database/Seed/                  hoisted seed data (see §8)
SharpMUSH.Plugins.Scene/Storage/LightningSceneStorage.cs
SharpMUSH.Library/Plugins/Storage/ILightningStorageAccessor.cs
SharpMUSH.Library/Plugins/IMigrationSource.cs   + LightningSteps
```

The provider class is `public sealed partial class LightningDatabase : ISharpDatabase, IWikiService,
IPackageRegistryService, IRoleRegistryService, ILayoutRegistryService, IApplicationRegistryService,
ILightningStorageAccessor`. Constructor: `(ILogger<LightningDatabase>, LightningStoreOptions,
IPasswordService, IReadOnlyList<IMigrationSource>? = null, IReadOnlyList<PluginFlag>? = null)`.

## 5. Store layer

### 5.1 Environment

`LightningStore` opens one `LightningEnvironment` at `options.Path` with
`EnvironmentOpenFlags.NoThreadLocalStorage`, `MapSize = options.MapSize`, `MaxDatabases = 64`,
`MaxReaders = options.MaxReaders`, `PageSize = options.PageSize` (applies at creation only). It opens
every table in `Tables` inside one write transaction with `DatabaseOpenFlags.Create` plus the table's
flags, and caches the `LightningDatabase` handles. Reads and writes go through the store so the
environment handle can be swapped during staging promotion under a reader-writer lock: readers take the
read side per transaction, promotion takes the write side after the writer thread drains.

### 5.2 Writer

`LightningWriter` starts one background thread (`IsBackground = true`, named `lightning-writer`) reading
a bounded `Channel<WriteJob>` (capacity 10 000, `SingleReader = true`). A job is
`Func<LightningTransaction, TableSet, object?>` plus a `TaskCompletionSource<object?>`. The thread
begins a write transaction, runs the delegate, commits, completes the task; on exception it aborts and
faults that task only. Callers use `ValueTask<T> WriteAsync<T>(Func<ITx, T> job, CancellationToken)`.
Cancellation before dequeue cancels the job; after dequeue it runs to completion. Dispose completes the
channel and joins the thread. The next-dbref counter lives in `meta` and is read-increment-written inside
the creating job, so allocation is serialised without extra locking.

### 5.3 Reader

`LightningReader.Read<T>(Func<ITx, T>)` begins a read-only transaction on the calling thread, runs the
delegate synchronously, disposes, returns the copied result. `MDBValue` spans are never returned;
results are decoded inside the scope. `ReadRange(table, prefix, pageSize = 256)` is an
`IAsyncEnumerable` that reads up to `pageSize` entries per transaction and resumes with `SetRange` on
the last key, so consumers may `await` between items without pinning a snapshot. Every
`IAsyncEnumerable` member of the provider wraps its enumeration in `FreshAsyncEnumerable`.

### 5.4 Keys and codec

Keys are byte arrays composed of fixed-width big-endian integers and UTF-8 strings with a `0x00`
separator where two variable parts meet. dbrefs are `ulong` (8 bytes). Attribute long names are stored
upper-cased as the engine already does. `Keys` exposes typed encoders (`Obj(dbref)`, `Attr(dbref,
longName)`, `Edge(from, to)`, …) and prefix builders. Values are JSON produced by a source-generated
`JsonSerializerContext` over the `Records` types (`PropertyNamingPolicy = null`, no indentation), except
markup strings which use `MModule.serialize` and are stored as UTF-8 bytes. JSON is chosen over binary
serialisation for inspectability with `mdb_dump`; the benchmark will show whether that matters.

## 6. Schema (sub-database catalogue)

Flags: `D` = `DuplicatesSort`, `F` = `DuplicatesFixed`. LMDB's integer-key flags are not used: keys are
big-endian bytes, which sort correctly under the default comparator and support prefix ranges. Unless
noted, keys are unique and values are JSON records.

| Table | Flags | Key | Value | Purpose |
|---|---|---|---|---|
| `meta` | | string | JSON / u64 | applied migration ids (`mig:<id>` → timestamp), `next_dbref`, `schema_version` |
| `obj` | | dbref u64 | `ObjectRecord` {Name, Type, Aliases[], CreationTime, ModifiedTime, PasswordHash, PasswordSalt, Quota, Warnings, Locks (JSON map)} | objects |
| `obj.name` | D,F | lower(name or alias) | dbref | player lookup by name or alias |
| `attr.meta` | | dbref + 0x00 + LONGNAME | `AttrMeta` {Owner dbref?, Flags[], EntryName?} | attribute metadata; range-scan for listing |
| `attr.val` | | same key | UTF-8 serialized MString | attribute values, read only on demand |
| `attr.flag` | | name | `AttributeFlagRecord` | `@attribute/flags` definitions |
| `attr.entry` | | NAME | `AttributeEntryRecord` {DefaultFlags[], Limit, Enum} | `@attribute` dictionary |
| `flag` | | NAME | `FlagRecord` {Symbol, Aliases[], SetPerms[], UnsetPerms[], TypeRestrictions[], System, Disabled} | object flags |
| `power` | | NAME | `PowerRecord` | powers |
| `e.loc` / `r.loc` | D,F | dbref | dbref | at_location forward / reverse |
| `e.home` / `r.home` | D,F | dbref | dbref | has_home |
| `e.owner` / `r.owner` | D,F | dbref | dbref | has_object_owner |
| `e.parent` / `r.parent` | D,F | dbref | dbref | has_parent |
| `e.zone` / `r.zone` | D,F | dbref | dbref | has_zone |
| `e.exit` / `r.exit` | D,F | dbref | dbref | has_exit (room → exit) |
| `e.dest` / `r.dest` | D,F | dbref | dbref | exit destination, only if the existing providers model it as a distinct edge; if they reuse the home or location edge for exits (see `ArangoDatabase.Objects.cs` `GetEntrancesAsync`), reuse those tables and drop this row |
| `e.flag` / `r.flag` | D | dbref / NAME | NAME / dbref | object has flag |
| `e.power` / `r.power` | D | dbref / NAME | NAME / dbref | object has power |
| `chan` | | NAME | `ChannelRecord` {Name, Description, Privs, JoinLock, SpeakLock, SeeLock, HideLock, ModLock, Mogrifier?, Buffer, Owner dbref} | channels |
| `chan.member` | | NAME + 0x00 + dbref | `ChannelMemberRecord` {Gagged, Mute, Hide, Combine, Title} | membership with status |
| `r.chan.member` | D | dbref | NAME | channels a player is on |
| `mail` | | mail id u64 | `MailRecord` {Sender dbref, Recipient dbref, DateSent, Fresh, Read, Tagged, Urgent, Forwarded, Cleared, Folder, Subject, Content} | mail |
| `mail.box` | | recipient dbref + mail id | empty | ordered per-recipient index (`@mail N` is positional) |
| `mail.sent` | | sender dbref + mail id | empty | sent lookup |
| `account` | | id string | `AccountRecord` | web accounts |
| `account.email` | | lower(email) | id | sparse unique |
| `account.user` | | lower(username) | id | unique |
| `e.acct.char` / `r.acct.char` | D | account id / dbref | dbref / account id | account owns character |
| `e.acct.role` | D | account id | role slug | account has role |
| `session` | | token | `SessionRecord` {AccountId, OriginIp, ExpiryUnixMs, …} | sessions |
| `session.acct` | D | account id | token | delete-by-account |
| `session.ip` | D | origin ip | token | delete-by-ip, distinct ips |
| `state` | | `"state"` | `ServerStateRecord` | server state |
| `x.obj` | | dbref + 0x00 + type | JSON | expanded data per object per type |
| `x.srv` | | type | JSON | expanded server data |
| `wiki.page` | | page id | `WikiPageRecord` | wiki pages |
| `wiki.slug` | | lower(slug) | page id | unique slug |
| `wiki.rev` | | page id + 0x00 + locale + 0x00 + rev u32 | `WikiRevisionRecord` | revisions, ordered |
| `wiki.tr` | | page id + 0x00 + locale | `WikiTranslationRecord` | translations |
| `layout` | | scope | JSON `LayoutConfiguration` | layouts |
| `app` | | slug | `ApplicationRecord` | applications |
| `role` | | slug | `RoleRecord` (permissions as JSON map) | roles |
| `pkg.*` | | per record type composite key | JSON | packages: `pkg`, `pkg.obj`, `pkg.attr`, `pkg.struct`, `pkg.remote`, `pkg.rev`, `pkg.dep` (D) |
| `scene.*` | | per scene record | JSON | owned by the Scene plugin through the accessor: `scene`, `scene.part`, `scene.pose`, `scene.log`, indexes as the plugin needs |

`Tables` is a single static catalogue: name, flags, and a `Kind` (`Node`, `ForwardEdge`, `ReverseEdge`,
`Index`). The object-deletion cascade iterates every table whose `Kind` is an edge or index keyed by
dbref; a test asserts the catalogue contains no edge table that the cascade skips, and that every table
name the environment reports on disk is in the catalogue.

Uniqueness (channel name, account email, username, wiki slug) is enforced inside the writer job by a
`Get` before `Put`; because there is one writer, check-then-put is atomic.

## 7. Method strategies

- **Object create.** One job: increment `next_dbref`, put `obj`, put `obj.name` for name and aliases,
  put owner/location/home edges both directions. Returns the dbref.
- **Object delete.** One job: delete `obj`, `obj.name` entries, all `attr.meta`/`attr.val` under the
  dbref prefix, `x.obj` prefix, received mail and its indexes, then every edge table in both directions
  (forward by prefix, reverse by scanning the reverse table for the dbref as value is avoided by always
  maintaining both directions and deleting by key on each side).
- **GetObjectNodeAsync.** Point read of `obj`; type comes from the record, so the typed-vertex hop the
  ArangoDB layout paid is gone.
- **GetFilteredObjectsAsync.** Range over `obj` (optionally bounded by `MinDbRef..MaxDbRef` as key
  bounds), decode, apply every predicate in C#: type, name contains or regex (case-insensitive), owner via
  `e.owner` point read, zone/parent via edge point reads, flag by name or alias or synthesised type flag,
  power by name or alias, then skip/limit. Streams through `FreshAsyncEnumerable`.
- **GetPlayerByNameOrAliasAsync.** `obj.name` lookup, filter type player.
- **GetObjectCountAsync.** `LightningDatabase.Count` on `obj` (LMDB keeps entry counts per table).
- **Attributes.** `GetAttributeAsync(dbref, path)`: point reads of `attr.meta` for each prefix of the
  path; all-or-nothing as today. `SetAttributeAsync`: one job that creates missing ancestors with empty
  value, sets the leaf, sets `branch` on the parent when a child appears. `ClearAttributeAsync` and
  `WipeAttributeAsync` mirror the existing semantics using a prefix range for children.
  `GetAttributesAsync(pattern)`: seek to `dbref + literal prefix of the glob`, iterate while the key keeps
  the prefix, filter with the existing backtick-aware regex conversion. `GetAttributesByRegexAsync`:
  range over the whole dbref prefix, `Regex` with `IgnoreCase`, ordered by key which is by long name, so
  parent-before-child holds by construction. Lazy variants defer the `attr.val` read.
  `GetAttributeWithInheritanceAsync`: compute parent chain (`e.parent`, depth ≤ 100, visited set), zones
  per chain member (`e.zone`), then in one read transaction probe the path prefixes on self, each parent,
  each zone in precedence order, returning the `{self, parents, zones}` candidate sets the existing C#
  evaluation consumes unchanged.
- **Flags and powers.** Definitions by name; object flag set/unset writes `e.flag`/`r.flag` pairs.
- **Navigation.** Location depth walk over `e.loc` with `depth = -1` meaning until no edge. Contents,
  exits, entrances, homed-at, objects-by-zone are reverse-table ranges decoded through `obj`.
  `GetNearbyObjectsAsync` composes self, contents, and location contents. `MoveObjectAsync` replaces the
  location pair. `IsReachableViaParentOrZoneAsync` is BFS over `e.parent` ∪ `e.zone` with a visited set
  and `maxDepth` cap.
- **Channels.** `CreateChannelAsync` checks `chan` then puts; returns the name-taken result otherwise.
  Membership reads decode `chan.member` records; status flags are edge properties as today.
- **Mail.** `SendMailAsync` allocates a mail id from `meta`, writes `mail`, `mail.box`, `mail.sent`.
  Positional access ranges `mail.box` and skips. Folder listing is a distinct over the recipient's range.
  Sent mail uses `mail.sent`, replacing the shortest-path query.
- **Accounts and sessions.** Point reads via unique index tables; `TouchSessionExpiryAsync` is a job that
  no-ops when the token is absent; delete-by-account and delete-by-ip iterate the secondary tables.
- **Expanded data.** Read-merge-write in the job with the same non-null-overrides semantics the
  SurrealDB provider uses.
- **Wiki.** Ported from `SurrealDatabase.Wiki.cs`; revision numbering and write-conflict detection happen
  inside the writer job by comparing the expected revision to the stored one.
- **Registries.** Ported method for method from the SurrealDB partials; all are keyed CRUD.
- **Scene storage.** `LightningSceneStorage` in the plugin, using `ILightningStorageAccessor` which
  exposes `ReadAsync`, `WriteAsync`, `Tables` (open-or-create by name at plugin start), and the key
  helpers. Ported from `SurrealSceneStorage` semantics.

## 8. Migration and seed

`Migrate()`:
1. Take the static migrate lock.
2. Open the store (creates the directory and every catalogue table).
3. Upsert flags, attribute flags, powers, attribute entries and plugin flags from the shared seed data
   (idempotent, keyed by name, always run).
4. If `meta` lacks `mig:0001_initial_seed`: seed objects #0–#9 with the same names, types, locations,
   homes, owners and flags as `SurrealDatabase.Migration.cs:281-404`, then record the id.
5. Run each `IMigrationSource.LightningSteps` step not yet recorded in `meta` (`mig:<plugin>:<step>`).
6. `AncestorSeed.SeedAncestorPlayerFormatsAsync(this)` under the same initial-seed gate.
7. Recompute `next_dbref` as last key of `obj` + 1.
8. Ensure the `state` record exists.

Shared seed data: `SharpMUSH.Database/Seed/FlagSeed.cs`, `AttributeFlagSeed.cs`, `PowerSeed.cs`,
`AttributeEntrySeed.cs`, `InitialObjectSeed.cs`, holding the tuple arrays hoisted verbatim from the
SurrealDB provider. Only the lightning provider reads them in this plan.

`IMigrationSource` gains `IEnumerable<LightningMigrationStep> LightningSteps => [];` where
`LightningMigrationStep(string Id, Func<ILightningStorageAccessor, ValueTask> Apply)`.

## 9. Staging and backup

`CreateStagingAsync` creates a `LightningStagingDatabase` on `<path>.staging-<StagingId>` with its own
store and writer, migrates it, and returns it. `PromoteToLiveAsync`: drain and stop the live writer, take
the store's write lock, close the live environment, rename live to `<path>.previous` (replacing any
older one), rename staging to live, reopen, restart the writer, recompute `next_dbref`, release.
`AbortAsync` closes and deletes the staging directory. `DisposeAsync` aborts if neither happened.
`WipeDatabaseAsync` closes, deletes the directory, reopens and migrates.

`LightningDatabase.CopyToAsync(path, compact: true)` wraps `LightningEnvironment.CopyTo` for backups;
exposed on the class and the accessor, not on `ISharpDatabase`.

## 10. Errors

`MDB_MAP_FULL`, `MDB_TXN_FULL`, `MDB_READERS_FULL` are mapped to `LightningStoreException` with a message
naming the setting (`SHARPMUSH_LIGHTNING_MAPSIZE`, batch size, `MaxReaders`). All other non-success
result codes are thrown as `LightningStoreException` with the code. A job that throws faults only its
caller. Nothing is retried; there are no conflicts to retry.

## 11. Wiring

- `DatabaseProvider.Lightning`; `Program.cs` env-var chain; `Startup.cs` branch reading
  `SHARPMUSH_LIGHTNING_PATH` → `Lightning:Path` → `lightning-data`, and `SHARPMUSH_LIGHTNING_MAPSIZE`
  (bytes, default 64 GiB); registers the singleton, runs `Migrate()` in the factory like the others;
  registers `ILightningStorageAccessor` as the same cast.
- `SceneSystemServiceCollectionExtensions`: `LightningKey` arm and `LightningSceneStorage` registration.
- `ServerWebAppFactory` and `TestWebApplicationBuilderFactory`: `lightning` arm pointing at a per-run
  temp directory; no container.
- CI matrices in `_dotnet-build-test.yml`: add `lightning`.
- `CLAUDE.md`: env var values and the two new settings.
- Project references: `SharpMUSH.Server`, `SharpMUSH.Tests.Infrastructure`, `SharpMUSH.Benchmarks`,
  `SharpMUSH.Plugins.Scene`, `SharpMUSH.Tests.ScenePlugin` as needed.

## 12. Core.Arango decoupling

Remove `Core.Arango` and `Core.Arango.Migration` from `SharpMUSH.Database.csproj`; add both to
`SharpMUSH.Database.ArangoDB.csproj`. Build the solution; any project that compiled only through the
transitive reference gets an explicit one. `DatabaseConstants` keeps its collection-name strings (they are
data, not a driver dependency). Verified by building and by `dotnet list package --include-transitive` on
`SharpMUSH.Database.Lightning` showing no `Core.Arango`.

## 13. Testing

Provider unit tests in `SharpMUSH.Tests/Database/Lightning/`:
- key encoding round-trips and ordering (dbref ranges, prefix boundaries, separator safety);
- writer: jobs execute in order, a throwing job faults only its task, cancellation before dequeue;
- reader: chunked range enumeration resumes correctly across page boundaries with concurrent writes;
- catalogue coverage: every edge/index table is in the delete cascade; every on-disk table is catalogued;
- staging: promote swaps data and counter; abort leaves live untouched;
- recovery: write, begin a transaction and dispose without commit, reopen, assert absent and prior state
  intact.
Then the full suites: `SHARPMUSH_DATABASE_PROVIDER=lightning dotnet run --project SharpMUSH.Tests` and the
integration project, with `ObjectSearchFilterPushdownTests` run first. Fail-first applies to every new
test.

## 14. Benchmarks

- `LightningBaseBenchmark` copied from `SurrealBaseBenchmark` (temp directory, NATS only).
- `LightningDatabaseBenchmarks` mirroring the seven existing operations and categories.
- `ExtendedDatabaseBenchmarks` base with per-provider subclasses for all four providers: inheritance walk
  (three-deep parent chain plus a zone), wildcard and regex attribute listing over a 200-attribute object,
  filtered search over 1 000 seeded objects, `WipeAttributeAsync` on a 50-attribute subtree,
  `DeleteObjectAsync`, `IsReachableViaParentOrZoneAsync`, and eight concurrent `SetAttributeAsync` tasks.
- Run lightning and SurrealDB locally; ArangoDB and Memgraph through Testcontainers when Docker is
  available. Results committed to `docs/benchmarks/2026-09-storage-providers.md`.
