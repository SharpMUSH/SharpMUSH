# ADR-DB-1: Storage engine for SharpMUSH

**Status:** Accepted
**Date:** 2026-09-07
**Deciders:** SharpMUSH maintainers

## Context

SharpMUSH ships three interchangeable storage providers behind `ISharpDatabase`
(plus the wiki, layout, package, application and role service interfaces that each
provider also implements): ArangoDB (default in config, remote server), Memgraph
(remote server, Bolt) and SurrealDB (embedded, RocksDB on disk in production, `mem://`
in tests). `docs/superpowers/plans/2026-08-24-attribute-tree-flag-parity.md` records
that **production runs SurrealDB**; `CLAUDE.md` and `DatabaseProvider.cs` still call it
"in-memory", which is stale.

Two forces prompted this review:

1. **Licensing.** None of the three engines is OSI open source any more.
   - ArangoDB moved from Apache 2.0 to BUSL 1.1 with 3.12; the prebuilt Community
     Edition adds a 100 GiB dataset cap and a non-commercial / internal-use-only
     clause. Building from source lifts the cap but keeps the BUSL restrictions.
   - Memgraph is BSL 1.1 (permissive additional-use grant: production use allowed,
     no DBaaS).
   - SurrealDB core is BSL 1.1 converting to Apache 2.0 four years after each release;
     the grant explicitly allows embedding SurrealDB in shipped applications and running
     it as a service, forbidding only offering SurrealDB itself as a DBaaS. SDKs are
     Apache 2.0 / MIT.
   SharpMUSH itself is Apache 2.0. Only SurrealDB is compatible with "run it anywhere,
   commercially, embedded" without reading fine print, and only for four-year windows.
2. **Operational shape.** The project wants an embedded engine: no Docker, no sidecar,
   one process. The maintainers are willing to write and own a .NET binding for a
   native library that lacks one.

### What the workload actually needs

The full access-pattern audit is summarised here; the provider contract lives in
`SharpMUSH.Library/ISharpDatabase.cs` and the collection/edge layout in
`SharpMUSH.Database/DatabaseConstants.cs`.

The data model is a **shallow graph**: ~22 edge types, nearly all of which are
one-hop (location, home, owner, zone, flags, powers, channel membership, mail,
account→character). Only two relations are chains:

- **Parent chain** (`HasParent`, walked 1..100) and **zone chain** (`HasZone`), including
  a BFS over the *union* of both with global vertex dedup for `@parent`/`@chzone`
  cycle detection.
- **Attribute tree** (`HasAttribute`, self-recursive, walked 1..999/1..99999). Every
  backtick segment is its own vertex, and the full path is *also* denormalised into a
  `LongName` string because wildcard `lattr` needs the flat form and exact lookup uses
  the edges. Attributes hang off the typed vertex rather than `node_objects`, costing an
  extra INBOUND hop on every attribute query (tagged tech debt in
  `DatabaseConstants.cs:126`).

Hard requirements a replacement must meet:

| # | Requirement | Where it bites |
|---|---|---|
| 1 | Variable-depth directed traversal both directions | parents, zones, attribute subtrees, location depth |
| 2 | Pruned path walk (O(path length) exact attribute lookup) | `GetAttributeAsync` |
| 3 | Union-of-edge-types BFS with dedup and early exit | `IsReachableViaParentOrZoneAsync` |
| 4 | Properties on edges | channel membership status lives on the edge |
| 5 | Correlated nested traversals in one statement | inheritance walk returns `{self, parents, zones}` |
| 6 | Per-row regex, case-insensitive | `lattr`, `wildgrep`, `@search` with `UseRegex` |
| 7 | Multi-collection ACID transactions or robust optimistic retry | attribute set touches 4 collections; object create ~6 |
| 8 | Monotonic key allocator (dbref) | Arango autoincrement / Memgraph counter node / Surreal in-process counter |
| 9 | Unique, sparse-unique and compound indexes | accounts, channels, sessions |
| 10 | Streaming result cursors | `GetAllObjectsAsync`, `@search`, `@wcheck` |
| 11 | Multiple named databases or snapshot/restore | `IStagingDatabase` for PennMUSH import |
| 12 | Cheap count | `INFO` answered pre-login, polled by MUD crawlers |
| 13 | `DISTINCT`, offset pagination, timestamp-descending sort | mail folders, sessions, logs, wiki |

