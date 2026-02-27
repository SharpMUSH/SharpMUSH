# ArangoDB Alternatives Analysis for SharpMUSH

## Executive Summary

SharpMUSH currently uses ArangoDB as its sole database via the `ISharpDatabase` interface (75+ methods) implemented by `ArangoDatabase` (3,300+ lines). The database serves as a multi-model store combining document, graph, and key-value capabilities. This analysis evaluates alternatives across embedded and standalone deployment modes, assessing each against SharpMUSH's specific requirements.

**Recommendation**: SurrealDB is the strongest overall candidate (embedded via SurrealKv, native graph support, multi-model). Memgraph is the strongest Cypher-compatible lightweight alternative. Neo4j is the most mature graph-native alternative but has heavy JVM resource overhead. RavenDB offers the best .NET developer experience but has weaker graph support. PostgreSQL + Marten is the most pragmatic but requires the most custom graph logic.

---

## Table of Contents

1. [Current ArangoDB Usage Profile](#1-current-arangodb-usage-profile)
2. [Requirements Matrix](#2-requirements-matrix)
3. [Candidate Analysis](#3-candidate-analysis)
   - 3.1 [SurrealDB](#31-surrealdb)
   - 3.2 [Neo4j](#32-neo4j)
   - 3.3 [RavenDB](#33-ravendb)
   - 3.4 [PostgreSQL + Marten](#34-postgresql--marten)
   - 3.5 [ArcadeDB](#35-arcadedb)
   - 3.6 [LiteDB](#36-litedb)
   - 3.7 [DGraph](#37-dgraph)
   - 3.8 [Memgraph](#38-memgraph)
   - 3.9 [FalkorDB](#39-falkordb)
   - 3.10 [Kùzu](#310-kùzu)
   - 3.11 [JanusGraph](#311-janusgraph)
   - 3.12 [TypeDB](#312-typedb)
4. [Feature Comparison Matrix](#4-feature-comparison-matrix)
5. [Resource Requirements Comparison](#5-resource-requirements-comparison)
6. [Migration Complexity Assessment](#6-migration-complexity-assessment)
7. [Recommendation Summary](#7-recommendation-summary)

---

## 1. Current ArangoDB Usage Profile

### 1.1 Core Features Used

| Feature | Usage | Complexity | Files |
|---------|-------|------------|-------|
| Named Graphs | 16 graphs with 19 edge types | High | `DatabaseConstants.cs`, `Migration_CreateDatabase.cs` |
| Graph Traversals | INBOUND/OUTBOUND 1..N depth | High | `ArangoDatabase.cs` (40+ queries) |
| PRUNE (conditional traversal) | Attribute path matching, early termination | Critical | `ArangoDatabase.cs:1921,2010,3075` |
| ALL_SHORTEST_PATH | Mail routing | Medium | `ArangoDatabase.cs:470,598` |
| Multi-graph cross-joins | Parent + Zone + Attributes in single query | Critical | `ArangoDatabase.cs:3057-3175` |
| ACID Transactions | Player/thing creation with exclusive locks | High | `ArangoDatabase.cs:68-86,648-678` |
| Auto-increment keys | Object DBRefs (0, 1, 2, ...) | Medium | `Migration_CreateDatabase.cs:31-37` |
| Schema validation | JSON Schema on `node_objects` | Low | `Migration_CreateDatabase.cs:38-51` |
| Streaming cursors | Large result sets via `ExecuteStreamAsync` | Medium | Throughout |
| AQL functions | DOCUMENT, FIRST, NTH, LENGTH, REGEX_TEST, etc. | Medium | Throughout |

### 1.2 Graph Structure

```
Object Model (16 Graphs):
├── GraphObjects: typed collections → base objects (IsObject edges)
├── GraphLocations: contents → containers (AtLocation edges)
├── GraphHomes: objects → home locations (HasHome edges)
├── GraphExits: containers → exits (HasExit edges)
├── GraphParents: objects → parent chain (HasParent edges)
├── GraphZones: objects → zones (HasZone edges)
├── GraphAttributes: objects → hierarchical attributes (HasAttribute edges)
├── GraphAttributeFlags: attributes → flags (HasAttributeFlag edges)
├── GraphAttributeEntries: attributes → entries (HasAttributeEntry edges)
├── GraphObjectOwners: objects → player owners (HasObjectOwner edges)
├── GraphAttributeOwners: attributes → player owners (HasAttributeOwner edges)
├── GraphFlags: objects → flags (HasFlags edges)
├── GraphPowers: objects → powers (HasPowers edges)
├── GraphObjectData: objects → expanded data (HasObjectData edges)
├── GraphChannels: players ↔ channels (OwnerOfChannel + OnChannel edges)
└── GraphMail: objects ↔ mail (SenderOfMail + ReceivedMail edges)
```

### 1.3 Critical Query Patterns

**Pattern 1: Attribute Inheritance (Most Complex)**
- Traverses Self → Parent chain (1..100) → Zone chain (1..100)
- Uses PRUNE for path matching at each level
- Cross-references GraphObjects, GraphParents, GraphZones, GraphAttributes
- ~70 lines of AQL per query variant
- Files: `ArangoDatabase.cs:3057-3332` (two variants: eager + lazy)

**Pattern 2: Conditional Graph Traversal with PRUNE**
- Hierarchical attribute paths (e.g., `["TWO", "LAYERS"]`)
- PRUNE stops traversal when path segment doesn't match
- Uses `NTH(@attr, LENGTH(p.edges)-1) != v.Name`
- Files: `ArangoDatabase.cs:1921,1962,2010`

**Pattern 3: Reachability Check**
- `IsReachableViaParentOrZoneAsync`: Multi-edge-type traversal
- Checks if target is reachable via HasParent OR HasZone edges
- Variable depth up to 100
- Files: `ArangoDatabase.cs:488`

### 1.4 Deployment Architecture

- **Standalone only** (Docker testcontainers, Kubernetes, socket)
- Strategy pattern: `ArangoStartupStrategy` → `ArangoTestContainerStartupStrategy` / `ArangoKubernetesStartupStrategy` / `ArangoSocketStartupStrategy`
- No embedded mode available with ArangoDB
- .NET SDK: `Core.Arango` v3.12.2 + `Core.Arango.Migration` v3.12.3

### 1.5 Current Resource Profile (ArangoDB)

| Resource | Minimum | Recommended | Notes |
|----------|---------|-------------|-------|
| **RAM** | 1 GB | 4 GB+ | C++ native; no JVM overhead. Memory-maps data files |
| **CPU** | 1 core | 2+ cores | Single-server mode; scales with query concurrency |
| **Disk** | 256 MB | Varies | RocksDB storage engine; data-dependent |
| **Runtime** | None | None | Self-contained C++ binary; no JVM or CLR needed |

---

## 2. Requirements Matrix

| # | Requirement | Priority | Notes |
|---|-------------|----------|-------|
| R1 | Graph traversals with variable depth (1..N) | **Must Have** | Parent/zone/attribute chains |
| R2 | Conditional traversal termination (PRUNE equiv.) | **Must Have** | Attribute path matching |
| R3 | Multi-edge-type traversals | **Must Have** | Parent + Zone combined |
| R4 | Shortest path queries | **Should Have** | Mail routing only |
| R5 | ACID transactions | **Must Have** | Object creation consistency |
| R6 | .NET SDK (async, streaming) | **Must Have** | `IAsyncEnumerable`, `ValueTask` |
| R7 | Auto-increment keys | **Should Have** | DBRef generation |
| R8 | Schema validation | **Nice to Have** | Currently on `node_objects` only |
| R9 | Embedded mode | **Should Have** | Simplified deployment |
| R10 | Standalone mode | **Must Have** | Production deployment |
| R11 | Migration framework | **Should Have** | Schema evolution |
| R12 | Cross-graph joins (single query) | **Must Have** | Inheritance queries |
| R13 | Regex pattern matching in queries | **Should Have** | Attribute pattern search |
| R14 | Document CRUD with JSON | **Must Have** | All object operations |
| R15 | Active maintenance & community | **Must Have** | Long-term viability |
| R16 | Permissive license | **Should Have** | Apache 2.0 / MIT preferred |

---

## 3. Candidate Analysis

### 3.1 SurrealDB

**Type**: Multi-model (document + graph + key-value + time-series)
**License**: Business Source License 1.1 (converts to Apache 2.0 after 4 years)
**Embedded**: Yes — SurrealKv (file-based), RocksDB backend
**.NET SDK**: `SurrealDb.Net` (official, v0.9.0+)

#### Strengths

1. **Embedded Mode (SurrealKv)**: First-class embedded mode with file-based storage (SurrealKv and RocksDB backends)
   ```csharp
   // Embedded - no server needed
   services.AddSurreal("Endpoint=surrealkv://data.db;Namespace=test;Database=test")
           .AddSurrealKvProvider();
   ```
2. **Native Graph Relations**: Record links and graph edges built into the data model
   ```sql
   RELATE player:1->has_attribute->attribute:desc;
   SELECT ->has_attribute->attribute FROM player:1;
   ```
3. **Graph Algorithms (v2.2.0+)**: Shortest path, BFS collection, all-path enumeration
   ```sql
   SELECT VALUE path FROM player:1->{..+shortest=room:5};
   SELECT VALUE path FROM player:1->{..+collect};
   ```
4. **Multi-model in one query**: Document, graph, and key-value in single SurrealQL statement
5. **Live Queries**: Real-time change notifications
6. **DI Integration**: `AddSurreal()` extension for .NET DI

#### Weaknesses

1. **No PRUNE equivalent**: SurrealQL lacks conditional traversal termination — attribute inheritance queries would need restructuring (application-level iteration or recursive subqueries)
2. **.NET SDK maturity**: As of v0.9.0 (latest verified), no client-side transaction API (`BeginTransaction` absent from .NET SDK, present in JS SDK). *Note: Verify current SDK version and capabilities before making migration decisions, as the SDK may have evolved.*
3. **No auto-increment keys**: Would need a counter table or `ULID`/`UUID` approach. Custom auto-increment simulation required
4. **BSL License**: Each SurrealDB version converts to Apache 2.0 four years after its individual release date (not a blanket conversion for all code). Newer features remain under BSL longer than older code. Usage is permitted under BSL, but redistribution restrictions apply
5. **No migration framework**: Would need a custom migration system
6. **Young ecosystem**: Fewer production deployments than ArangoDB

#### SharpMUSH-Specific Assessment

| Requirement | Support | Notes |
|-------------|---------|-------|
| R1 Graph traversals | ✅ Yes | `->edge->` and `<-edge<-` with depth |
| R2 PRUNE | ❌ No | Must implement in application logic |
| R3 Multi-edge traversals | ✅ Yes | Multiple edge types in single query |
| R4 Shortest path | ✅ Yes | `{..+shortest=target}` (v2.2.0+) |
| R5 Transactions | ⚠️ Partial | Server supports; .NET SDK lacks `BeginTransaction` |
| R6 .NET SDK | ✅ Yes | Official SDK with async support |
| R7 Auto-increment keys | ❌ No | Must simulate with counter records |
| R8 Schema validation | ✅ Yes | `DEFINE FIELD` with type constraints |
| R9 Embedded mode | ✅ Yes | SurrealKv + RocksDB providers |
| R10 Standalone mode | ✅ Yes | HTTP/WebSocket server |
| R11 Migration framework | ❌ No | Must build custom |
| R12 Cross-graph joins | ✅ Yes | Subqueries with graph traversals |
| R13 Regex matching | ✅ Yes | `string::is::match()` function |
| R14 Document CRUD | ✅ Yes | Native JSON documents |
| R15 Active community | ⚠️ Medium | Growing but smaller than Neo4j/PostgreSQL |
| R16 License | ⚠️ BSL | Converts to Apache 2.0 after 4 years |

**Migration effort**: HIGH — Attribute inheritance queries need complete redesign. Transaction handling needs workarounds. Auto-increment simulation needed.

#### Resource Requirements

| Resource | Minimum | Recommended | Notes |
|----------|---------|-------------|-------|
| **RAM** | 50 MB (embedded) / 256 MB (standalone) | 512 MB+ | Rust-native; very efficient memory usage. SurrealKv embedded has minimal overhead |
| **CPU** | 1 core | 2+ cores | Rust async runtime; efficient single-core performance |
| **Disk** | 10 MB (binary) | Varies | Small binary footprint; data-dependent storage |
| **Runtime** | None | None | Self-contained Rust binary; no JVM or CLR needed |

---

### 3.2 Neo4j

**Type**: Native graph database (property graph model)
**License**: GPL v3 (Community), Commercial (Enterprise)
**Embedded**: Yes — Neo4j.Embedded (JVM-based, via IKVM or separate process)
**.NET SDK**: Official `Neo4j.Driver` (Bolt protocol)

#### Strengths

1. **Best-in-class graph queries**: Cypher is the most mature graph query language
   ```cypher
   MATCH (obj:Object)-[:HAS_PARENT*1..100]->(parent:Object)
   WHERE obj.key = $startKey
   RETURN parent
   ```
2. **Variable-length pattern matching**: Native support for depth-limited traversals
   ```cypher
   MATCH p = (start)-[:HAS_ATTRIBUTE*1..10]->(attr)
   WHERE ALL(idx IN range(0, length(p)-1) WHERE
     relationships(p)[idx].name = $path[idx])
   RETURN attr
   ```
3. **Shortest path**: Native `shortestPath()` and `allShortestPaths()` functions
   ```cypher
   MATCH p = allShortestPaths((sender)-[:SENT_MAIL|RECEIVED_MAIL*]-(recipient))
   RETURN p
   ```
4. **PRUNE-like behavior**: `WHERE` clauses on variable-length patterns + quantified path patterns (Neo4j 5+)
   ```cypher
   MATCH p = (start)((a)-[:HAS_ATTR]->(b) WHERE b.name = $segments[length(p)]){1,10}
   RETURN last(nodes(p))
   ```
5. **Mature ecosystem**: Largest graph database community, extensive tooling
6. **ACID transactions**: Full multi-statement transaction support
7. **Schema constraints**: Unique, existence, and node key constraints

#### Weaknesses

1. **No true .NET embedded mode**: Neo4j is JVM-based. Embedded requires JVM interop (IKVM) or running a separate process. Not a clean .NET-native embedded experience
2. **GPL license (Community)**: Copyleft — may conflict with project licensing preferences. Enterprise license is commercial
3. **Not multi-model**: Pure graph — no native document collection semantics. All data modeled as nodes/relationships (can store JSON properties, but no collection-level operations like ArangoDB)
4. **No collection-level schema**: Schema is per-label, not per-collection
5. **No auto-increment keys**: Must use `apoc.atomic.add()` or application-level sequences
6. **No streaming cursor equivalent**: Results are materialized in memory (driver supports reactive streams but not `IAsyncEnumerable` natively)
7. **Complex property storage**: Nested objects stored as separate nodes or serialized JSON strings

#### SharpMUSH-Specific Assessment

| Requirement | Support | Notes |
|-------------|---------|-------|
| R1 Graph traversals | ✅ Excellent | Native Cypher with `*1..N` |
| R2 PRUNE | ✅ Yes | `WHERE` on path patterns, quantified patterns (5+) |
| R3 Multi-edge traversals | ✅ Yes | `[:TYPE1\|TYPE2*]` syntax |
| R4 Shortest path | ✅ Excellent | `shortestPath()`, `allShortestPaths()` |
| R5 Transactions | ✅ Yes | Full ACID transactions |
| R6 .NET SDK | ✅ Yes | Official Bolt driver with async |
| R7 Auto-increment keys | ⚠️ Plugin | Via APOC procedures |
| R8 Schema validation | ⚠️ Partial | Constraints but no JSON Schema |
| R9 Embedded mode | ⚠️ Limited | JVM dependency; not .NET-native |
| R10 Standalone mode | ✅ Yes | Docker, Kubernetes, cloud |
| R11 Migration framework | ⚠️ Third-party | Liquigraph, Neo4j-Migrations |
| R12 Cross-graph joins | ✅ Yes | Multi-pattern MATCH clauses |
| R13 Regex matching | ✅ Yes | `=~` regex operator |
| R14 Document CRUD | ⚠️ Adapted | Properties on nodes; no collection semantics |
| R15 Active community | ✅ Excellent | Largest graph DB community |
| R16 License | ❌ GPL/Commercial | Community is GPL v3 |

**Migration effort**: MEDIUM-HIGH — Graph model maps well, but document/collection semantics need rethinking. All 16 collections become node labels. Edge collections map naturally. Complex AQL→Cypher translation needed for inheritance queries but Cypher is capable of expressing them.

#### Resource Requirements

| Resource | Minimum | Recommended | Notes |
|----------|---------|-------------|-------|
| **RAM** | 2 GB | 4–8 GB+ | ⚠️ **JVM-based**: Default heap 512 MB min (`-Xms`), but JVM process overhead adds ~500 MB. Page cache needs additional RAM on top of heap. Total realistic minimum is 2 GB |
| **CPU** | 2 cores | 4+ cores | JVM garbage collection and query planning benefit significantly from multiple cores |
| **Disk** | 500 MB | Varies | JVM + Neo4j install ~400 MB; plus data |
| **Runtime** | **JVM 17+** | JVM 17–21 | ⚠️ Requires Java Runtime Environment. JVM startup time ~2–5s. For .NET projects, this means shipping/managing a JVM alongside the CLR |
| **JVM Heap** | 512 MB (`-Xms512m`) | 2–4 GB (`-Xmx`) | Heap must be tuned; default `dbms.memory.heap.max_size` is 512 MB but production workloads need 2 GB+. GC pauses can affect latency |
| **Page Cache** | 256 MB | ≥ data size | `dbms.memory.pagecache.size` — ideally fits entire graph in RAM; separate from JVM heap |

---

### 3.3 RavenDB

**Type**: Document database with graph query support
**License**: Open Source (AGPL v3 Community), Commercial (Professional/Enterprise)
**Embedded**: Yes — `RavenDB.Embedded` NuGet package (first-class .NET embedded)
**.NET SDK**: Official `RavenDB.Client` (first-class .NET support, LINQ, async)

#### Strengths

1. **Best .NET embedded experience**: Native .NET embedded server
   ```csharp
   EmbeddedServer.Instance.StartServer(new ServerOptions
   {
       DataDirectory = "Data",
       ServerUrl = "http://127.0.0.1:0"
   });
   var store = EmbeddedServer.Instance.GetDocumentStore("SharpMUSH");
   ```
2. **Excellent .NET SDK**: LINQ queries, `IAsyncDocumentSession`, change tracking
   ```csharp
   using var session = store.OpenAsyncSession();
   var player = await session.LoadAsync<SharpPlayer>("players/1");
   player.Name = "NewName";
   await session.SaveChangesAsync();
   ```
3. **ACID transactions**: Multi-document, multi-collection transactions with optimistic concurrency
4. **Graph queries**: RQL `match` syntax for graph traversals (4.x+)
   ```rql
   match (Players as p)-[Location]->(Rooms as r)
   select p.Name, r.Name
   ```
5. **Recursive graph traversal**: `recursive` keyword with min/max/shortest/longest
   ```rql
   match (Objects as obj)-
   recursive as chain(shortest) {
     [Parent]->(Objects as parent)
   }
   ```
6. **Full-text search**: Built-in Lucene-based indexing
7. **Auto-generated IDs**: Configurable strategies including HiLo (sequential-like)
8. **Migration tools**: `Raven.Migrations` NuGet package

#### Weaknesses

1. **Graph queries are limited**: RQL graph support is experimental/basic compared to Cypher or AQL
   - No equivalent to PRUNE (conditional traversal termination)
   - No variable-depth attribute path matching in graph context
   - Graph queries only work with document references, not arbitrary edge collections
2. **No named graphs**: Graph relationships are document references, not separate edge collections. Would need to restructure the 16-graph architecture
3. **No multi-edge-type traversals**: Can't combine HasParent + HasZone in a single recursive pattern
4. **AGPL Community license**: Copyleft; commercial license needed for non-AGPL projects
5. **Graph queries are second-class**: Primary focus is document store; graph features may not evolve as rapidly
6. **No schema validation**: RavenDB is schemaless by design (can use conventions but not enforce)
7. **No PRUNE equivalent**: Recursive traversals can filter but not prune branches

#### SharpMUSH-Specific Assessment

| Requirement | Support | Notes |
|-------------|---------|-------|
| R1 Graph traversals | ⚠️ Basic | `recursive` with depth limits |
| R2 PRUNE | ❌ No | No conditional traversal termination |
| R3 Multi-edge traversals | ❌ No | Single relationship type per recursive block |
| R4 Shortest path | ✅ Yes | `recursive(shortest)` |
| R5 Transactions | ✅ Excellent | Multi-document, cluster-wide |
| R6 .NET SDK | ✅ Excellent | Best-in-class .NET DX |
| R7 Auto-increment keys | ✅ Yes | HiLo strategy for sequential IDs |
| R8 Schema validation | ❌ No | Schemaless design |
| R9 Embedded mode | ✅ Excellent | Native .NET embedded |
| R10 Standalone mode | ✅ Yes | Docker, Kubernetes, cloud |
| R11 Migration framework | ✅ Yes | Raven.Migrations NuGet |
| R12 Cross-graph joins | ❌ No | Cannot combine graph traversals |
| R13 Regex matching | ✅ Yes | Via indexes and RQL |
| R14 Document CRUD | ✅ Excellent | Native document store |
| R15 Active community | ✅ Good | Stable, well-maintained |
| R16 License | ❌ AGPL/Commercial | Community is AGPL v3 |

**Migration effort**: VERY HIGH — Graph model doesn't map well. The 16-graph architecture would need to be flattened into document references. Attribute inheritance queries would need to be implemented entirely in application code or via complex index-based workarounds. RavenDB's graph support is insufficient for SharpMUSH's needs.

#### Resource Requirements

| Resource | Minimum | Recommended | Notes |
|----------|---------|-------------|-------|
| **RAM** | 512 MB | 2 GB+ | .NET-based server; Voron storage engine uses memory-mapped files. Embedded mode has lower baseline (~256 MB) |
| **CPU** | 1 core | 2+ cores | .NET runtime; good single-core performance |
| **Disk** | 200 MB | Varies | Server binaries + Voron data files |
| **Runtime** | .NET 8+ (embedded) | .NET 8+ | ⚠️ Embedded mode bundles its own .NET runtime. Standalone uses its own bundled runtime |

---

### 3.4 PostgreSQL + Marten

**Type**: Relational database with document store overlay (Marten) and graph capabilities via extensions
**License**: PostgreSQL License (permissive), Marten is MIT
**Embedded**: No (PostgreSQL requires a server process; can use Docker)
**.NET SDK**: `Marten` (document store), `Npgsql` (raw SQL), `Dapper` (micro-ORM)

#### Strengths

1. **Most mature and stable**: PostgreSQL is battle-tested for 30+ years
2. **Marten document store**: JSON document storage with LINQ, events, projections
   ```csharp
   await using var session = store.LightweightSession();
   var player = await session.LoadAsync<SharpPlayer>(playerId);
   session.Store(player);
   await session.SaveChangesAsync();
   ```
3. **Graph via recursive CTEs**: SQL `WITH RECURSIVE` for tree/graph traversals
   ```sql
   WITH RECURSIVE parent_chain AS (
     SELECT id, parent_id, 0 AS depth FROM objects WHERE id = @start
     UNION ALL
     SELECT o.id, o.parent_id, pc.depth + 1
     FROM objects o JOIN parent_chain pc ON o.id = pc.parent_id
     WHERE pc.depth < 100
   ) SELECT * FROM parent_chain;
   ```
4. **Apache AGE extension**: Graph query support (Cypher-like syntax) within PostgreSQL. *Note: .NET integration requires raw SQL with `agtype` casting — no mature .NET-native bindings exist, which adds integration friction.*
   ```sql
   SELECT * FROM cypher('sharpmush', $$
     MATCH (obj:Object)-[:HAS_PARENT*1..100]->(parent:Object)
     WHERE obj.key = '1'
     RETURN parent
   $$) as (parent agtype);
   ```
5. **Excellent transaction support**: Full ACID with savepoints, isolation levels
6. **Permissive license**: PostgreSQL License + MIT (Marten)
7. **Migration frameworks**: FluentMigrator, EF Core Migrations, DbUp
8. **Largest ecosystem**: Widest range of hosting, tooling, and community support
9. **`ltree` extension**: Hierarchical path queries (useful for attribute paths)
   ```sql
   SELECT * FROM attributes WHERE path ~ 'root.parent.*.child';
   ```

#### Weaknesses

1. **No native graph model**: Graph queries require either:
   - Recursive CTEs (verbose, limited optimization)
   - Apache AGE extension (adds complexity, separate query language)
   - Application-level graph traversal
2. **No embedded mode**: Always requires a server process (can mitigate with Docker)
3. **No PRUNE equivalent in CTEs**: `WHERE` clause in recursive CTE is not the same as PRUNE — it filters results but doesn't terminate branches
4. **Performance overhead for deep traversals**: Recursive CTEs can be slow for deep/wide graph traversals compared to native graph databases
5. **Impedance mismatch**: Multi-model queries (document + graph) require switching between Marten (LINQ) and raw SQL (CTEs/AGE)
6. **Apache AGE maturity**: Still incubating; .NET integration is limited

#### SharpMUSH-Specific Assessment

| Requirement | Support | Notes |
|-------------|---------|-------|
| R1 Graph traversals | ⚠️ Via CTEs/AGE | Not native; verbose |
| R2 PRUNE | ❌ No | CTEs filter but don't prune |
| R3 Multi-edge traversals | ⚠️ Via JOINs | Possible but complex |
| R4 Shortest path | ⚠️ Via AGE/pgRouting | Extension-dependent |
| R5 Transactions | ✅ Excellent | Gold standard ACID |
| R6 .NET SDK | ✅ Excellent | Marten + Npgsql |
| R7 Auto-increment keys | ✅ Yes | `SERIAL` / `GENERATED ALWAYS AS IDENTITY` |
| R8 Schema validation | ✅ Yes | CHECK constraints, JSON schema |
| R9 Embedded mode | ❌ No | Server process required |
| R10 Standalone mode | ✅ Excellent | Universal hosting |
| R11 Migration framework | ✅ Excellent | Multiple mature options |
| R12 Cross-graph joins | ⚠️ Via SQL JOINs | Possible but loses graph semantics |
| R13 Regex matching | ✅ Yes | `~` and `~*` operators |
| R14 Document CRUD | ✅ Yes | Marten provides this |
| R15 Active community | ✅ Excellent | Largest DB community |
| R16 License | ✅ Permissive | PostgreSQL + MIT |

**Migration effort**: VERY HIGH — Entire graph model needs redesign. 16 named graphs → junction tables or AGE graph. Complex inheritance queries → multi-step recursive CTEs. Massive SQL rewriting. Benefit: rock-solid foundation for everything else.

#### Resource Requirements

| Resource | Minimum | Recommended | Notes |
|----------|---------|-------------|-------|
| **RAM** | 256 MB | 1–2 GB+ | C-native; `shared_buffers` defaults to 128 MB. Very efficient memory usage |
| **CPU** | 1 core | 2+ cores | Efficient query planner; parallelism available for large queries |
| **Disk** | 100 MB | Varies | Compact install; data-dependent storage |
| **Runtime** | None | None | Self-contained C binary; no JVM or CLR needed |

---

### 3.5 ArcadeDB

**Type**: Multi-model (document + graph + key-value + time-series), spiritual successor to OrientDB
**License**: Apache License 2.0
**Embedded**: Yes — JVM-based embedded mode
**.NET SDK**: No official SDK; HTTP/REST API only

#### Strengths

1. **Most similar to ArangoDB**: Multi-model with native graph, document, and key-value support
2. **SQL-like + Cypher + Gremlin**: Supports multiple query languages
   ```sql
   SELECT FROM Object WHERE @type = 'Player'
   MATCH {class: Object, as: obj}-HasParent->{class: Object, as: parent} RETURN parent
   ```
3. **Apache 2.0 license**: Fully permissive
4. **Embedded mode**: JVM-based (same limitation as Neo4j for .NET)
5. **Graph traversals**: Native graph with depth-limited traversals
6. **ACID transactions**: Full transaction support

#### Weaknesses

1. **No .NET SDK**: Only HTTP/REST API — would need to build or adopt a client library
2. **JVM-based embedded**: Same interop issues as Neo4j
3. **Small community**: Relatively niche; fewer resources and examples
4. **OrientDB migration baggage**: While a fresh start, some design decisions inherited
5. **No PRUNE equivalent**: MATCH syntax doesn't support conditional branch pruning
6. **Limited tooling**: No migration framework, limited monitoring

#### SharpMUSH-Specific Assessment

| Requirement | Support | Notes |
|-------------|---------|-------|
| R1 Graph traversals | ✅ Yes | Native MATCH with depth |
| R2 PRUNE | ❌ No | No conditional termination |
| R3 Multi-edge traversals | ✅ Yes | Multiple edge types in MATCH |
| R4 Shortest path | ✅ Yes | Via Gremlin or SQL extension |
| R5 Transactions | ✅ Yes | Full ACID |
| R6 .NET SDK | ❌ No | Must build HTTP client |
| R7 Auto-increment keys | ✅ Yes | Sequences supported |
| R8 Schema validation | ✅ Yes | Type constraints |
| R9 Embedded mode | ⚠️ JVM | Not .NET-native |
| R10 Standalone mode | ✅ Yes | Docker, bare metal |
| R11 Migration framework | ❌ No | Must build custom |
| R12 Cross-graph joins | ✅ Yes | Multi-pattern MATCH |
| R13 Regex matching | ✅ Yes | SQL LIKE + regex |
| R14 Document CRUD | ✅ Yes | Native documents |
| R15 Active community | ⚠️ Small | Growing from OrientDB base |
| R16 License | ✅ Apache 2.0 | Fully permissive |

**Migration effort**: HIGH — Model maps well but no .NET SDK means building a client from scratch. JVM embedded mode is impractical for .NET.

#### Resource Requirements

| Resource | Minimum | Recommended | Notes |
|----------|---------|-------------|-------|
| **RAM** | 1.5 GB | 4 GB+ | ⚠️ **JVM-based**: Default heap 512 MB + JVM overhead ~500 MB. Embedded mode requires same JVM resources |
| **CPU** | 2 cores | 4+ cores | JVM GC threads + query processing; benefits from multiple cores |
| **Disk** | 300 MB | Varies | JVM + ArcadeDB binaries ~250 MB; plus data |
| **Runtime** | **JVM 11+** | JVM 17+ | ⚠️ Requires Java Runtime Environment. Same JVM management burden as Neo4j for .NET projects |
| **JVM Heap** | 512 MB (`-Xmx512m`) | 2 GB+ (`-Xmx`) | Must tune for workload; default may be insufficient for graph-heavy queries |

---

### 3.6 LiteDB

**Type**: Embedded document database for .NET
**License**: MIT
**Embedded**: Yes — .NET-native, single-file storage
**.NET SDK**: Native .NET library (`LiteDB` NuGet)

#### Strengths

1. **Purest .NET embedded**: Single DLL, single file, zero dependencies
   ```csharp
   using var db = new LiteDatabase("SharpMUSH.db");
   var players = db.GetCollection<SharpPlayer>("players");
   players.Insert(new SharpPlayer { Name = "God" });
   ```
2. **MIT license**: Most permissive option
3. **Zero deployment**: No server, no Docker, no configuration
4. **BSON storage**: MongoDB-like document model
5. **LINQ queries**: Full LINQ support for querying

#### Weaknesses

1. **No graph support**: Pure document store — all graph operations must be implemented in application code
2. **No multi-document transactions**: Only single-collection transactions (v5 improved but still limited)
3. **Limited query language**: BsonExpression is basic compared to AQL/Cypher/SQL
4. **Single-threaded writes**: Performance degrades under concurrent write load
5. **No streaming queries**: Results materialized in memory
6. **No schema validation**: Schemaless only
7. **No regex in queries**: Limited pattern matching (BsonExpression supports basic `LIKE` but not full regex)
8. **v5 rewrite status**: LiteDB v5 was a major rewrite; v6 development status unclear

#### SharpMUSH-Specific Assessment

| Requirement | Support | Notes |
|-------------|---------|-------|
| R1 Graph traversals | ❌ No | Must implement in C# |
| R2 PRUNE | ❌ No | N/A |
| R3 Multi-edge traversals | ❌ No | N/A |
| R4 Shortest path | ❌ No | Must implement BFS in C# |
| R5 Transactions | ⚠️ Limited | Single-collection only |
| R6 .NET SDK | ✅ Excellent | Native .NET |
| R7 Auto-increment keys | ✅ Yes | Built-in auto-ID |
| R8 Schema validation | ❌ No | Schemaless |
| R9 Embedded mode | ✅ Excellent | .NET-native single file |
| R10 Standalone mode | ❌ No | Embedded only |
| R11 Migration framework | ❌ No | Must build custom |
| R12 Cross-graph joins | ❌ No | N/A |
| R13 Regex matching | ❌ Limited | BsonExpression LIKE only, no full regex |
| R14 Document CRUD | ✅ Yes | Core feature |
| R15 Active community | ⚠️ Declining | v5/v6 transition uncertain |
| R16 License | ✅ MIT | Most permissive |

**Migration effort**: EXTREME — Every graph operation (traversals, inheritance, shortest path, reachability) must be implemented in application code. Would essentially require writing a graph engine on top of LiteDB. Not recommended.

#### Resource Requirements

| Resource | Minimum | Recommended | Notes |
|----------|---------|-------------|-------|
| **RAM** | 10 MB | 50–100 MB | Extremely lightweight; in-process .NET library with minimal overhead |
| **CPU** | 1 core | 1 core | Single-threaded writes; minimal CPU impact |
| **Disk** | < 1 MB (library) | Varies | Single NuGet DLL; data file grows with content |
| **Runtime** | .NET (host process) | .NET 8+ | Runs inside the host .NET process; no separate runtime needed |

---

### 3.7 DGraph

**Type**: Native distributed graph database
**License**: Apache License 2.0 (Community), Proprietary (Enterprise)
**Embedded**: No
**.NET SDK**: No official SDK; gRPC/HTTP API

#### Strengths

1. **Native graph**: Purpose-built for graph workloads with GraphQL-like DQL
   ```graphql
   {
     player(func: eq(name, "God")) {
       ~has_parent @recurse(depth: 100) {
         name
       }
     }
   }
   ```
2. **Horizontal scalability**: Designed for distributed graph at scale
3. **GraphQL native**: Built-in GraphQL endpoint
4. **Apache 2.0 license**: Permissive

#### Weaknesses

1. **No .NET SDK**: Community libraries only (limited quality)
2. **No embedded mode**: Requires running a cluster (at minimum Alpha + Zero nodes)
3. **DQL is unique**: Not AQL, Cypher, or SQL — another query language to learn
4. **Operational complexity**: Heavy deployment requirements
5. **Community turmoil**: Multiple forks and organizational changes
6. **No document model**: RDF-like predicate model, not document collections
7. **No PRUNE**: Recursive queries use `@recurse` with depth only

#### SharpMUSH-Specific Assessment

Not recommended for SharpMUSH due to lack of .NET SDK, no embedded mode, operational complexity, and no document-model support. The query model (RDF predicates) is a poor fit for SharpMUSH's document+graph hybrid needs.

**Migration effort**: EXTREME — Complete data model redesign + custom client library.

#### Resource Requirements

| Resource | Minimum | Recommended | Notes |
|----------|---------|-------------|-------|
| **RAM** | 2 GB (total cluster) | 8 GB+ | Go-based; Alpha node needs ≥1 GB, Zero node needs ≥512 MB. Must run both |
| **CPU** | 2 cores | 4+ cores | Multi-node architecture requires dedicated CPU per component |
| **Disk** | 500 MB | Varies | Go binaries + Badger data store |
| **Runtime** | None | None | Self-contained Go binaries; no JVM. But requires running multiple processes (Alpha + Zero) |

---

### 3.8 Memgraph

**Type**: Native graph database (property graph model, Cypher-compatible)
**License**: Business Source License 1.1 (Community), Commercial (Enterprise)
**Embedded**: No — standalone server only
**.NET SDK**: Via Neo4j.Driver (Bolt protocol compatible) or `MGCLIENT` (community)
**Written in**: C++ (high-performance, in-memory)

#### Strengths

1. **Cypher-compatible**: Drop-in Cypher support means Neo4j query patterns transfer directly
   ```cypher
   MATCH (obj:Object)-[:HAS_PARENT*1..100]->(parent:Object)
   WHERE obj.key = $startKey
   RETURN parent
   ```
2. **In-memory performance**: All data in RAM — extremely fast traversals and pattern matching. Purpose-built for low-latency graph queries
3. **Low resource overhead**: C++ native, no JVM. Starts in ~1 second with minimal RAM for small datasets
4. **Bolt protocol compatible**: Works with the official `Neo4j.Driver` .NET NuGet package — existing Neo4j client code works with Memgraph
   ```csharp
   var driver = GraphDatabase.Driver("bolt://localhost:7687");
   await using var session = driver.AsyncSession();
   var result = await session.RunAsync("MATCH (n:Player) RETURN n.name");
   ```
5. **MAGE graph algorithms**: Open-source algorithm library with PageRank, shortest path, community detection, BFS/DFS, etc.
6. **Triggers and streams**: Real-time triggers on data changes; Kafka/Pulsar stream integration
7. **ACID transactions**: Full multi-statement transaction support
8. **Docker-friendly**: Lightweight Docker image (~200 MB), fast startup

#### Weaknesses

1. **No embedded mode**: Must run as a separate process (Docker or bare metal). No in-process option for .NET
2. **In-memory limitation**: Entire dataset must fit in RAM (on-disk storage available in Enterprise only). For SharpMUSH's small datasets this is fine, but sets a ceiling
3. **BSL license**: Similar to SurrealDB — BSL 1.1 with eventual open-source conversion
4. **No PRUNE equivalent**: Cypher `WHERE` filters on paths but does not prune branches the way ArangoDB's PRUNE does. Same limitation as Neo4j
5. **Not multi-model**: Pure graph database — no native document collection semantics. Properties on nodes, but no collection-level CRUD like ArangoDB
6. **No auto-increment keys**: Must implement application-level sequences
7. **No migration framework**: Schema evolution must be managed manually or with custom tooling
8. **Smaller ecosystem than Neo4j**: Fewer tutorials, extensions, and community resources

#### SharpMUSH-Specific Assessment

| Requirement | Support | Notes |
|-------------|---------|-------|
| R1 Graph traversals | ✅ Excellent | Native Cypher with `*1..N` |
| R2 PRUNE | ⚠️ Partial | `WHERE` on paths; no branch-level pruning. MAGE procedures can help |
| R3 Multi-edge traversals | ✅ Yes | `[:TYPE1\|TYPE2*]` Cypher syntax |
| R4 Shortest path | ✅ Excellent | `shortestPath()` + MAGE algorithms |
| R5 Transactions | ✅ Yes | Full ACID transactions |
| R6 .NET SDK | ✅ Yes | Via Neo4j.Driver (Bolt compatible) |
| R7 Auto-increment keys | ❌ No | Must simulate in application |
| R8 Schema validation | ⚠️ Partial | Constraints (unique, exists) but no JSON Schema |
| R9 Embedded mode | ❌ No | Standalone only |
| R10 Standalone mode | ✅ Yes | Docker, bare metal, Kubernetes |
| R11 Migration framework | ❌ No | Must build custom |
| R12 Cross-graph joins | ✅ Yes | Multi-MATCH Cypher patterns |
| R13 Regex matching | ✅ Yes | `=~` Cypher regex |
| R14 Document CRUD | ⚠️ Adapted | Properties on nodes; no collection semantics |
| R15 Active community | ⚠️ Medium | Growing; smaller than Neo4j but active |
| R16 License | ⚠️ BSL | BSL 1.1; eventual Apache 2.0 per version |

**Migration effort**: MEDIUM — Cypher queries from a Neo4j migration would work directly. Same graph model limitations as Neo4j (no document collections), but much lower resource overhead. Good choice if Neo4j's JVM cost is unacceptable.

#### Resource Requirements

| Resource | Minimum | Recommended | Notes |
|----------|---------|-------------|-------|
| **RAM** | 256 MB | 1 GB+ | C++ native; **no JVM overhead**. In-memory store, so RAM = data size + ~100–200 MB overhead. For SharpMUSH's small graph this is very modest |
| **CPU** | 1 core | 2+ cores | C++ query engine is efficient; single-core performance is excellent |
| **Disk** | 100 MB | Varies | ~200 MB Docker image. Snapshots written to disk for persistence |
| **Runtime** | None | None | Self-contained C++ binary; **no JVM, no CLR**. ~1s startup time |

---

### 3.9 FalkorDB

**Type**: Native graph database (formerly RedisGraph, now standalone)
**License**: Server Side Public License (SSPL) v1
**Embedded**: No — standalone server (previously a Redis module)
**.NET SDK**: Community `NRedisStack` or raw Redis protocol; no dedicated .NET driver
**Written in**: C (extremely lightweight)

#### Strengths

1. **Cypher subset support**: Supports a subset of openCypher for graph queries
   ```cypher
   GRAPH.QUERY sharpmush "MATCH (p:Player)-[:HAS_PARENT*1..10]->(parent) RETURN parent"
   ```
2. **Extremely fast**: C-based, in-memory graph with custom sparse matrix engine (GraphBLAS). Benchmark leaders for small-to-medium graph queries
3. **Very low resource footprint**: Lighter than any other graph database — runs comfortably in 50 MB for small graphs
4. **Redis protocol compatible**: Can leverage existing Redis client libraries
5. **Graph algorithms**: Built-in shortest path, BFS, DFS via Cypher extensions

#### Weaknesses

1. **SSPL license**: Not OSI-approved open source; may conflict with licensing requirements. More restrictive than BSL
2. **Limited Cypher support**: Only a subset of openCypher — no quantified path patterns, limited `WHERE` on paths
3. **No .NET SDK**: Must use Redis client with raw `GRAPH.QUERY` commands — poor developer experience
4. **No PRUNE equivalent**: Limited path filtering compared to even Neo4j/Memgraph
5. **In-memory only**: No on-disk storage option in community edition. Data must fit in RAM
6. **No multi-model**: Pure graph only — no document storage
7. **No ACID transactions**: Redis-style single-command atomicity only
8. **Small ecosystem**: Relatively new as standalone product (forked from RedisGraph 2023)
9. **No migration framework**: Must build custom

#### SharpMUSH-Specific Assessment

| Requirement | Support | Notes |
|-------------|---------|-------|
| R1 Graph traversals | ✅ Yes | Cypher subset with depth limits |
| R2 PRUNE | ❌ No | Very limited path filtering |
| R3 Multi-edge traversals | ⚠️ Limited | Basic multi-type support |
| R4 Shortest path | ✅ Yes | Built-in `shortestPath()` |
| R5 Transactions | ❌ No | Single-command atomicity only |
| R6 .NET SDK | ❌ Poor | Raw Redis commands via NRedisStack |
| R7 Auto-increment keys | ❌ No | Must simulate |
| R8 Schema validation | ❌ No | No schema enforcement |
| R9 Embedded mode | ❌ No | Standalone only |
| R10 Standalone mode | ✅ Yes | Docker, bare metal |
| R11 Migration framework | ❌ No | Must build custom |
| R12 Cross-graph joins | ⚠️ Limited | Single graph per query |
| R13 Regex matching | ⚠️ Limited | Basic pattern support |
| R14 Document CRUD | ❌ No | Properties on nodes only |
| R15 Active community | ⚠️ Small | New standalone project; growing |
| R16 License | ❌ SSPL | Not OSI-approved; restrictive |

**Migration effort**: EXTREME — Limited Cypher subset, no transactions, no document model, no .NET SDK. Not a viable candidate for SharpMUSH.

#### Resource Requirements

| Resource | Minimum | Recommended | Notes |
|----------|---------|-------------|-------|
| **RAM** | 50 MB | 256 MB+ | C-native; extremely efficient. In-memory store, so RAM = data size + minimal overhead. Lightest option in this analysis |
| **CPU** | 1 core | 1–2 cores | Single-threaded query execution (like Redis); very efficient |
| **Disk** | 20 MB | Varies | Tiny binary; RDB snapshots for persistence |
| **Runtime** | None | None | Self-contained C binary; no JVM or CLR |

---

### 3.10 Kùzu

**Type**: Embedded graph database (property graph, Cypher-compatible)
**License**: MIT
**Embedded**: Yes — native C++ library with in-process bindings
**.NET SDK**: No official .NET SDK; Python, Node.js, Rust, Java, Go bindings available
**Written in**: C++ (columnar storage, vectorized execution)

#### Strengths

1. **True embedded graph**: Runs in-process like SQLite but for graphs — no server needed
2. **Full Cypher support**: Comprehensive openCypher implementation with extensions
3. **MIT license**: Most permissive graph database license available
4. **Columnar + vectorized**: Excellent performance for analytical graph queries; competitive with Neo4j on benchmarks
5. **Recursive path queries**: Full variable-length path patterns with `WHERE` filtering
6. **Very low overhead**: C++ embedded library; ~50 MB memory for small graphs
7. **Disk-based storage**: Unlike Memgraph/FalkorDB, data doesn't need to fit in RAM

#### Weaknesses

1. **No .NET SDK**: This is the critical blocker. Only Python, Node.js, Rust, Java, Go bindings exist. Would need to build C# bindings via P/Invoke or wait for official support
2. **No standalone server mode**: Embedded only — no Docker deployment, no client-server architecture
3. **No PRUNE equivalent**: Cypher `WHERE` on paths but no branch pruning
4. **Young project**: v0.x releases; API may change. Fewer production deployments
5. **No transactions API**: Single-statement atomicity; no multi-statement ACID
6. **No multi-model**: Pure graph — no document collections
7. **No migration framework**: Must build custom

#### SharpMUSH-Specific Assessment

| Requirement | Support | Notes |
|-------------|---------|-------|
| R1 Graph traversals | ✅ Excellent | Full Cypher with `*1..N` |
| R2 PRUNE | ⚠️ Partial | `WHERE` on paths; no branch pruning |
| R3 Multi-edge traversals | ✅ Yes | Cypher multi-type patterns |
| R4 Shortest path | ✅ Yes | `shortestPath()` function |
| R5 Transactions | ❌ No | Single-statement atomicity only |
| R6 .NET SDK | ❌ No | No .NET bindings; P/Invoke needed |
| R7 Auto-increment keys | ❌ No | Must simulate |
| R8 Schema validation | ⚠️ Partial | Table definitions with types |
| R9 Embedded mode | ✅ Excellent | Native C++ in-process |
| R10 Standalone mode | ❌ No | Embedded only |
| R11 Migration framework | ❌ No | Must build custom |
| R12 Cross-graph joins | ✅ Yes | Multi-MATCH Cypher |
| R13 Regex matching | ✅ Yes | Cypher `=~` |
| R14 Document CRUD | ❌ No | Structured tables, not documents |
| R15 Active community | ⚠️ Growing | Active development; small but engaged |
| R16 License | ✅ MIT | Most permissive |

**Migration effort**: EXTREME — No .NET SDK is an absolute blocker. If .NET bindings were added, the Cypher compatibility would make it MEDIUM effort. Watch this project for future .NET support.

#### Resource Requirements

| Resource | Minimum | Recommended | Notes |
|----------|---------|-------------|-------|
| **RAM** | 50 MB | 256 MB+ | C++ embedded; very efficient memory usage. Disk-based storage means data doesn't need to fit in RAM |
| **CPU** | 1 core | 2+ cores | Vectorized query engine benefits from SIMD; good single-core too |
| **Disk** | 10 MB (library) | Varies | Small library footprint; columnar data files on disk |
| **Runtime** | None (C++ native) | None | In-process native library; **no JVM, no CLR for DB itself**. But would need P/Invoke layer for .NET |

---

### 3.11 JanusGraph

**Type**: Distributed graph database (built on pluggable storage backends)
**License**: Apache License 2.0
**Embedded**: Technically possible (JVM in-process) but not practical for .NET
**.NET SDK**: No official SDK; Gremlin.Net (Apache TinkerPop) for query protocol
**Written in**: Java (JVM-based, inherits JVM resource profile)

#### Strengths

1. **Apache TinkerPop / Gremlin**: Standard graph traversal language with .NET support via `Gremlin.Net`
   ```csharp
   var g = traversal().WithRemote(new DriverRemoteConnection(
       new GremlinClient(new GremlinServer("localhost", 8182))));
   var parents = await g.V().Has("Object", "key", startKey)
       .Repeat(__.Out("HAS_PARENT")).Until(__.Not(__.Out("HAS_PARENT")))
       .Limit<Vertex>(100).ToListAsync();
   ```
2. **Pluggable storage**: Can use Cassandra, HBase, BerkeleyDB, or Scylla as backend
3. **Apache 2.0 license**: Fully permissive
4. **Horizontal scalability**: Designed for massive distributed graphs
5. **Rich graph features**: Mixed index backends (Elasticsearch, Solr, Lucene), graph partitioning

#### Weaknesses

1. **Very heavy JVM footprint**: JanusGraph + storage backend + index backend = massive resource requirements
2. **Operational complexity**: Requires managing JanusGraph server + storage backend (Cassandra/HBase) + optional index backend
3. **No embedded mode for .NET**: JVM-only embedded; Gremlin.Net is remote only
4. **Gremlin is verbose**: Compared to Cypher or AQL, Gremlin traversals are more procedural and harder to read
5. **No PRUNE**: Gremlin has `until()` step for recursion but no branch-level pruning
6. **Slow startup**: JVM + storage backend initialization can take 30–60 seconds
7. **No document model**: Pure graph with properties; no collection-level document operations
8. **Declining momentum**: Less active development compared to Neo4j or Memgraph

#### SharpMUSH-Specific Assessment

| Requirement | Support | Notes |
|-------------|---------|-------|
| R1 Graph traversals | ✅ Yes | Gremlin `repeat().until()` with depth |
| R2 PRUNE | ❌ No | `until()` terminates but doesn't prune branches |
| R3 Multi-edge traversals | ✅ Yes | Gremlin can traverse multiple edge labels |
| R4 Shortest path | ✅ Yes | `shortestPath()` step |
| R5 Transactions | ✅ Yes | Full ACID (backend-dependent) |
| R6 .NET SDK | ⚠️ Via Gremlin.Net | Official TinkerPop .NET driver; remote only |
| R7 Auto-increment keys | ❌ No | Must simulate |
| R8 Schema validation | ✅ Yes | PropertyKey and EdgeLabel schemas |
| R9 Embedded mode | ❌ No | JVM-only; not practical for .NET |
| R10 Standalone mode | ✅ Yes | Docker, Kubernetes |
| R11 Migration framework | ❌ No | Must build custom |
| R12 Cross-graph joins | ✅ Yes | Multi-step Gremlin traversals |
| R13 Regex matching | ✅ Yes | `TextP.regex()` predicate |
| R14 Document CRUD | ❌ No | Properties on vertices only |
| R15 Active community | ⚠️ Declining | Less active; major contributors moved on |
| R16 License | ✅ Apache 2.0 | Fully permissive |

**Migration effort**: VERY HIGH — Gremlin is capable but verbose. No document model. Heavy operational burden. JVM resource overhead is the highest of any option.

#### Resource Requirements

| Resource | Minimum | Recommended | Notes |
|----------|---------|-------------|-------|
| **RAM** | 4 GB (total) | 8–16 GB+ | ⚠️ **Heaviest JVM option**: JanusGraph JVM ~1.5 GB + storage backend (Cassandra ~2 GB or HBase ~1 GB) + optional index backend (Elasticsearch ~1 GB). Combined minimum is realistically 4 GB |
| **CPU** | 4 cores | 8+ cores | Multiple JVM processes (JanusGraph + Cassandra/HBase); each needs dedicated cores |
| **Disk** | 1 GB | Varies | JanusGraph + storage backend binaries; data-dependent |
| **Runtime** | **JVM 11+** | JVM 17+ | ⚠️ Multiple JVM instances (JanusGraph server + often Cassandra). JVM heap must be tuned per component |
| **JVM Heap** | 1 GB (JanusGraph) + 1 GB (Cassandra) | 2–4 GB each | Each JVM process needs separate heap tuning. Cassandra is famously heap-hungry |

---

### 3.12 TypeDB

**Type**: Strongly-typed knowledge graph database with inference engine
**License**: Mozilla Public License 2.0 (MPL 2.0)
**Embedded**: No — standalone server only (was Java-based, now Rust core since TypeDB 3.x)
**.NET SDK**: No official .NET SDK; Java, Python, Node.js, Rust drivers available
**Written in**: Rust (core engine, since v3.x) / Java (v2.x and client libraries)

#### Strengths

1. **Type system with inference**: Powerful schema with inheritance, roles, and rule-based reasoning
   ```typeql
   define
     object sub entity, owns name, plays parent-child:child, plays parent-child:parent;
     parent-child sub relation, relates parent, relates child;
   ```
2. **TypeQL query language**: Declarative, pattern-matching query language
   ```typeql
   match
     $obj isa object, has name $name;
     $parent isa object;
     (child: $obj, parent: $parent) isa parent-child;
   get $parent;
   ```
3. **Inference engine**: Can derive new relationships from rules — potentially useful for attribute inheritance
   ```typeql
   rule inherited-attribute:
     when {
       (child: $obj, parent: $parent) isa parent-child;
       $parent has attribute $attr;
       not { $obj has attribute $attr; };
     } then {
       $obj has attribute $attr;
     };
   ```
4. **Transition to Rust**: TypeDB 3.x core engine is Rust-based, improving performance and reducing JVM dependency
5. **MPL 2.0 license**: File-level copyleft; more permissive than GPL/AGPL

#### Weaknesses

1. **No .NET SDK**: Only Java, Python, Node.js, Rust drivers. Would need to build .NET client via gRPC
2. **Complex learning curve**: TypeQL is a unique language; neither Cypher nor SQL
3. **No embedded mode**: Must run as standalone server
4. **No document model**: Entity-relationship model, not document collections
5. **Version 3.x transition**: Major rewrite from Java to Rust; ecosystem disruption. Many v2 resources are outdated
6. **No Cypher/Gremlin compatibility**: Cannot reuse queries from other graph databases
7. **Inference overhead**: Rule engine adds latency and complexity; must be carefully managed
8. **Small ecosystem**: Niche product with limited community compared to Neo4j or PostgreSQL
9. **No auto-increment keys**: Must simulate

#### SharpMUSH-Specific Assessment

| Requirement | Support | Notes |
|-------------|---------|-------|
| R1 Graph traversals | ✅ Yes | TypeQL pattern matching with depth |
| R2 PRUNE | ⚠️ Via inference | Rules could implement conditional logic, but not equivalent to PRUNE |
| R3 Multi-edge traversals | ✅ Yes | Multi-relation patterns in TypeQL |
| R4 Shortest path | ⚠️ Limited | No built-in shortest path; must implement via rules or application code |
| R5 Transactions | ✅ Yes | Full ACID transactions |
| R6 .NET SDK | ❌ No | No .NET driver; would need gRPC client |
| R7 Auto-increment keys | ❌ No | Must simulate |
| R8 Schema validation | ✅ Excellent | Strongest type system of any option |
| R9 Embedded mode | ❌ No | Standalone only |
| R10 Standalone mode | ✅ Yes | Docker, bare metal |
| R11 Migration framework | ❌ No | Schema evolution via TypeQL `define`/`undefine` |
| R12 Cross-graph joins | ✅ Yes | Multi-pattern TypeQL |
| R13 Regex matching | ⚠️ Limited | Basic string matching |
| R14 Document CRUD | ❌ No | Entity-relationship model |
| R15 Active community | ⚠️ Small | Niche; v3 transition causing churn |
| R16 License | ✅ MPL 2.0 | File-level copyleft; permissive enough |

**Migration effort**: EXTREME — No .NET SDK, unique query language, no document model. The inference engine is interesting for attribute inheritance but the complete lack of .NET support makes this impractical.

#### Resource Requirements

| Resource | Minimum | Recommended | Notes |
|----------|---------|-------------|-------|
| **RAM** | 1 GB (v3 Rust) / 2 GB (v2 Java) | 4 GB+ | v3.x Rust core is more efficient than v2 Java. Inference engine adds memory overhead for rules |
| **CPU** | 2 cores | 4+ cores | Inference engine is CPU-intensive; reasoning over rules adds computation |
| **Disk** | 200 MB | Varies | Server binaries + data; v3 is leaner than v2 |
| **Runtime** | None (v3 Rust) / **JVM 11+** (v2) | None (v3) | ⚠️ v2.x was fully JVM-based with ~1 GB heap minimum. v3.x Rust core eliminates JVM dependency for server, but Java client libraries still need JVM if used directly |

---

## 4. Feature Comparison Matrix

| Feature | ArangoDB | SurrealDB | Neo4j | Memgraph | RavenDB | PostgreSQL | ArcadeDB | LiteDB | FalkorDB | Kùzu | JanusGraph | TypeDB |
|---------|----------|-----------|-------|----------|---------|------------|----------|--------|----------|------|------------|--------|
| **Graph Traversals** | ✅ Excellent | ✅ Good | ✅ Excellent | ✅ Excellent | ⚠️ Basic | ⚠️ Via CTEs | ✅ Good | ❌ None | ✅ Good | ✅ Excellent | ✅ Yes | ✅ Yes |
| **PRUNE (conditional)** | ✅ Native | ❌ No | ✅ Via patterns | ⚠️ Partial | ❌ No | ❌ No | ❌ No | ❌ No | ❌ No | ⚠️ Partial | ❌ No | ⚠️ Via rules |
| **Shortest Path** | ✅ Native | ✅ v2.2+ | ✅ Native | ✅ MAGE | ✅ recursive | ⚠️ Extension | ✅ Gremlin | ❌ No | ✅ Yes | ✅ Yes | ✅ Yes | ⚠️ Limited |
| **Multi-edge Traversal** | ✅ Native | ✅ Yes | ✅ Native | ✅ Yes | ❌ No | ⚠️ JOINs | ✅ Yes | ❌ No | ⚠️ Limited | ✅ Yes | ✅ Yes | ✅ Yes |
| **ACID Transactions** | ✅ Yes | ⚠️ Server | ✅ Yes | ✅ Yes | ✅ Excellent | ✅ Excellent | ✅ Yes | ⚠️ Limited | ❌ No | ❌ No | ✅ Yes | ✅ Yes |
| **.NET SDK Quality** | ✅ Good | ⚠️ Maturing | ✅ Good | ✅ Via Bolt | ✅ Excellent | ✅ Excellent | ❌ None | ✅ Excellent | ❌ Poor | ❌ None | ⚠️ Gremlin.Net | ❌ None |
| **Embedded Mode** | ❌ No | ✅ SurrealKv | ⚠️ JVM | ❌ No | ✅ .NET | ❌ No | ⚠️ JVM | ✅ .NET | ❌ No | ✅ C++ | ❌ JVM | ❌ No |
| **Auto-increment Keys** | ✅ Native | ❌ Simulate | ⚠️ APOC | ❌ Simulate | ✅ HiLo | ✅ SERIAL | ✅ Sequences | ✅ Auto-ID | ❌ No | ❌ No | ❌ No | ❌ No |
| **Document CRUD** | ✅ Native | ✅ Native | ⚠️ Properties | ⚠️ Properties | ✅ Native | ✅ Marten | ✅ Native | ✅ Native | ❌ No | ❌ No | ❌ No | ❌ No |
| **License** | Apache 2.0 | BSL 1.1 | GPL v3 | BSL 1.1 | AGPL v3 | PG+MIT | Apache 2.0 | MIT | SSPL | MIT | Apache 2.0 | MPL 2.0 |
| **Standalone Mode** | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Yes | ❌ No | ✅ Yes | ❌ No | ✅ Yes | ✅ Yes |
| **Min RAM** | 1 GB | 50 MB–256 MB | ⚠️ 2 GB (JVM) | 256 MB | 512 MB | 256 MB | ⚠️ 1.5 GB (JVM) | 10 MB | 50 MB | 50 MB | ⚠️ 4 GB (JVM×2) | 1–2 GB |
| **Runtime Dependency** | None (C++) | None (Rust) | ⚠️ JVM 17+ | None (C++) | .NET 8+ | None (C) | ⚠️ JVM 11+ | .NET (host) | None (C) | None (C++) | ⚠️ JVM 11+ (×2) | None (Rust v3) |

---

## 5. Resource Requirements Comparison

This section compares the minimum and recommended resource profiles across all candidates. **JVM-based databases** (Neo4j, ArcadeDB, JanusGraph) carry significant overhead due to Java heap management, garbage collection, and JVM process footprint — particularly important when running alongside a .NET application.

### 5.1 Minimum RAM Comparison (Sorted Lightest → Heaviest)

| Database | Min RAM | Language | JVM? | Notes |
|----------|---------|----------|------|-------|
| **LiteDB** | 10 MB | C# (.NET) | No | In-process library; lightest possible |
| **FalkorDB** | 50 MB | C | No | In-memory graph; C-native |
| **Kùzu** | 50 MB | C++ | No | Embedded; disk-based storage |
| **SurrealDB** (embedded) | 50 MB | Rust | No | SurrealKv embedded mode |
| **Memgraph** | 256 MB | C++ | No | In-memory graph; very efficient |
| **PostgreSQL** | 256 MB | C | No | Mature; efficient memory usage |
| **SurrealDB** (standalone) | 256 MB | Rust | No | Server mode |
| **RavenDB** | 512 MB | C# (.NET) | No | .NET-based; Voron storage engine |
| **ArangoDB** (current) | 1 GB | C++ | No | Memory-mapped data files |
| **TypeDB** (v3) | 1 GB | Rust (v3) | No* | *v3 Rust core; v2 was JVM-based at 2 GB+ |
| **ArcadeDB** | ⚠️ 1.5 GB | Java | **Yes** | JVM heap 512 MB + JVM overhead ~500 MB + OS |
| **Neo4j** | ⚠️ 2 GB | Java | **Yes** | JVM heap 512 MB + JVM overhead ~500 MB + page cache 256 MB+ |
| **DGraph** | 2 GB | Go | No | Alpha (1 GB) + Zero (512 MB); multi-process |
| **JanusGraph** | ⚠️ 4 GB | Java | **Yes (×2)** | JanusGraph JVM (~1.5 GB) + Cassandra JVM (~2 GB). Heaviest option |

### 5.2 JVM Impact Assessment

For a .NET project like SharpMUSH, JVM-based databases impose a **dual-runtime tax**:

| Factor | Impact |
|--------|--------|
| **Memory overhead** | JVM process itself uses 300–500 MB before any application data. Heap must be pre-allocated (`-Xms`/`-Xmx`) |
| **Garbage collection** | GC pauses can cause latency spikes of 10–200ms. Must tune GC strategy (G1, ZGC, Shenandoah) |
| **Startup time** | JVM cold start: 2–5 seconds. Warm-up period for JIT compilation: 30–60 seconds |
| **Operational burden** | Must install/manage JVM separately from .NET runtime. Version compatibility issues (JVM 11 vs 17 vs 21) |
| **Container size** | JVM base images add 200–400 MB to Docker image size |
| **Total deployment** | Running .NET app + JVM database = two runtimes, two GCs, two sets of memory tuning |

**Databases affected**: Neo4j, ArcadeDB, JanusGraph (most severely), TypeDB v2.x (v3.x moved to Rust)

### 5.3 Resource Efficiency Tiers

| Tier | Databases | Min RAM | Runtime Tax | Best For |
|------|-----------|---------|-------------|----------|
| 🟢 **Ultra-Light** | LiteDB, FalkorDB, Kùzu | 10–50 MB | None | Embedded/edge deployments |
| 🟢 **Light** | SurrealDB (embedded), Memgraph | 50–256 MB | None | Small-to-medium deployments; development |
| 🟡 **Moderate** | PostgreSQL, SurrealDB (standalone), RavenDB, ArangoDB | 256 MB–1 GB | None/.NET | Production single-server |
| 🔴 **Heavy** | Neo4j, ArcadeDB, TypeDB v2 | 1.5–2 GB | ⚠️ JVM | Production; JVM overhead significant |
| 🔴 **Very Heavy** | JanusGraph, DGraph | 2–4 GB | ⚠️ JVM×2 / Multi-process | Distributed; significant ops burden |

---

## 6. Migration Complexity Assessment

### 6.1 Effort Estimates

| Alternative | Overall Effort | Graph Rewrite | SDK Integration | Data Migration | Schema/Migration |
|-------------|---------------|---------------|-----------------|----------------|------------------|
| **SurrealDB** | 🟡 HIGH | Major (PRUNE) | Medium (SDK gaps) | Low (model fits) | Medium (custom) |
| **Neo4j** | 🟡 MEDIUM-HIGH | Medium (Cypher capable) | Low (good SDK) | Medium (remodel) | Medium (third-party) |
| **Memgraph** | 🟡 MEDIUM | Medium (Cypher capable) | Low (Bolt compat) | Medium (remodel) | Medium (custom) |
| **RavenDB** | 🔴 VERY HIGH | Extreme (no graph) | Low (best SDK) | High (flatten graphs) | Low (migration NuGet) |
| **PostgreSQL** | 🔴 VERY HIGH | Extreme (CTEs/AGE) | Low (Marten) | High (relational) | Low (many options) |
| **ArcadeDB** | 🔴 HIGH | Medium (model fits) | Extreme (no SDK) | Low (model fits) | High (custom) |
| **LiteDB** | 🔴 EXTREME | Extreme (no graph) | Low (native .NET) | High (flatten) | High (custom) |
| **FalkorDB** | 🔴 EXTREME | High (limited Cypher) | Extreme (raw Redis) | High (remodel) | High (custom) |
| **Kùzu** | 🔴 EXTREME | Medium (full Cypher) | Extreme (no .NET) | Medium (remodel) | High (custom) |
| **JanusGraph** | 🔴 VERY HIGH | High (Gremlin verbose) | Medium (Gremlin.Net) | Medium (remodel) | High (custom) |
| **TypeDB** | 🔴 EXTREME | High (TypeQL) | Extreme (no .NET) | High (entity model) | High (custom) |

### 6.2 Critical Migration Challenges

#### Challenge 1: Attribute Inheritance Queries (ALL alternatives)
The 70-line AQL query for `GetAttributeWithInheritanceAsync` is the hardest to migrate. It requires:
- Nested graph traversal (Self → Parent → Zone)
- Conditional branch pruning (PRUNE)
- Cross-graph joins (Objects + Parents + Zones + Attributes)
- Lazy evaluation support

**SurrealDB**: Would need 3 separate queries composed in application code, losing single-roundtrip efficiency.
**Neo4j**: Can express this in Cypher using `UNION` + variable-length patterns, but requires careful optimization.
**Memgraph**: Same Cypher capabilities as Neo4j; MAGE algorithms may help with traversal. Lower resource cost than Neo4j.
**Others**: Must implement entirely in application code.

#### Challenge 2: PRUNE Semantics
PRUNE is unique to ArangoDB. It stops traversal down a branch when a condition is met, reducing work. Without it:
- Application must fetch more data and filter client-side
- Or use multiple smaller queries
- Performance impact depends on graph depth/breadth

#### Challenge 3: Named Graphs
ArangoDB's named graphs define valid edge-vertex relationships at the schema level. Alternatives handle this differently:
- **SurrealDB**: Table-based relations (similar concept)
- **Neo4j** / **Memgraph**: Relationship types (no schema constraint on endpoints)
- **RavenDB**: Document references (no graph schema)
- **PostgreSQL**: Foreign keys (rigid) or AGE graph (flexible)

---

## 7. Recommendation Summary

### Tier 1: Viable Alternatives (Recommended for Evaluation)

#### 🥇 SurrealDB — Best Overall Fit
- **Why**: Only alternative with embedded mode + native graph + multi-model + active development
- **Key risk**: .NET SDK maturity (no client-side transactions), PRUNE equivalent missing
- **Best for**: If embedded mode is a high priority and you can accept application-level inheritance logic
- **License concern**: BSL 1.1 (usage is allowed; each version converts to Apache 2.0 four years after its release date)
- **Resource profile**: 🟢 Light — 50 MB embedded, 256 MB standalone, no JVM
- **Estimated migration**: 4-6 weeks for a senior developer

#### 🥈 Memgraph — Best Lightweight Graph Alternative
- **Why**: Full Cypher compatibility (same queries as Neo4j) with dramatically lower resource footprint (256 MB vs 2 GB). C++ native, no JVM. Bolt-compatible so it works with `Neo4j.Driver` NuGet
- **Key risk**: BSL license, no embedded mode, no document model, smaller ecosystem than Neo4j
- **Best for**: If you want Neo4j-grade Cypher queries without the JVM tax. Ideal for containerized deployments
- **Resource profile**: 🟢 Light — 256 MB minimum, no JVM, ~1s startup
- **Estimated migration**: 3-5 weeks for a senior developer (same Cypher effort as Neo4j)

#### 🥉 Neo4j — Most Mature Graph Capabilities
- **Why**: Most capable graph query language (Cypher), can express all SharpMUSH patterns
- **Key risk**: GPL license, JVM-based embedded mode, document model impedance, heavy resource footprint
- **Best for**: If graph query expressiveness is the top priority and you can accept JVM overhead + licensing
- **Resource profile**: 🔴 Heavy — 2 GB minimum (JVM heap + page cache), JVM 17+ required
- **Estimated migration**: 3-5 weeks for a senior developer (Cypher can express the complex queries)

### Tier 2: Possible but Significant Trade-offs

#### RavenDB — Best .NET Experience
- **Why**: Best-in-class .NET embedded experience, excellent SDK, mature ecosystem
- **Key risk**: Graph support is insufficient for SharpMUSH's needs; would require application-level graph engine
- **Best for**: If you're willing to rewrite graph operations in C# and want the best developer experience
- **Resource profile**: 🟡 Moderate — 512 MB minimum, .NET-native
- **Estimated migration**: 6-10 weeks (graph engine construction)

#### PostgreSQL + Marten — Most Pragmatic Foundation
- **Why**: Rock-solid foundation, permissive license, universal hosting
- **Key risk**: No native graph model; recursive CTEs are verbose and slower for deep traversals
- **Best for**: If long-term stability and ecosystem breadth outweigh graph query elegance
- **Resource profile**: 🟡 Moderate — 256 MB minimum, C-native, no JVM
- **Estimated migration**: 6-10 weeks (graph layer construction)

### Tier 3: Not Recommended

| Alternative | Why Not | Resource Profile |
|-------------|---------|-----------------|
| **ArcadeDB** | No .NET SDK; must build from scratch | 🔴 1.5 GB (JVM) |
| **LiteDB** | No graph support; too limited for SharpMUSH | 🟢 10 MB |
| **DGraph** | No .NET SDK; RDF model is wrong paradigm; operational complexity | 🔴 2 GB (multi-process) |
| **FalkorDB** | No .NET SDK; SSPL license; limited Cypher; no transactions | 🟢 50 MB |
| **Kùzu** | No .NET SDK (critical blocker); excellent graph but embedded-only | 🟢 50 MB |
| **JanusGraph** | Heaviest resource footprint (4 GB+); JVM×2; operational complexity | 🔴 4 GB (JVM×2) |
| **TypeDB** | No .NET SDK; unique query language; niche ecosystem | 🟡 1–2 GB |

### Tier 3 Watch List

| Alternative | Watch For | Would Move To |
|-------------|-----------|---------------|
| **Kùzu** | Official .NET bindings | Tier 1 (MIT license, embedded graph, full Cypher, 50 MB) |
| **TypeDB** | .NET SDK + ecosystem growth | Tier 2 (inference engine could solve attribute inheritance) |

### Decision Framework

```
If embedded mode is critical:
  → SurrealDB (SurrealKv) or RavenDB (RavenDB.Embedded)

If graph query power is critical:
  → Memgraph (lightweight Cypher) or Neo4j (mature Cypher) or stay with ArangoDB

If resource efficiency matters:
  → Memgraph (256 MB, no JVM) > SurrealDB (50 MB embedded) > PostgreSQL (256 MB)
  → AVOID: Neo4j (2 GB JVM), JanusGraph (4 GB JVM×2), ArcadeDB (1.5 GB JVM)

If license must be permissive:
  → PostgreSQL (PostgreSQL+MIT), Kùzu (MIT), or ArcadeDB (Apache 2.0)

If .NET developer experience is critical:
  → RavenDB > PostgreSQL+Marten > SurrealDB > Memgraph (via Neo4j.Driver) > Neo4j

If migration effort must be minimal:
  → Stay with ArangoDB, or Memgraph/Neo4j as closest graph alternatives
```

---

## Appendix A: ISharpDatabase Method Count by Category

| Category | Methods | Graph-Dependent |
|----------|---------|-----------------|
| Object CRUD | 8 | Low |
| Attribute Operations | 16 | **High** (inheritance traversals) |
| Flag/Power Management | 14 | Medium (edge operations) |
| Location/Navigation | 12 | **High** (location graph, exits) |
| Mail System | 12 | Medium (sender/recipient graph) |
| Channel Management | 10 | Medium (membership graph) |
| Player Management | 4 | Low |
| Expanded Data | 4 | Low |
| Migration | 1 | N/A |
| **Total** | **~81** | **~40% graph-dependent** |

## Appendix B: ArangoDB-Specific AQL Features Used

| AQL Feature | Usage Count | Replacement Difficulty |
|-------------|-------------|----------------------|
| `FOR v,e,p IN 1..N GRAPH` | 40+ | Varies by target |
| `PRUNE condition` | 6 | High (unique to ArangoDB) |
| `ALL_SHORTEST_PATH` | 2 | Medium (most graph DBs have equivalent) |
| `LET ... subquery` | 30+ | Medium (most languages support) |
| `DOCUMENT()` function | 5 | Low (direct lookup) |
| `FIRST()` / `NTH()` / `LENGTH()` | 15+ | Low (standard functions) |
| `REGEX_TEST()` | 3 | Low (regex support is common) |
| `PARSE_IDENTIFIER()` | 2 | Low (string manipulation) |
| `APPEND()` / `DISTINCT()` | 5 | Low (array operations) |
| Transaction with exclusive locks | 5 | Medium (varies by target) |

## Appendix C: Existing Analysis References

- **SurrealDB analysis** was previously performed (referenced in repository memories) and concluded SurrealDB can replace ArangoDB with embedded mode (SurrealKv), but needs custom migration framework, auto-increment ID simulation, and PRUNE-equivalent rewrites.
- **NATS JetStream** has already replaced Kafka/Redis for messaging (separate from database concerns).
- The `ISharpDatabase` interface provides a clean abstraction boundary — any replacement only needs to implement this interface without affecting the rest of the codebase.
