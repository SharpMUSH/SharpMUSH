# ArangoDB Alternatives Analysis for SharpMUSH

## Executive Summary

SharpMUSH currently uses ArangoDB as its sole database via the `ISharpDatabase` interface (75+ methods) implemented by `ArangoDatabase` (3,300+ lines). The database serves as a multi-model store combining document, graph, and key-value capabilities. This analysis evaluates alternatives across embedded and standalone deployment modes, assessing each against SharpMUSH's specific requirements.

**Recommendation**: SurrealDB is the strongest overall candidate (embedded via SurrealKv, native graph support, multi-model). Neo4j is the strongest graph-native alternative. RavenDB offers the best .NET developer experience but has weaker graph support. PostgreSQL + Marten is the most pragmatic but requires the most custom graph logic.

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
4. [Feature Comparison Matrix](#4-feature-comparison-matrix)
5. [Migration Complexity Assessment](#5-migration-complexity-assessment)
6. [Recommendation Summary](#6-recommendation-summary)

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

1. **Embedded Mode (SurrealKv)**: First-class embedded mode with file-based storage
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
2. **.NET SDK maturity**: v0.9.0; no client-side transaction API (`BeginTransaction` absent from .NET SDK, present in JS SDK)
3. **No auto-increment keys**: Would need a counter table or `ULID`/`UUID` approach. Custom auto-increment simulation required
4. **BSL License**: Not fully open-source for 4 years (but allows usage)
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
4. **Apache AGE extension**: Full graph query support (Cypher-like) within PostgreSQL
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
7. **No regex in queries**: Limited pattern matching
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
| R13 Regex matching | ⚠️ Limited | Basic pattern only |
| R14 Document CRUD | ✅ Yes | Core feature |
| R15 Active community | ⚠️ Declining | v5/v6 transition uncertain |
| R16 License | ✅ MIT | Most permissive |

**Migration effort**: EXTREME — Every graph operation (traversals, inheritance, shortest path, reachability) must be implemented in application code. Would essentially require writing a graph engine on top of LiteDB. Not recommended.

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

---

## 4. Feature Comparison Matrix

| Feature | ArangoDB (Current) | SurrealDB | Neo4j | RavenDB | PostgreSQL+Marten | ArcadeDB | LiteDB |
|---------|-------------------|-----------|-------|---------|-------------------|----------|--------|
| **Graph Traversals** | ✅ Excellent | ✅ Good | ✅ Excellent | ⚠️ Basic | ⚠️ Via CTEs | ✅ Good | ❌ None |
| **PRUNE (conditional)** | ✅ Native | ❌ No | ✅ Via patterns | ❌ No | ❌ No | ❌ No | ❌ No |
| **Shortest Path** | ✅ Native | ✅ v2.2+ | ✅ Native | ✅ recursive | ⚠️ Extension | ✅ Via Gremlin | ❌ No |
| **Multi-edge Traversal** | ✅ Native | ✅ Yes | ✅ Native | ❌ No | ⚠️ JOINs | ✅ Yes | ❌ No |
| **ACID Transactions** | ✅ Yes | ⚠️ Server only | ✅ Yes | ✅ Excellent | ✅ Excellent | ✅ Yes | ⚠️ Limited |
| **.NET SDK Quality** | ✅ Good | ⚠️ Maturing | ✅ Good | ✅ Excellent | ✅ Excellent | ❌ None | ✅ Excellent |
| **Embedded Mode** | ❌ No | ✅ SurrealKv | ⚠️ JVM only | ✅ Native .NET | ❌ No | ⚠️ JVM only | ✅ Native .NET |
| **Auto-increment Keys** | ✅ Native | ❌ Simulate | ⚠️ Via APOC | ✅ HiLo | ✅ SERIAL | ✅ Sequences | ✅ Auto-ID |
| **Schema Validation** | ✅ JSON Schema | ✅ DEFINE FIELD | ⚠️ Constraints | ❌ No | ✅ CHECK | ✅ Types | ❌ No |
| **Migration Framework** | ✅ Core.Arango | ❌ Custom | ⚠️ Third-party | ✅ Raven.Migrations | ✅ Many options | ❌ Custom | ❌ Custom |
| **Document CRUD** | ✅ Native | ✅ Native | ⚠️ Properties | ✅ Native | ✅ Via Marten | ✅ Native | ✅ Native |
| **Cross-graph Joins** | ✅ Multi-GRAPH | ✅ Subqueries | ✅ Multi-MATCH | ❌ No | ⚠️ SQL JOINs | ✅ Multi-MATCH | ❌ No |
| **Regex in Queries** | ✅ REGEX_TEST | ✅ string::is::match | ✅ `=~` | ✅ Via indexes | ✅ `~` operator | ✅ Yes | ⚠️ Limited |
| **License** | Apache 2.0 | BSL 1.1 | GPL v3 | AGPL v3 | PostgreSQL+MIT | Apache 2.0 | MIT |
| **Standalone Mode** | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Yes | ❌ No |
| **Streaming Cursors** | ✅ Yes | ✅ Yes | ⚠️ Reactive | ✅ Yes | ✅ Yes | ⚠️ Pagination | ❌ No |

---

## 5. Migration Complexity Assessment

### 5.1 Effort Estimates

| Alternative | Overall Effort | Graph Rewrite | SDK Integration | Data Migration | Schema/Migration |
|-------------|---------------|---------------|-----------------|----------------|------------------|
| **SurrealDB** | 🟡 HIGH | Major (PRUNE) | Medium (SDK gaps) | Low (model fits) | Medium (custom) |
| **Neo4j** | 🟡 MEDIUM-HIGH | Medium (Cypher capable) | Low (good SDK) | Medium (remodel) | Medium (third-party) |
| **RavenDB** | 🔴 VERY HIGH | Extreme (no graph) | Low (best SDK) | High (flatten graphs) | Low (migration NuGet) |
| **PostgreSQL** | 🔴 VERY HIGH | Extreme (CTEs/AGE) | Low (Marten) | High (relational) | Low (many options) |
| **ArcadeDB** | 🔴 HIGH | Medium (model fits) | Extreme (no SDK) | Low (model fits) | High (custom) |
| **LiteDB** | 🔴 EXTREME | Extreme (no graph) | Low (native .NET) | High (flatten) | High (custom) |

### 5.2 Critical Migration Challenges

#### Challenge 1: Attribute Inheritance Queries (ALL alternatives)
The 70-line AQL query for `GetAttributeWithInheritanceAsync` is the hardest to migrate. It requires:
- Nested graph traversal (Self → Parent → Zone)
- Conditional branch pruning (PRUNE)
- Cross-graph joins (Objects + Parents + Zones + Attributes)
- Lazy evaluation support

**SurrealDB**: Would need 3 separate queries composed in application code, losing single-roundtrip efficiency.
**Neo4j**: Can express this in Cypher using `UNION` + variable-length patterns, but requires careful optimization.
**Others**: Must implement entirely in application code.

#### Challenge 2: PRUNE Semantics
PRUNE is unique to ArangoDB. It stops traversal down a branch when a condition is met, reducing work. Without it:
- Application must fetch more data and filter client-side
- Or use multiple smaller queries
- Performance impact depends on graph depth/breadth

#### Challenge 3: Named Graphs
ArangoDB's named graphs define valid edge-vertex relationships at the schema level. Alternatives handle this differently:
- **SurrealDB**: Table-based relations (similar concept)
- **Neo4j**: Relationship types (no schema constraint on endpoints)
- **RavenDB**: Document references (no graph schema)
- **PostgreSQL**: Foreign keys (rigid) or AGE graph (flexible)

---

## 6. Recommendation Summary

### Tier 1: Viable Alternatives (Recommended for Evaluation)

#### 🥇 SurrealDB — Best Overall Fit
- **Why**: Only alternative with embedded mode + native graph + multi-model + active development
- **Key risk**: .NET SDK maturity (no client-side transactions), PRUNE equivalent missing
- **Best for**: If embedded mode is a high priority and you can accept application-level inheritance logic
- **License concern**: BSL 1.1 (usage is allowed; source-available but not fully open for 4 years)
- **Estimated migration**: 4-6 weeks for a senior developer

#### 🥈 Neo4j — Best Graph Capabilities
- **Why**: Most capable graph query language (Cypher), can express all SharpMUSH patterns
- **Key risk**: GPL license, JVM-based embedded mode, document model impedance
- **Best for**: If graph query expressiveness is the top priority and licensing is acceptable
- **Estimated migration**: 3-5 weeks for a senior developer (Cypher can express the complex queries)

### Tier 2: Possible but Significant Trade-offs

#### 🥉 RavenDB — Best .NET Experience
- **Why**: Best-in-class .NET embedded experience, excellent SDK, mature ecosystem
- **Key risk**: Graph support is insufficient for SharpMUSH's needs; would require application-level graph engine
- **Best for**: If you're willing to rewrite graph operations in C# and want the best developer experience
- **Estimated migration**: 6-10 weeks (graph engine construction)

#### PostgreSQL + Marten — Most Pragmatic Foundation
- **Why**: Rock-solid foundation, permissive license, universal hosting
- **Key risk**: No native graph model; recursive CTEs are verbose and slower for deep traversals
- **Best for**: If long-term stability and ecosystem breadth outweigh graph query elegance
- **Estimated migration**: 6-10 weeks (graph layer construction)

### Tier 3: Not Recommended

| Alternative | Why Not |
|-------------|---------|
| **ArcadeDB** | No .NET SDK; must build from scratch |
| **LiteDB** | No graph support; too limited for SharpMUSH |
| **DGraph** | No .NET SDK; RDF model is wrong paradigm; operational complexity |

### Decision Framework

```
If embedded mode is critical:
  → SurrealDB (SurrealKv) or RavenDB (RavenDB.Embedded)

If graph query power is critical:
  → Neo4j (Cypher) or stay with ArangoDB (AQL)

If license must be permissive:
  → SurrealDB (BSL→Apache), PostgreSQL (PostgreSQL+MIT), or ArcadeDB (Apache 2.0)

If .NET developer experience is critical:
  → RavenDB > PostgreSQL+Marten > SurrealDB > Neo4j

If migration effort must be minimal:
  → Stay with ArangoDB (or Neo4j as closest graph alternative)
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