Soft / roadmap: full-text search (designed in `docs/design/search-infrastructure.md`,
not built); TTL expiry (sessions are app-managed today, at the cost of an exclusive
transaction per authenticated request).

Two facts lower the bar considerably:

- **FusionCache absorbs the hot reads.** 20 of 35 queries are `ICacheable`, including
  `GetObjectNodeAsync` and `GetAttributeWithInheritanceAsync`. What must be fast at the
  engine is the *uncached* set: writes, wildcard/regex attribute scans, filtered
  search, full scans, mail, wiki, packages.
- **Scale is small.** Realistic MUSH: 10³–10⁵ objects, 10⁴–10⁷ attribute rows, well
  under a gigabyte. The entire dataset fits in RAM on any host. Engine choice is about
  correctness, transactions, licensing and ergonomics, not throughput.

Also relevant: the SurrealDB provider already carries an optimistic-concurrency retry
loop (`SurrealDatabase.cs:116-137`) because one CI run logged 270 write conflicts on
channel creation alone; the Memgraph provider retries 8 times with backoff; only
ArangoDB has real exclusive locking. Three providers means every semantic (e.g. the
parent-before-child ordering `@clone` depends on) is satisfied three different ways,
one of them by accident (`ISharpDatabase.cs:196-216`).

## Decision

Build **LMDB via Lightning.NET** (`SharpMUSH.Database.Lightning`, the appendix's Option F
substrate) as the embedded, permissively licensed storage provider, selected by
`SHARPMUSH_DATABASE_PROVIDER=lightning`; it landed 2026-09-07, with the full unit and
integration suite passing under it per
`docs/superpowers/specs/2026-09-06-lightning-provider-design.md`. Keep ArangoDB, Memgraph
and SurrealDB selectable rather than retiring them now. SQLite (Option A) remains the
relational alternative on record, considered and set aside for now rather than adopted.
Track **LadybugDB** as the graph-native alternative if a future workload needs traversal
the flat keyspace cannot express.

## Options considered

### Option A: SQLite (Microsoft.Data.Sqlite / SQLitePCLRaw)

Not pursued: set aside in favor of the LMDB provider that shipped (Option F below); kept
here as the relational alternative of record.

| Dimension | Assessment |
|---|---|
| License | Public domain (SQLite); MIT (Microsoft.Data.Sqlite) |
| Embedded | Yes, in-process, native lib ships in the NuGet for all RIDs |
| .NET support | First-party Microsoft package; EF Core, Dapper, everything |
| Complexity | Low engine, medium provider rewrite (schema + ~6k LOC provider) |
| Maturity | Highest of any candidate; deterministic upgrade story |
| Concurrency | WAL mode: many readers, one writer, writers serialised. No optimistic-conflict retries at all. |

How each hard requirement maps:

- (1, 3) Recursive CTEs. Parent chain, zone chain, location depth, contents, and the
  union BFS (`UNION` of two edge tables inside the recursive member, dedup via the
  working table) are each a single statement.
- (2) The attribute tree becomes a flat table `attributes(object_id, long_name, name,
  parent_id, value, owner_id, flags…)` with `UNIQUE(object_id, long_name)`. Exact lookup
  is an indexed equality on `(object_id, long_name)` for each prefix of the path: no
  traversal at all. This also removes the typed-vertex hop.
- Wildcard `lattr`: real patterns are overwhelmingly `PREFIX*` / `` PREFIX`* ``. Store
  `long_name` upper-cased with BINARY collation and use `GLOB 'PREFIX*'`; SQLite's
  LIKE/GLOB optimisation turns the literal prefix into an index range scan, then the
  backtick-aware regex filters the remainder. Today this is a full-subtree traversal
  with a per-row regex; this is the biggest single performance win available.
- (5) The inheritance walk becomes one statement: recursive CTE yields the ordered
  chain (self, parents…, then zones of each), joined to `attributes` on the path
  prefixes, ordered by chain depth. The `no_inherit` short-circuit stays in C# as now.
- (4) Edges are rows; channel membership status columns live on `channel_members`.
- (6) Register a `REGEXP` function from C# (`SqliteConnection.CreateFunction`) backed
  by .NET `Regex` with `RegexOptions.IgnoreCase | CultureInvariant`. Same semantics
  the engine layer already assumes, no engine-specific anchoring fixups.
- (7) Full ACID across all tables in one transaction; `BEGIN IMMEDIATE` gives the
  writer lock up front, so the session-touch and channel-create races become trivial.
- (8) `INTEGER PRIMARY KEY AUTOINCREMENT` on `objects` is the dbref allocator; it
  never reuses numbers, which matches PennMUSH's objid semantics.
- (9) Unique, partial (`WHERE email IS NOT NULL` = sparse-unique) and compound
  indexes are all native.
- (10) `SqliteDataReader` streams rows; no materialisation.
- (11) Staging is a second file. Promote = close, atomic rename; abort = delete the
  file. `VACUUM INTO` gives consistent online backups. Simpler and safer than either
  the Surreal reflection client-swap or the Memgraph JSON-file fallback.
- (12) `SELECT COUNT(*) FROM objects` is a b-tree walk that is sub-millisecond at
  10⁵ rows; or keep a counter row if `INFO` polling ever matters.
- (13) All native.
- Expanded data and locks: JSON1 columns (`json_extract`, `json_patch`), matching
  Arango's `mergeObjects` semantics server-side.
- Full-text (roadmap): FTS5 is compiled into `e_sqlite3`; the designed
  `portal_search_view` maps directly to an FTS5 virtual table with external content.
- TTL: a periodic `DELETE FROM sessions WHERE expiry_unix_ms < ?` on a background
  timer replaces the per-request exclusive transaction.

**Pros:** zero licensing risk forever; no native binding to write or maintain; the
provider is plain SQL that any contributor can read; the flat attribute table fixes
three known tech-debt items (typed-vertex hop, LongName prefix seek, per-attribute flag
N+1 via a single JOIN); staging becomes a file rename; tests need no container.

**Cons:** it is a rewrite of the storage provider, not a swap; the graph query
languages go away, so the union BFS and inheritance walk are longer to express in SQL
than in AQL; single-writer means a long import blocks other writers (mitigated by the
staging file being a *different* database); no built-in graph algorithms (the only one
in use, `ALL_SHORTEST_PATH` for sent-mail, is a BFS CTE or, better, a `sender_id`
column on `mails`).

### Option B: LadybugDB (Kuzu successor)

| Dimension | Assessment |
|---|---|
| License | MIT (source and binaries) |
| Embedded | Yes; C API; one READ_WRITE database per process, many connections |
| .NET support | None official. Community bindings exist (Knaackee/ladybug.net, sergey-v9/ladybug-dotnet); maintainers approved an official repo in May 2026. SharpMUSH would own or co-own a P/Invoke binding over the C API. |
| Complexity | Medium: Cypher is close to the Memgraph provider, so much of that provider ports |
| Maturity | Fork of Kuzu (archived Oct 2025 after Apple acquired Kùzu Inc). Very active: v0.20.2 on 2026-09-02, 1000+ commits and 80+ contributors since Nov 2025. Storage format still moves between minor versions; expect export/import on upgrades. |
| Concurrency | Transactional within one process; designed as an OLAP-leaning columnar engine, so many small writes are not its strength |

Meets requirements 1–6 natively (variable-length paths, edge properties, `=~` regex,
nested subqueries). Gaps: no secondary/unique indexes beyond node primary keys in the
Kuzu lineage (uniqueness on channel name, account email, username would be enforced in
the provider or by making them primary keys); no multiple databases, so staging is
copy-directory-and-swap; schema-first typed tables (fine, the schema is fixed).

**Pros:** keeps the graph model and Cypher; MIT; fast traversals; FTS and vector
indexes built in for the search roadmap.
**Cons:** the project would carry a native binding for a fast-moving 0.x engine whose
storage format churns; OLTP write path is unproven at MUSH-style update rates; no
secondary indexes; a second young-Rust/C++-engine bet after SurrealDB.

### Option C: Keep SurrealDB embedded (status quo, RocksDB or SurrealKV)

| Dimension | Assessment |
|---|---|
| License | BSL 1.1 → Apache 2.0 per release after 4 years; embedding explicitly permitted |
| Embedded | Yes; official `SurrealDb.Embedded.{InMemory,RocksDb,SurrealKv}` packages (repo pins 0.9.0; 1.0.0 is on NuGet) |
| Complexity | None new |
| Maturity | 2.x; graph edges only via `RELATE`; regex needs full-match anchoring fixups; recursion capped at 256 |
| Concurrency | Optimistic; the provider already needs a retry loop and saw 270 conflicts in one CI run |

**Pros:** already works in production; official .NET embedded packages; the license is
the least restrictive of the three current engines and does permit what SharpMUSH does.
**Cons:** still source-available rather than open; engine-level write conflicts under
ordinary load; the parent-before-child ordering `@clone` relies on is satisfied by
accident of DFS order; the in-process dbref counter must be rebuilt after staging
promotion.

### Option D: Keep ArangoDB as default

Strongest query language of the set and the reference semantics, but it is a remote
server, BUSL 1.1, and the prepackaged Community Edition is non-commercial with a
100 GiB cap. It contradicts the embedded goal and is the worst licensing position.
Retire it after the SQLite provider reaches parity; keep the AQL as the semantic
reference while porting.

### Option E: DuckDB + DuckPGQ

MIT, embedded, SQL/PGQ graph syntax, `DuckDB.NET` exists. Rejected: DuckDB is an
analytics engine with a single-writer model and poor small-update performance, and
DuckPGQ is a CWI research extension that lags DuckDB patch releases.

### Option F: In-memory object graph in C# with an embedded KV/log for durability

What shipped: `SharpMUSH.Database.Lightning` built the LMDB substrate below; see
`docs/superpowers/specs/2026-09-06-lightning-provider-design.md` for the design.

The classic PennMUSH/TinyMUX architecture with a modern persistence layer (LMDB via
Lightning.NET, RocksDB, ZoneTree, FASTER/Tsavorite, LiteDB, DBreeze; all MIT/BSD/
OpenLDAP-licensed). Fastest possible reads and it would make FusionCache redundant.
Rejected for now: it moves transactions, crash recovery, indexes, staging and every
query into hand-written C#, which is more code to own than a SQL provider, and SQLite
with a large `cache_size` is effectively in-memory anyway. Revisit only if SQLite's
serialised writer measurably limits a real game.

### Option G: CozoDB / mnestic (Datalog, MPL-2.0)

Embedded, recursion-native Datalog, C API. Original project dormant since Dec 2024;
the `mnestic` fork is maintained by one party and pitched at agent memory. No .NET
binding. Rejected on bus factor.

### Options rejected on license or platform

- **ArcadeDB** (Apache 2.0 with a written pledge never to change, native OpenCypher, Bolt, online backup): cannot be loaded into a .NET process (IKVM is Java 8 only and ArcadeDB targets Java 21; no maintained free JNI wrapper; no GraalVM shared-library build). Viable only as a supervised child JVM with a bundled jlink runtime (~270 MB per platform), reached over localhost Bolt with the Neo4j.Driver the Memgraph provider already uses. Set `txWalFlush=1` or higher; the embedded default flushes to the OS only. The mature alternative to LadybugDB if a Cypher surface is a product requirement. Full dossier: the SharpMUSH Storage Dossier artifact.
- **HelixDB** (AGPL-3.0), **RavenDB embedded** (server AGPL/commercial): copyleft or
  commercial server incompatible with an Apache 2.0 embedded target.
- **Oxigraph** (Apache/MIT, RDF/SPARQL): paradigm mismatch, no C API or .NET binding.
- **Realm .NET**: end-of-life since MongoDB deprecated the device SDKs.
- **Garnet / Tsavorite** (MIT): excellent embedded KV/log, no query layer; only
  relevant as the persistence half of Option F.
- **libSQL / Turso** (MIT): SQLite-compatible; adopt only if replication is ever
  wanted, via the community `Nelknet.LibSQL` binding. Turso's Rust rewrite is beta.
- **Embedded PostgreSQL** (child process, e.g. MysticMind.PostgresEmbed): `ltree` and
  Apache AGE are attractive for attribute trees and Cypher, but it is a spawned server,
  not in-process. The upgrade path if SQLite is ever outgrown.

## Trade-off analysis

The deciding observation is that SharpMUSH's data is not deep-graph data. Twenty of the
twenty-two edge types are one hop; the two chains are short (parents ≤ 100) or are trees
whose full path is already denormalised into a string. A relational engine with
recursive CTEs covers every traversal in use, and the flat `(object_id, long_name)`
attribute table is *better* than the vertex-per-segment layout for the two hottest
uncached operations (exact path lookup and prefix `lattr`). The graph engines buy
expressive traversal syntax the workload barely uses, and pay for it with licensing
(Arango, Memgraph, Surreal), youth and native-binding maintenance (Ladybug), or
write-path weakness (DuckDB).

SQLite's single writer is the real cost. For a MUSH it is the correct trade: the game
loop is effectively one logical writer, and the alternative (optimistic conflicts with
retry loops) is what the SurrealDB provider fights today. The one write that must not
block is PennMUSH import, and staging into a separate file removes it from the writer
queue entirely.

LadybugDB is the honest runner-up. If the team values keeping Cypher and a graph model
over minimising owned native code, it is the only permissively licensed embedded graph
engine with momentum, and writing the binding is tractable (C API, Go and Swift
wrappers to crib from). Its storage-format churn and lack of secondary indexes are the
reasons it is second, not first.

## Consequences

Easier:
- Licensing questions disappear; Docker becomes optional for development; tests run
  against a temp file with no Testcontainers.
- Exact attribute lookup, prefix `lattr`, attribute flag hydration and the typed-vertex
  hop all improve structurally rather than by tuning.
- Staging/promotion is a file rename; backups are `VACUUM INTO`.
- One provider to keep semantically correct instead of three.

Harder:
- A storage-provider rewrite (~6k LOC by analogy with the Surreal provider) plus new
  migrations; the PennMUSH importer needs batching regardless of engine.
- Recursive CTEs are wordier than AQL/Cypher for the union BFS and inheritance walk.
- Provider-parity tests (`ObjectSearchFilterPushdownTests`,
  `ArangoDeleteObjectEdgeCoverageTests`, etc.) need re-pointing while providers are
  retired.

Revisit if:
- A production game shows writer-lock contention in SQLite (then Option F or embedded
  Postgres).
- LadybugDB ships stable storage, secondary indexes and an official .NET binding, and
  the team wants graph algorithms beyond what the game needs today.

## Action items

1. [ ] Fix the stale "in-memory" wording in `CLAUDE.md` and `DatabaseProvider.cs`; state that production runs SurrealDB on RocksDB.
2. [x] Extend `SharpMUSH.Benchmarks/DatabaseBenchmarks.cs` with the uncached shapes before comparing engines: inheritance walk, wildcard and regex `lattr`, `GetFilteredObjectsAsync`, `WipeAttributeAsync`, `DeleteObjectAsync`, `IsReachableViaParentOrZoneAsync`, and a concurrent-writer scenario. **Done:** `ExtendedDatabaseBenchmarks` covers all seven shapes for all four providers.
3. [x] Prototype the SQLite schema (objects, typed tables or a `type` column, flat `attributes` with `UNIQUE(object_id, long_name)`, edge tables with properties, JSON1 for locks/expanded data) and implement `GetAttributeAsync`, `GetAttributesAsync`, `GetAttributeWithInheritanceAsync` and `IsReachableViaParentOrZoneAsync` against it; run the parity tests for those four. **Done as LMDB:** `SharpMUSH.Database.Lightning` implements the whole surface — a flat dbref-prefixed attribute keyspace, edge tables with `DUPSORT` reverse indexes, and all four of those methods — and passes the full unit and integration suites.
4. [ ] Register a .NET-backed `REGEXP` function and confirm `GLOB` prefix seeks use the `long_name` index (`EXPLAIN QUERY PLAN`).
5. [x] Implement `IStagingDatabase` as a second file with rename-on-promote. **Done as LMDB:** `LightningStagingDatabase` is a second directory, and promotion swaps directories under the live store's gate, keeping the outgoing world at `<path>.previous`.
6. [x] Make the parent-before-child ordering of `GetAttributesByRegexAsync` an explicit contract and test it, rather than relying on traversal order. **Done as LMDB:** the ordering is the byte order of the attribute keyspace, stated on the method and pinned by test.
7. [ ] Add batching to `PennMUSHDatabaseConverter` (independent of engine).
8. [ ] Delete or fix `LazilyGetAttributePatternAsync` (zero callers, three divergent implementations).
9. [x] Once SQLite passes the full suite: flip the default provider, then retire Memgraph and ArangoDB, and move `Core.Arango` out of the shared `SharpMUSH.Database` project (today every provider transitively depends on it). **Done:** `Core.Arango` and `Core.Arango.Migration` moved to `SharpMUSH.Database.ArangoDB.csproj`; flipping the default provider and retiring Memgraph/ArangoDB stays open.
10. [ ] Spike a LadybugDB .NET binding only if step 3 surfaces a traversal the relational model cannot express acceptably.

## Sources

- ArangoDB licensing: [3.12 incompatible changes](https://docs.arango.ai/arangodb/stable/release-notes/version-3.12/incompatible-changes-in-3-12/), [3.12 CE changes FAQ](https://arangodb.com/3-12-ce-changes-faq/), [licensing update](https://arango.ai/blog/update-evolving-arangodbs-licensing-model-for-a-sustainable-future/)
- SurrealDB licensing: [surrealdb/license](https://github.com/surrealdb/license), [Licence FAQs](https://surrealdb.com/license); embedded packages: [surrealdb.net](https://github.com/surrealdb/surrealdb.net), [SurrealDb.Embedded.RocksDb](https://www.nuget.org/packages/SurrealDb.Embedded.RocksDb/), [SurrealDb.Embedded.SurrealKv](https://www.nuget.org/packages/SurrealDb.Embedded.SurrealKv/)
- Memgraph licensing: [BSL text](https://github.com/memgraph/memgraph/blob/master/licenses/BSL.txt), [Legal](https://memgraph.com/legal)
- Kuzu archival and forks: [The Register](https://www.theregister.com/software/2025/10/14/kuzudb-graph-database-abandoned-community-mulls-options/1142229), [From Kuzu to Ladybug](https://thedataquarry.com/blog/from-kuzu-to-ladybug/), [Ladybug: Spreading its Wings](https://blog.ladybugdb.com/post/ladybug-spreading-its-wings/), [Kineviz/bighorn](https://github.com/Kineviz/bighorn)
- LadybugDB: [repo](https://github.com/LadybugDB/ladybug), [releases](https://github.com/LadybugDB/ladybug/releases), [installation and C API](https://docs.ladybugdb.com/installation/), [concurrency model](https://docs.ladybugdb.com/concurrency/), [C#/.NET bindings discussion #544](https://github.com/LadybugDB/ladybug/discussions/544), [Kuzu .NET wrapper (FactEngine)](https://github.com/FactEngineCommunity/KuzuDB-DotNetInterface)
- DuckDB / DuckPGQ: [concurrency](https://duckdb.org/docs/current/connect/concurrency), [duckpgq community extension](https://duckdb.org/community_extensions/extensions/duckpgq), [DuckPGQ](https://duckpgq.org/)
- CozoDB: [cozodb/cozo](https://github.com/cozodb/cozo), [mnestic fork](https://www.mnesticdb.com/)
- SQLite: [Recursive CTEs](https://sqlite.org/lang_with.html), [SQLite as a graph database](https://dev.to/rohansx/sqlite-as-a-graph-database-recursive-ctes-semantic-search-and-why-we-ditched-neo4j-1ai)
- Others: [ArcadeDB embedded (JVM)](https://arcadedb.com/embedded.html), [HelixDB (AGPL)](https://github.com/HelixDB/helix-db), [RavenDB licensing](https://ayende.com/blog/178434/ravendb-4-0-licensing-pricing), [Oxigraph](https://github.com/oxigraph/oxigraph), [Garnet (MIT)](https://github.com/microsoft/garnet), [Lightning.NET](https://github.com/CoreyKaylor/Lightning.NET), [RocksDbSharp](https://www.nuget.org/packages/RocksDbSharp/), [Nelknet.LibSQL](https://github.com/nelknet/Nelknet.LibSQL), [Turso status](https://www.theregister.com/databases/2026/07/29/after-rewriting-sqlite-in-rust-turso-turns-its-sights-on-postgres/5279835), [LiteDB](https://github.com/litedb-org/litedb), [DBreeze](https://github.com/hhblaze/DBreeze)

## Appendix: substrates for a bespoke SharpMUSH engine (Option F)

Added 2026-09-06 after the question "what if we wrote our own database?" The
requirement for the substrate is an ordered key space with range/prefix iteration,
atomic multi-key writes, crash safety, and a permissive license, usable in-process from
.NET on Linux, macOS and Windows. Raw throughput is not a criterion at MUSH scale.

| Substrate | License | .NET path | Ordered | Verdict |
|---|---|---|---|---|
| **LMDB** 1.0.1 (Aug 2026) | OpenLDAP (permissive) | Lightning.NET 0.23 (MIT), bundles natives, zero-copy `Span` reads | Yes, B+tree | **First choice.** Read-optimised, single writer, MVCC readers never block, copy-on-write so no WAL and no compaction threads, `mdb_copy` gives a live consistent copy for staging/backup. Named sub-databases act as column families; `DUPSORT` makes `edge:{type}:{from}` → many `{to}` values a native multimap. Watch: map size fixed at open (set it large; sparse on Linux/macOS, preallocated on Windows), default 511-byte key limit (compile-time) which long attribute paths could hit, and read transactions must never span an `await` or they pin old pages. |
| **libmdbx** 0.14.3 | Apache 2.0 (since 0.13) | None usable: mdbx.NET on NuGet last published Nov 2018 and binds deprecated calls; the only maintained binding (Amsoft `libmdbx-dotnet`) ships win-x64/linux-x64 from a private Nexus with Russian docs | Yes | Technically the strongest mmap engine (auto grow/shrink, ~2 KB keys at 4 KB pages, `SAFE_NOSYNC` keeps integrity, slow-reader callback, `mdbx_chk`, defrag), and Erigon documented why it left LMDB for it. Not recommended: amalgamated-source-only distribution since Dec 2025 from SourceCraft, roadmap says "open code, but not development", ABI broke twice in 2026, and SharpMUSH would own both the native build for every RID and the binding. |
| **SQLite as KV** | Public domain | Microsoft.Data.Sqlite | Yes (`WITHOUT ROWID` blob-key table per family) | The hedge: keeps the bespoke index design in C# but retains the `sqlite3` CLI, `VACUUM INTO`, and WAL. If the design goes this way, use real tables instead (Option A). |
| **ZoneTree** 1.9.7 (Aug 2026) | MIT | Pure .NET, net6.0–net10.0 | Yes, LSM | Best pure-managed option; no native deps. One author (99% of commits), 501 stars, zero open issues, commercial support offered. Transactions are optimistic, read-committed, and scoped to a single tree, so a bespoke engine must put every table in one tree with composite byte keys to get multi-table atomicity. Default async-compressed WAL can lose in-flight writes on crash; Sync WAL is the durable mode. Live backup covers only the non-transactional tree. On-disk format stability is not documented; namespace and target-framework breaks shipped in mid-2026. |
| **DBreeze** 1.138 (Jun 2026) | BSD-3 | Pure .NET | Yes | Maintained since 2012 by one author; ordered tables, ACID, built-in text search. Dated API and PDF docs. Viable, ranked with ZoneTree on bus factor. |
| **FASTER / Tsavorite** | MIT | `Microsoft.FASTER.Core` 2.6.5 (dormant; status issue unanswered) or Tsavorite inside `Microsoft.Garnet` (standalone package request closed as not planned) | **No** (hash index) | Not a graph substrate: no range scans. `FasterLog` is a good append log for the snapshot-plus-WAL design, but a hand-rolled fsync'd log is simpler than taking the Garnet dependency. |
| **LiteDB** v5 / v6 prerelease.80 (Aug 2026) | MIT | Pure .NET | Indexes on BSON docs | Document store, not a KV; v6 still prerelease. Not a substrate. |
| **redb** 2.x (stable format), **SurrealKV** 0.21, **fjall** | MIT/Apache; Apache 2.0; MIT/Apache | None: no C API; SharpMUSH would write a Rust `cdylib` with a C ABI plus P/Invoke, built for three RIDs | Yes | Only if someone wants to own a Rust shim. redb is the most LMDB-like (copy-on-write B-tree). |
| **No engine: snapshot + WAL** | none | MemoryPack/protobuf snapshot on a timer, append-only fsync'd op log, replay on start | n/a | The PennMUSH architecture. Zero dependencies, simplest mental model, whole DB in RAM (it is anyway). SharpMUSH owns log format, fsync discipline and log compaction; staging is a directory. Pair with LMDB instead if random-access persistence or partial loads are ever wanted. |

Rejected on license or platform: BerkeleyDB (AGPL), Kyoto/Tokyo Cabinet (GPL),
WiredTiger (GPL), RavenDB Voron (AGPL), ESENT/ManagedEsent (MIT but Windows-only),
LevelDB (stale .NET bindings), Speedb (RocksDB fork, dormant after the Redis
acquisition), Sled (perpetual beta), bbolt/Pebble (Go only).

Recommended shape if Option F is pursued: in-memory domain model (attribute trees as
nodes, parent chain as references) with LMDB as the durable store, one writer lock,
declarative index specs, and property-based tests against a pure in-memory reference
model. Spike it against the same five methods as the SQLite prototype before deciding.

Sources: [LMDB CHANGES](https://github.com/openldap/openldap/blob/master/libraries/liblmdb/CHANGES),
[Lightning.NET](https://github.com/CoreyKaylor/Lightning.NET), [libmdbx](https://libmdbx.dqdkfa.ru/),
[mdbx.NET](https://github.com/wangjia184/mdbx.NET), [ZoneTree](https://github.com/ZoneTree/ZoneTree),
[DBreeze](https://github.com/hhblaze/DBreeze), [FASTER status issue](https://github.com/microsoft/FASTER/issues/919),
[Tsavorite package request](https://github.com/microsoft/garnet/issues/691), [LiteDB v6](https://github.com/litedb-org/LiteDB/issues/2623),
[redb](https://github.com/cberner/redb), [SurrealKV](https://github.com/surrealdb/surrealkv),
[RocksDB .NET (curiosity-ai)](https://github.com/curiosity-ai/rocksdb-sharp).
