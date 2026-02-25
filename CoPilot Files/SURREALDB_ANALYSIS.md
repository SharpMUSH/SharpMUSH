# SurrealDB vs ArangoDB: In-Depth Analysis for SharpMUSH

## Executive Summary

This document provides a comprehensive, method-by-method analysis of whether SurrealDB (via the official .NET SDK `SurrealDb.Net`) can replace ArangoDB as the database backend for SharpMUSH. The analysis covers every capability currently used in the ArangoDB implementation (`ArangoDatabase.cs`, 3355 lines) against what SurrealDB's .NET SDK and SurrealQL language offer.

**Key Finding**: SurrealDB is a viable candidate with significant advantages (embedded mode, native graph model, simpler schema) but has notable gaps in the .NET SDK — particularly the lack of native `IAsyncEnumerable` streaming, the absence of a migration framework, and no client-side transaction API. The graph model is different (RELATE-based vs named graph definitions) but is functionally equivalent for SharpMUSH's use cases.

**SurrealDB v3 Status** (as of Feb 25, 2026): SurrealDB server v3.0.0 was released Feb 17, 2026. The .NET SDK added v3 compatibility ([PR #222](https://github.com/surrealdb/surrealdb.net/pull/222), merged Feb 23, 2026) but no new NuGet release has been published (latest is v0.9.0). The JavaScript SDK supports client-side transactions via `db.beginTransaction()`, but the .NET SDK does **not** yet implement this — transactions remain query-level only via `RawQuery()`.

---

## 1. Deployment Model Comparison

### ArangoDB (Current)
- **Deployment**: External server process (Docker container via `docker-compose.yml`)
- **Protocol**: HTTP REST API via `Core.Arango` NuGet package
- **Dependency**: Requires ArangoDB server running separately
- **Embedding**: Not possible — ArangoDB is a standalone server only

### SurrealDB
- **Deployment Options**:
  - **External server**: HTTP/WebSocket (`SurrealDb.Net` package)
  - **Embedded file-backed**: `SurrealDb.Embedded.SurrealKv` — persists to a local file (`data.db`)
  - **Embedded in-memory**: `SurrealDb.Embedded.InMemory` — no persistence, ideal for testing
- **Protocol**: HTTP, WebSocket, or native Rust FFI (embedded)
- **Dependency**: Embedded mode eliminates the need for a separate server process
- **.NET 10 Support**: Yes, the SDK targets .NET 8, 9, 10 and .NET Standard 2.1
- **SurrealDB v3 Support**: SurrealDB v3.0.0 was released Feb 17, 2026. The .NET SDK added v3 compatibility via [PR #222](https://github.com/surrealdb/surrealdb.net/pull/222) (merged Feb 23, 2026), but no new NuGet release has been published yet (latest is v0.9.0 targeting SurrealDB v2.x)

**Verdict**: ✅ **SurrealDB wins decisively** — embedded mode (`SurrealKv`) eliminates Docker/server dependency entirely, which is a major architectural simplification for SharpMUSH.

---

## 2. Connection & Initialization

### ArangoDB
```csharp
// Constructor injection
public ArangoDatabase(ILogger<ArangoDatabase> logger, IArangoContext arangoDb, 
    ArangoHandle handle, IMediator mediator, IPasswordService passwordService)

// Migration
var migrator = new ArangoMigrator(arangoDb) { HistoryCollection = "MigrationHistory" };
migrator.AddMigrations(typeof(ArangoDatabase).Assembly);
await migrator.UpgradeAsync(handle);
```

### SurrealDB
```csharp
// Embedded file-backed
services.AddSurreal("Endpoint=surrealkv://data.db;Namespace=sharpmush;Database=main")
    .AddSurrealKvProvider();

// Or construct directly
using var db = new SurrealDbKvClient("data.db");
await db.Use("sharpmush", "main");
```

**Key Differences**:
| Feature | ArangoDB | SurrealDB |
|---------|----------|-----------|
| DI Registration | Custom via `IArangoContext` | Built-in `AddSurreal()` |
| Namespace/DB | Single database per handle | Multi-tenant: Namespace → Database hierarchy |
| Connection String | Custom configuration | Standard connection string format |

**Verdict**: ✅ **SurrealDB** — cleaner DI integration, native connection string support, multi-tenant namespace model.

---

## 3. Schema & Migration System

### ArangoDB (Current)
- **Framework**: `Core.Arango.Migration` with `IArangoMigration` interface
- **Pattern**: Up-only migrations with `Id` (e.g., `20240304_001`) and `Name`
- **Features**: `ApplyStructureAsync()` for atomic collection/index/graph creation
- **History**: Tracked in `MigrationHistory` collection
- **Schema**: JSON Schema validation on collections (e.g., `Objects` has schema rules for Name, Type)
- **Auto-increment keys**: `Objects` collection uses `ArangoKeyType.Autoincrement` starting from 0

### SurrealDB
- **Framework**: ❌ **No built-in migration framework** in the .NET SDK
- **Schema Definition**: Via `RawQuery()` executing SurrealQL `DEFINE` statements
  ```sql
  DEFINE TABLE node_objects SCHEMAFULL;
  DEFINE FIELD Name ON TABLE node_objects TYPE string;
  DEFINE FIELD Type ON TABLE node_objects TYPE string;
  DEFINE FIELD CreationTime ON TABLE node_objects TYPE number;
  DEFINE FIELD ModifiedTime ON TABLE node_objects TYPE number;
  DEFINE FIELD Locks ON TABLE node_objects TYPE option<object>;
  ```
- **Schema Modes**: `SCHEMAFULL` (strict) or `SCHEMALESS` (flexible)
- **Field Validation**: `ASSERT` clauses, `VALUE` computed fields, `DEFAULT` values
- **Record IDs**: String-based (`table:key`), no built-in auto-increment; would need `RawQuery("CREATE node_objects SET ...")` with counter management or ULID/UUID IDs
- **Import/Export**: `Export()` and `Import()` methods for backup/restore

**Migration Gap Analysis**:
A custom migration framework would need to be built for SurrealDB. This could be modeled after the ArangoDB pattern:
```csharp
// Would need: custom migration runner
public interface ISurrealMigration {
    string Id { get; }
    string Name { get; }
    Task Up(ISurrealDbClient client);
}
```

**Auto-Increment Key Impact**:
SharpMUSH's `DBRef` system uses sequential integer keys (0, 1, 2...). ArangoDB supports this natively via `ArangoKeyType.Autoincrement`. SurrealDB does **not** have built-in auto-increment IDs. Options:
1. Use a counter document + transaction to simulate auto-increment
2. Use SurrealQL `fn::next_id()` custom function
3. Maintain a sequence via `RawQuery("UPDATE counter:objects SET val += 1 RETURN AFTER")`

**Verdict**: ⚠️ **ArangoDB wins on migrations** — has a mature migration framework. SurrealDB would require building custom migration infrastructure. Auto-increment IDs need a workaround.

---

## 4. Document CRUD Operations

### 4.1 Create Operations

#### ArangoDB
```csharp
// Typed create with response
var obj = await arangoDb.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(
    handle, DatabaseConstants.Objects, new SharpObjectCreateRequest(name, type, locks, time, time), 
    returnNew: true, cancellationToken: ct);

// Simple create
await arangoDb.Document.CreateAsync(handle, DatabaseConstants.Rooms, 
    new SharpRoomCreateRequest(), cancellationToken: ct);

// Upsert
await arangoDb.Document.CreateAsync(handle, DatabaseConstants.ServerData, data, 
    overwriteMode: ArangoOverwriteMode.Update, cancellationToken: ct);
```

#### SurrealDB .NET SDK
```csharp
// Create with table name
var obj = await client.Create("node_objects", new SharpObjectCreateRequest(...), ct);

// Create with specific record ID
var obj = await client.Create<TData, TOutput>(
    new StringRecordId("node_objects:123"), data, ct);

// Upsert
var obj = await client.Upsert(data, ct);  // uses record's Id
```

**ISurrealDbClient Methods Available**:
| Method | Signature | Notes |
|--------|-----------|-------|
| `Create<T>(T data)` | Creates record using data's Id | Requires `IRecord` |
| `Create<T>(string table, T? data)` | Creates in named table | Auto-generates Id |
| `Create<TData, TOutput>(StringRecordId, TData?)` | Creates at specific record ID | Allows typed output |
| `Upsert<T>(T data)` | Creates or replaces | Requires `IRecord` |
| `Upsert<T>(string table, T data)` | Upserts all matching | Returns `IEnumerable<T>` |

**Verdict**: ✅ **Equivalent** — both support typed creation, upsert, and return of created documents.

### 4.2 Read Operations

#### ArangoDB
```csharp
// Get single document by key
var doc = await arangoDb.Document.GetAsync<T>(handle, collection, key, ct);

// Execute query returning list
var results = await arangoDb.Query.ExecuteAsync<T>(handle, aqlQuery, bindVars, ct);

// Stream results as IAsyncEnumerable
var stream = arangoDb.Query.ExecuteStreamAsync<T>(handle, aqlQuery, ct);
```

#### SurrealDB .NET SDK
```csharp
// Get all records from table
IEnumerable<T> all = await client.Select<T>("table", ct);

// Get single record by ID
T? record = await client.Select<T>(new RecordId("table", "key"), ct);
T? record = await client.Select<T>(new StringRecordId("table:key"), ct);

// Range query
IEnumerable<T> range = await client.Select<TStart, TEnd, T>(
    new RecordIdRange<TStart, TEnd>(...), ct);

// Raw query (arbitrary SurrealQL)
SurrealDbResponse response = await client.RawQuery(surqlQuery, parameters, ct);
T result = response.GetValue<T>(0);  // Get typed result from response index
```

**Key Difference — Streaming**:
ArangoDB's `ExecuteStreamAsync<T>()` returns `IAsyncEnumerable<T>` which is used extensively throughout SharpMUSH (30+ methods). SurrealDB's .NET SDK returns `Task<IEnumerable<T>>` — all results are loaded into memory at once.

This is a significant concern for methods like:
- `GetAllObjectsAsync()` — iterates all objects
- `GetFilteredObjectsAsync()` — could return large result sets
- `GetNearbyObjectsAsync()` — graph traversal results
- All mail/channel listing methods

**Workaround**: Use `RawQuery()` with pagination (`LIMIT offset, count`) to batch results, then yield them as `IAsyncEnumerable` in a wrapper.

**Verdict**: ⚠️ **ArangoDB has better streaming** — `IAsyncEnumerable` support is native. SurrealDB loads all results into memory. This requires careful workaround design.

### 4.3 Update Operations

#### ArangoDB
```csharp
// Partial update (merge)
await arangoDb.Document.UpdateAsync(handle, collection, new { _key = key, Field = value }, 
    mergeObjects: true, keepNull: false, ct);

// Batch update
await arangoDb.Document.UpdateManyAsync(handle, collection, updates, ct);

// Graph vertex update
await arangoDb.Graph.Vertex.UpdateAsync(handle, graphName, collection, key, updateData, ct);
```

#### SurrealDB .NET SDK
```csharp
// Full replace
T updated = await client.Update(data, ct);  // IRecord required

// Partial merge (named fields)
T merged = await client.Merge<TMerge, TOutput>(data, ct);
T merged = await client.Merge<T>(recordId, new Dictionary<string, object> { ... }, ct);

// JSON Patch (RFC 6902)
T patched = await client.Patch(recordId, jsonPatchDocument, ct);

// Batch update via table
IEnumerable<T> all = await client.Update("table", data, ct);

// Via raw query for complex updates
await client.RawQuery("UPDATE node_objects SET Name = $name WHERE _key = $key", params, ct);
```

**ISurrealDbClient Methods Available**:
| Method | Notes |
|--------|-------|
| `Update<T>(T data)` | Full replace, requires `IRecord` |
| `Update<TData, TOutput>(RecordId, TData)` | Replace specific record |
| `Update<T>(string table, T data)` | Replace all in table |
| `Merge<TMerge, TOutput>(TMerge data)` | Partial update (like ArangoDB's mergeObjects) |
| `Merge<T>(RecordId, Dictionary)` | Partial update with field dict |
| `Patch<T>(RecordId, JsonPatchDocument)` | RFC 6902 JSON Patch |
| `PatchAll<T>(string table, JsonPatchDocument)` | Patch all in table |

**Verdict**: ✅ **SurrealDB has richer update semantics** — JSON Patch, Merge, and full Replace. ArangoDB's `mergeObjects` maps to SurrealDB's `Merge`.

### 4.4 Delete Operations

#### ArangoDB
```csharp
await arangoDb.Document.DeleteAsync<T>(handle, collection, key, ct);
await arangoDb.Graph.Vertex.RemoveAsync(handle, graphName, collection, key, ct);
await arangoDb.Graph.Edge.RemoveAsync<T>(handle, graphName, edgeCollection, edgeKey, ct);
```

#### SurrealDB .NET SDK
```csharp
// Delete by record ID
bool success = await client.Delete(new RecordId("table", "key"), ct);
bool success = await client.Delete(new StringRecordId("table:key"), ct);

// Delete all in table
await client.Delete("table", ct);

// Via raw query for edge deletion
await client.RawQuery("DELETE wrote WHERE in = $from AND out = $to", params, ct);
```

**Verdict**: ✅ **Equivalent** — both support single and bulk deletes. Edge deletion requires `RawQuery` in SurrealDB.

---

## 5. Graph Operations — The Critical Comparison

This is the most significant area of difference and the heart of SharpMUSH's data model.

### 5.1 Graph Model Architecture

#### ArangoDB
- **Named Graphs**: 14 explicitly defined graphs with vertex/edge collection bindings
- **Edge Collections**: 17 separate edge collections (e.g., `edge_is_object`, `edge_at_location`)
- **Document Collections**: 12+ vertex collections (e.g., `node_objects`, `node_players`)
- **Graph Definition**: Declared in migration with explicit vertex/edge collection lists
- **Traversal**: AQL `FOR v, e, p IN depth..depth OUTBOUND/INBOUND vertex GRAPH graphName`

#### SurrealDB
- **Record Links**: `record<table>` field types create direct references between records
- **Relation Tables**: Created via `RELATE` statement — relation tables are regular tables with `in` and `out` fields
- **No Named Graphs**: Relationships are implicit via RELATE tables, not explicit graph definitions
- **Traversal**: Arrow syntax `->relation->target` and `<-relation<-source`
- **Recursive Traversal**: `@.{depth}->relation->target` for variable depth (v2.1.0+)

### 5.2 Edge/Relation Creation

#### ArangoDB
```csharp
// Create edge in named graph
await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphObjects, 
    DatabaseConstants.IsObject, new SharpEdgeCreateRequest(fromId, toId), ct);
```

#### SurrealDB .NET SDK
```csharp
// Using typed Relate method
IEnumerable<TOutput> result = await client.Relate<TOutput, TData>(
    "edge_is_object",           // relation table name
    new[] { new RecordId("node_players", playerId) },  // from records
    new[] { new RecordId("node_objects", objectId) },   // to records
    data,                        // optional edge data
    ct);

// Using specific record ID for the relation
TOutput result = await client.Relate<TOutput, TData>(
    new RecordId("edge_is_object", relationKey),  // specific relation record
    new RecordId("node_players", playerId),       // from
    new RecordId("node_objects", objectId),        // to
    data, ct);

// Using InsertRelation (for IRelationRecord types)
T result = await client.InsertRelation<T>(data, ct);
T result = await client.InsertRelation<T>("edge_is_object", data, ct);

// Via raw query
await client.RawQuery(
    "RELATE $from->edge_is_object->$to", 
    new Dictionary<string, object?> { 
        { "from", new RecordId("node_players", playerId) },
        { "to", new RecordId("node_objects", objectId) }
    }, ct);
```

**ISurrealDbClient Relation Methods**:
| Method | Signature | Notes |
|--------|-----------|-------|
| `Relate<TOutput, TData>(string table, IEnumerable<RecordId> ins, IEnumerable<RecordId> outs, TData? data)` | Batch relate | Multiple from→to pairs |
| `Relate<TOutput, TData>(RecordId recordId, RecordId in, RecordId out, TData? data)` | Single relate | Specific relation ID |
| `InsertRelation<T>(T data)` | Insert typed relation | `IRelationRecord` required |
| `InsertRelation<T>(string table, T data)` | Insert to specific table | `IRelationRecord` required |

**Verdict**: ✅ **Equivalent** — SurrealDB's `Relate` and `InsertRelation` methods map directly to ArangoDB's `Graph.Edge.CreateAsync`. SurrealDB additionally supports batch creation.

### 5.3 Edge/Relation Update

#### ArangoDB
```csharp
// Update edge by key
await arangoDb.Graph.Edge.UpdateAsync(handle, graphName, edgeCollection, 
    edgeKey, new { To = newTarget }, ct);
```

#### SurrealDB .NET SDK
```csharp
// Update relation via RawQuery (no dedicated SDK method)
await client.RawQuery(
    "UPDATE edge_at_location SET out = $newTarget WHERE in = $source", 
    new Dictionary<string, object?> { 
        { "source", sourceId }, 
        { "newTarget", targetId } 
    }, ct);

// Or update the relation record directly
await client.Update(new EdgeRecord { Id = recordId, In = fromId, Out = newToId }, ct);
```

**Verdict**: ⚠️ **ArangoDB is more ergonomic** — has a dedicated `Graph.Edge.UpdateAsync`. SurrealDB requires `RawQuery` or `Update` on the relation record.

### 5.4 Edge/Relation Deletion

#### ArangoDB
```csharp
// Remove by edge key
await arangoDb.Graph.Edge.RemoveAsync<T>(handle, graphName, edgeCollection, edgeKey, ct);

// Query edge first, then remove
var edges = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
    $"FOR v, e IN 1..1 OUTBOUND {id} GRAPH {graphName} RETURN e", ct);
await arangoDb.Graph.Edge.RemoveAsync<T>(handle, graphName, edgeCollection, edges.First().Key, ct);
```

#### SurrealDB .NET SDK
```csharp
// Delete by relation record ID
await client.Delete(new RecordId("edge_at_location", key), ct);

// Query and delete via RawQuery
await client.RawQuery(
    "DELETE edge_at_location WHERE in = $source AND out = $target",
    params, ct);
```

**Verdict**: ✅ **Equivalent** — both require a query-then-delete pattern for relationship removal.

### 5.5 Graph Traversal

This is the core differentiator. ArangoDB uses AQL with explicit graph traversal syntax. SurrealDB uses arrow operators and recursive paths.

#### ArangoDB Traversal Patterns Used in SharpMUSH

| Pattern | AQL | SharpMUSH Method |
|---------|-----|-----------------|
| **1-hop OUTBOUND** | `FOR v IN 1..1 OUTBOUND @start GRAPH g` | `GetLocationAsync`, `GetHomeAsync`, `GetDropToAsync` |
| **1-hop INBOUND** | `FOR v IN 1..1 INBOUND @start GRAPH g` | `GetContentsAsync`, `GetExitsAsync`, `GetEntrancesAsync` |
| **Variable depth OUTBOUND** | `FOR v IN 1..999 OUTBOUND @start GRAPH g` | `GetParentsAsync`, `GetAllAttributesAsync` |
| **PRUNE condition** | `PRUNE cond = NTH(@attr, LENGTH(p.edges)-1) != v.Name` | `GetAttributeAsync`, `GetAttributeWithInheritanceAsync` |
| **Multi-edge traversal** | `FOR v IN 1..@d OUTBOUND @s edge1, edge2` | `IsReachableViaParentOrZoneAsync` |
| **BFS with unique vertices** | `OPTIONS {uniqueVertices: 'global', order: 'bfs'}` | `IsReachableViaParentOrZoneAsync` |
| **ALL_SHORTEST_PATH** | `FOR path IN 1..1 INBOUND ALL_SHORTEST_PATH a TO b GRAPH g` | `GetSentMailsAsync` |

#### SurrealDB Equivalents

| ArangoDB Pattern | SurrealQL Equivalent | Notes |
|------------------|---------------------|-------|
| **1-hop OUTBOUND** | `SELECT ->edge_at_location->node_rooms FROM $start` or `$start->edge_at_location->node_rooms` | Direct arrow syntax |
| **1-hop INBOUND** | `SELECT <-edge_at_location<-node_players FROM $start` | Reverse arrow syntax |
| **Variable depth** | `@.{1..999}->edge_has_parent->node_objects` | Recursive path syntax (v2.1.0+) |
| **PRUNE condition** | ❌ **No direct equivalent** | Must use recursive queries with conditional logic or multi-step queries |
| **Multi-edge traversal** | `$start.{1..100}(->edge_has_parent->node_objects, ->edge_has_zone->node_objects)` | Multiple edge types in recursive path |
| **BFS with dedup** | ✅ `$start.{..+collect}->edge->target` | `+collect` collects all unique nodes walked (BFS-like order, closest first) — available since v2.2.0 |
| **ALL_SHORTEST_PATH** | ✅ `$start.{..+shortest=record:id}->edge->target` | `+shortest=record:id` finds shortest path to target — available since v2.2.0 |
| **All paths enumeration** | ✅ `$start.{..+path}->edge->target` | `+path` collects all walked paths as array of arrays — available since v2.2.0 |

**Critical Gap — PRUNE**:
ArangoDB's `PRUNE` is essential for attribute tree traversal. It allows early termination of branches that don't match the attribute path. The `GetAttributeAsync` and `GetAttributeWithInheritanceAsync` methods (the most complex queries in the codebase) rely heavily on this.

In SurrealDB, the equivalent would be a multi-step query or a stored procedure:
```sql
-- Step 1: Find the first-level attribute matching attr[0]
LET $level1 = (SELECT * FROM edge_has_attribute WHERE in = $start AND out.Name = $attr[0]);
-- Step 2: Find second level matching attr[1]  
LET $level2 = (SELECT * FROM edge_has_attribute WHERE in = $level1.out AND out.Name = $attr[1]);
-- etc.
```
Or use recursive paths with filtering:
```sql
$start.{1..5}(->edge_has_attribute->node_attributes WHERE Name = $currentLevel)
```

**Critical Gap — ALL_SHORTEST_PATH** *(CORRECTED — no longer a gap)*:
SurrealDB v2.2.0+ supports shortest path via `{..+shortest=record:id}` recursive path syntax. The ArangoDB `ALL_SHORTEST_PATH` used in mail queries (`GetSentMailsAsync`, `GetSentMailAsync`) can be directly replaced:
```sql
-- SurrealDB shortest path equivalent
$recipient.{..+shortest=$sender}->edge_mail_sender->node_objects;
```
Additionally, `{..+collect}` provides BFS-like unique node collection, and `{..+path}` enumerates all paths — both available since v2.2.0.

**Verdict**: ⚠️ **ArangoDB has more powerful graph traversal in some areas** — PRUNE has no direct SurrealDB equivalent. However, SurrealDB v2.2.0+ **does** support shortest path (`+shortest`), unique node collection/BFS-like traversal (`+collect`), and all-path enumeration (`+path`). Most SharpMUSH traversals are simple 1-hop operations that map cleanly. The complex attribute inheritance queries (PRUNE-based) would need significant rewriting.

---

## 6. Transaction Support

### ArangoDB
```csharp
// Begin transaction with explicit collection locks
var transaction = new ArangoTransaction {
    LockTimeout = 9999,
    WaitForSync = true,
    Collections = new ArangoTransactionScope {
        Exclusive = ["node_objects", "node_players", "edge_is_object", ...],
        Read = ["node_object_flags"]
    }
};
var handle = await arangoDb.Transaction.BeginAsync(handle, transaction, ct);

// Execute operations within transaction
await arangoDb.Document.CreateAsync(handle, ...);
await arangoDb.Graph.Edge.CreateAsync(handle, ...);

// Commit
await arangoDb.Transaction.CommitAsync(handle, ct);
```

**SharpMUSH methods using transactions**:
- `CreatePlayerAsync` — 6 collection locks
- `CreateThingAsync` — 6 collection locks
- `CreateExitAsync` — 5 collection locks
- `SetAttributeAsync` — 7 collection locks (most complex)
- `SendMailAsync` — 3 collection locks
- `CreateChannelAsync` — 3 collection locks
- `SetExpandedServerData` — 1 collection lock (AllowImplicit: false)

### SurrealDB
```sql
-- Via SurrealQL (executed through RawQuery)
BEGIN TRANSACTION;
CREATE node_objects:123 SET Name = 'Test', Type = 'PLAYER';
CREATE node_players:456 SET PasswordHash = '...';
RELATE node_players:456->edge_is_object->node_objects:123;
COMMIT TRANSACTION;
```

```csharp
// Via .NET SDK RawQuery (all statements in one call)
var response = await client.RawQuery(@"
    BEGIN TRANSACTION;
    LET $obj = CREATE node_objects SET Name = $name, Type = $type;
    LET $player = CREATE node_players SET PasswordHash = $hash;
    RELATE $player->edge_is_object->$obj;
    COMMIT TRANSACTION;
", parameters, ct);
```

> **Note on client-side transactions**: The SurrealDB JavaScript SDK (`surrealdb.js`) introduced `db.beginTransaction()` which returns a transaction object supporting `tx.create()`, `tx.update()`, `tx.commit()`, and `tx.cancel()` — true client-side API-level transactions. However, the .NET SDK (`SurrealDb.Net`) **does not yet implement this pattern**. The `ISurrealDbClient` interface has no `BeginTransaction()` method as of v0.9.0 (Feb 2026). Transactions in the .NET SDK remain query-level only via `RawQuery()`.

**Key Differences**:
| Feature | ArangoDB | SurrealDB |
|---------|----------|-----------|
| **API-level transactions** | ✅ `BeginAsync`/`CommitAsync` with separate operations | ❌ Not in .NET SDK (⚠️ exists in JS SDK as `beginTransaction()`) |
| **Query-level transactions** | ✅ AQL within transaction handles | ✅ `BEGIN`/`COMMIT` in SurrealQL |
| **Collection-level locks** | ✅ Explicit Exclusive/Read locks | ❌ Automatic MVCC, no explicit locks |
| **Timeout control** | ✅ `LockTimeout` parameter | ❌ Not configurable |
| **WaitForSync** | ✅ Per-transaction control | ❌ Not configurable per-transaction |
| **Intermediate results** | ✅ Can read results between operations | ⚠️ Only within `LET` bindings in query |

**Transaction Impact**:
SurrealDB's transaction model in the .NET SDK is **query-level only** — all statements must be submitted as a single `RawQuery` call. This means complex transactional operations like `CreatePlayerAsync` (which creates objects, edges, and players in separate API calls within a transaction) must be rewritten as a single SurrealQL query string.

This is a significant architectural difference but not a blocker — the query can be composed as a SurrealQL string with `LET` bindings to capture intermediate results. If the .NET SDK adds `BeginTransaction()` support in the future (mirroring the JS SDK), this limitation would be resolved.

**Verdict**: ⚠️ **ArangoDB has more flexible transactions** — API-level Begin/Commit allows mixing SDK calls with transaction context. SurrealDB's .NET SDK requires bundling all operations into a single query string. The JS SDK's `beginTransaction()` shows that client-side transactions are architecturally supported by SurrealDB, but this feature has not yet been ported to the .NET SDK.

---

## 7. Query Language Comparison

### AQL (ArangoDB Query Language) vs SurrealQL

#### Simple Queries

| Operation | AQL | SurrealQL |
|-----------|-----|-----------|
| Get by key | `RETURN DOCUMENT('node_objects/123')` | `SELECT * FROM node_objects:123` |
| Filter by name | `FOR v IN node_objects FILTER v.Name == @name RETURN v` | `SELECT * FROM node_objects WHERE Name = $name` |
| Case-insensitive | `FILTER UPPER(v.Name) == UPPER(@name)` | `WHERE string::lowercase(Name) = string::lowercase($name)` |
| Regex filter | `FILTER v.Name =~ @pattern` | `WHERE Name ~ $pattern` or `WHERE string::matches(Name, $pattern)` |
| Substring | `CONTAINS(LOWER(v.Name), LOWER(@pat))` | `WHERE string::lowercase(Name) CONTAINS string::lowercase($pat)` |
| Array membership | `@flag IN v.Flags[*].Name` | `WHERE $flag IN Flags.*.Name` or `WHERE Flags[WHERE Name = $flag]` |
| Count | `COLLECT WITH COUNT INTO length RETURN length` | `SELECT count() FROM ...` |
| Sort | `SORT v.Name ASC` | `ORDER BY Name ASC` |
| Pagination | `LIMIT skip, count` | `LIMIT count START skip` |
| Distinct | `RETURN DISTINCT v.Folder` | `SELECT DISTINCT Folder FROM ...` |

#### Complex Queries

| Operation | AQL | SurrealQL |
|-----------|-----|-----------|
| Subquery | `LET x = (FOR v IN ... RETURN v)` | `LET $x = (SELECT * FROM ...)` |
| Nested traversal | `FOR v IN 1..99 OUTBOUND @s GRAPH g` | `@.{1..99}->edge->target` or `SELECT ->edge->target FROM $s` |
| Conditional | `checkParent ? (subquery) : []` | `IF $checkParent THEN (subquery) END` |
| Bind variables | `@name`, `@@collection` | `$name` |
| Dynamic collection | `FOR v IN @@C1` | Via string interpolation in query |

**Verdict**: ✅ **Roughly equivalent** — both languages are SQL-like with graph extensions. SurrealQL is arguably more readable for simple queries. AQL has `PRUNE` for early branch termination; SurrealDB v2.2.0+ has `+shortest`, `+collect`, and `+path` graph algorithms.

---

## 8. Index Support

### ArangoDB
```javascript
// Persistent index
{ type: "persistent", fields: ["Name"] }
// Inverted index (for regex/fulltext)
{ type: "inverted", fields: [{ name: "LongName" }] }
// Auto-increment key
{ type: "autoincrement", offset: 0, increment: 1 }
```

### SurrealDB
```sql
-- Standard index
DEFINE INDEX name_idx ON TABLE node_objects COLUMNS Name;
-- Unique index
DEFINE INDEX email_idx ON TABLE node_players COLUMNS email UNIQUE;
-- Full-text search index (BM25)
DEFINE INDEX name_search ON TABLE node_objects COLUMNS Name SEARCH ANALYZER ascii BM25;
-- Vector index (for embeddings)
DEFINE INDEX embed_idx ON TABLE docs FIELDS embedding MTREE DIMENSION 1536;
-- Composite index
DEFINE INDEX longname_idx ON TABLE node_attributes COLUMNS LongName;
```

**SharpMUSH Index Requirements**:
| Index | ArangoDB | SurrealDB |
|-------|----------|-----------|
| `Objects.Name` | Persistent | `DEFINE INDEX` |
| `Players.Aliases` | Persistent (array) | `DEFINE INDEX ... COLUMNS Aliases` |
| `Things.Aliases` | Persistent (array) | `DEFINE INDEX ... COLUMNS Aliases` |
| `Exits.Aliases` | Persistent (array) | `DEFINE INDEX ... COLUMNS Aliases` |
| `Attributes.LongName` | Persistent + Inverted | `DEFINE INDEX ... COLUMNS LongName` + `SEARCH ANALYZER` |

**Verdict**: ✅ **SurrealDB has comparable index support** — plus additional capabilities (vector search, BM25 full-text) that could be useful for future features.

---

## 9. Method-by-Method Mapping

### Object Lifecycle Methods

| ISharpDatabase Method | ArangoDB Implementation | SurrealDB Approach | Complexity |
|-----------------------|------------------------|-------------------|------------|
| `Migrate()` | `ArangoMigrator.UpgradeAsync()` | Custom migration runner + `RawQuery()` with DEFINE statements | 🔴 High — need to build migration framework |
| `CreatePlayerAsync()` | Transaction: Create object + player + 4 edges | `RawQuery()` with `BEGIN TRANSACTION; CREATE + RELATE; COMMIT` | 🟡 Medium — single query string |
| `CreateRoomAsync()` | Create object + room + 2 edges | `RawQuery()` with CREATE + RELATE | 🟢 Low |
| `CreateThingAsync()` | Transaction: Create object + thing + 4 edges | `RawQuery()` with `BEGIN TRANSACTION; CREATE + RELATE; COMMIT` | 🟡 Medium |
| `CreateExitAsync()` | Transaction: Create object + exit + 3 edges | `RawQuery()` with `BEGIN TRANSACTION; CREATE + RELATE; COMMIT` | 🟡 Medium |
| `SetPlayerPasswordAsync()` | `Document.UpdateAsync()` | `client.Merge()` or `RawQuery("UPDATE ...")` | 🟢 Low |
| `SetPlayerQuotaAsync()` | `Document.UpdateAsync()` | `client.Merge()` | 🟢 Low |
| `GetOwnedObjectCountAsync()` | AQL with `COLLECT WITH COUNT` | `RawQuery("SELECT count() FROM edge_has_object_owner WHERE out = $player")` | 🟢 Low |

### Link/Relationship Methods

| ISharpDatabase Method | ArangoDB Implementation | SurrealDB Approach | Complexity |
|-----------------------|------------------------|-------------------|------------|
| `LinkExitAsync()` | `Graph.Edge.CreateAsync()` | `client.Relate()` or `RawQuery("RELATE ...")` | 🟢 Low |
| `UnlinkExitAsync()` | Query edge → `Graph.Edge.RemoveAsync()` | `RawQuery("DELETE edge_has_home WHERE in = $exit")` | 🟢 Low |
| `LinkRoomAsync()` | Unlink existing + `Graph.Edge.CreateAsync()` | `RawQuery("DELETE ... ; RELATE ...")` | 🟢 Low |
| `UnlinkRoomAsync()` | Query edge → remove | `RawQuery("DELETE edge_has_home WHERE in = $room")` | 🟢 Low |
| `SetContentHome()` | Query edge key → `Graph.Edge.UpdateAsync()` | `RawQuery("UPDATE edge_has_home SET out = $home WHERE in = $obj")` | 🟢 Low |
| `SetContentLocation()` | Query edge key → `Graph.Edge.UpdateAsync()` | `RawQuery("UPDATE edge_at_location SET out = $loc WHERE in = $obj")` | 🟢 Low |
| `SetObjectParent()` | Complex: query + create/update edge | `RawQuery("DELETE edge_has_parent WHERE in = $obj; RELATE $obj->edge_has_parent->$parent")` | 🟢 Low |
| `UnsetObjectParent()` | Query edge → remove | `RawQuery("DELETE edge_has_parent WHERE in = $obj")` | 🟢 Low |
| `SetObjectZone()` | Complex: query + create/update edge | `RawQuery("DELETE edge_has_zone WHERE in = $obj; RELATE $obj->edge_has_zone->$zone")` | 🟢 Low |
| `UnsetObjectZone()` | Query edge → remove | `RawQuery("DELETE edge_has_zone WHERE in = $obj")` | 🟢 Low |
| `SetObjectOwner()` | Query edge key → update | `RawQuery("UPDATE edge_has_object_owner SET out = $owner WHERE in = $obj")` | 🟢 Low |
| `MoveObjectAsync()` | Query edge key → update `To` field | `RawQuery("UPDATE edge_at_location SET out = $dest WHERE in = $obj")` | 🟢 Low |
| `IsReachableViaParentOrZoneAsync()` | Multi-edge BFS traversal with dedup | `RawQuery` with `{..+collect}` recursive path for unique node collection, or `{..+shortest=target}` | 🟡 Medium — SurrealDB v2.2.0 `+collect`/`+shortest` provides equivalent |

### Object Retrieval Methods

| ISharpDatabase Method | ArangoDB Implementation | SurrealDB Approach | Complexity |
|-----------------------|------------------------|-------------------|------------|
| `GetObjectNodeAsync(DBRef)` | AQL: 0..1 INBOUND to determine type + hydrate | `RawQuery("SELECT * FROM node_objects:$key")` + type resolution | 🟡 Medium — type polymorphism |
| `GetBaseObjectNodeAsync()` | Simple document get | `client.Select<T>(recordId)` | 🟢 Low |
| `GetPlayerByNameOrAliasAsync()` | AQL with name/alias filter | `RawQuery("SELECT * FROM node_players WHERE Name = $n OR $n IN Aliases")` | 🟢 Low |
| `GetAllObjectsAsync()` | Stream all objects → hydrate each | `RawQuery()` with pagination wrapper | 🟡 Medium — no streaming |
| `GetFilteredObjectsAsync()` | Dynamic AQL with filters | Dynamic SurrealQL WHERE clauses | 🟡 Medium |
| `GetAllPlayersAsync()` | Stream all players | `RawQuery()` with pagination wrapper | 🟡 Medium — no streaming |
| `GetParentAsync()` | 1-hop OUTBOUND on GraphParents | `RawQuery("SELECT ->edge_has_parent->node_objects FROM $id")` | 🟢 Low |
| `GetParentsAsync()` | Variable-depth OUTBOUND | `RawQuery("$id.{1..999}->edge_has_parent->node_objects")` | 🟡 Medium |

### Location & Navigation Methods

| ISharpDatabase Method | ArangoDB Implementation | SurrealDB Approach | Complexity |
|-----------------------|------------------------|-------------------|------------|
| `GetLocationAsync(DBRef, depth)` | Variable-depth OUTBOUND | `RawQuery` with recursive path `@.{$depth}->edge_at_location->*` | 🟡 Medium |
| `GetLocationAsync(AnySharpObject)` | 1-hop OUTBOUND | `RawQuery("SELECT ->edge_at_location->* FROM $obj")` | 🟢 Low |
| `GetContentsAsync(DBRef)` | 1-hop INBOUND on GraphLocations | `RawQuery("SELECT <-edge_at_location<-* FROM $obj")` | 🟢 Low |
| `GetExitsAsync(DBRef)` | 1-hop INBOUND on GraphExits | `RawQuery("SELECT <-edge_has_exit<-* FROM $obj")` | 🟢 Low |
| `GetNearbyObjectsAsync()` | Get location contents + location | Compose from location + contents queries | 🟡 Medium |
| `GetEntrancesAsync()` | 1-hop INBOUND on GraphHomes | `RawQuery("SELECT <-edge_has_home<-node_exits FROM $dest")` | 🟢 Low |

### Flag & Power Methods

| ISharpDatabase Method | ArangoDB Implementation | SurrealDB Approach | Complexity |
|-----------------------|------------------------|-------------------|------------|
| `GetObjectFlagAsync(name)` | AQL filter on ObjectFlags | `RawQuery("SELECT * FROM node_object_flags WHERE Name = $name")` | 🟢 Low |
| `GetObjectFlagsAsync()` | Stream all flags | `client.Select<T>("node_object_flags")` | 🟢 Low |
| `GetObjectFlagsAsync(id, type)` | 1-hop OUTBOUND on GraphFlags | `RawQuery("SELECT ->edge_has_flags->node_object_flags FROM $id")` | 🟢 Low |
| `CreateObjectFlagAsync()` | `Document.CreateAsync()` | `client.Create("node_object_flags", data)` | 🟢 Low |
| `DeleteObjectFlagAsync()` | `Document.DeleteAsync()` | `client.Delete(recordId)` | 🟢 Low |
| `SetObjectFlagAsync()` | `Graph.Edge.CreateAsync()` | `client.Relate()` or `RawQuery("RELATE ...")` | 🟢 Low |
| `UnsetObjectFlagAsync()` | Query edge → remove | `RawQuery("DELETE edge_has_flags WHERE in = $obj AND out = $flag")` | 🟢 Low |
| `UpdateObjectFlagAsync()` | `Document.UpdateAsync()` | `client.Merge()` | 🟢 Low |
| `SetObjectFlagDisabledAsync()` | `Document.UpdateAsync()` | `client.Merge()` | 🟢 Low |
| `GetPowerAsync()` | AQL filter | `RawQuery("SELECT * FROM node_object_powers WHERE Name = $name")` | 🟢 Low |
| `GetObjectPowersAsync()` | Stream all powers | `client.Select<T>("node_object_powers")` | 🟢 Low |
| `CreatePowerAsync()` | `Document.CreateAsync()` | `client.Create(...)` | 🟢 Low |
| `DeletePowerAsync()` | `Document.DeleteAsync()` | `client.Delete(...)` | 🟢 Low |
| `SetObjectPowerAsync()` | `Graph.Edge.CreateAsync()` | `client.Relate()` | 🟢 Low |
| `UnsetObjectPowerAsync()` | Query edge → remove | `RawQuery("DELETE edge_has_powers WHERE ...")` | 🟢 Low |
| `UpdatePowerAsync()` | `Document.UpdateAsync()` | `client.Merge()` | 🟢 Low |
| `SetPowerDisabledAsync()` | `Document.UpdateAsync()` | `client.Merge()` | 🟢 Low |

### Attribute Methods (Most Complex)

| ISharpDatabase Method | ArangoDB Implementation | SurrealDB Approach | Complexity |
|-----------------------|------------------------|-------------------|------------|
| `SetAttributeAsync()` | Transaction: complex edge chain creation | `RawQuery()` transaction with CREATE + RELATE chain | 🔴 High |
| `GetAttributeAsync()` | OUTBOUND traversal with PRUNE | Multi-step RawQuery or recursive path with filtering | 🔴 High — no PRUNE |
| `GetAttributesAsync()` | OUTBOUND with regex on LongName | `RawQuery` with `WHERE LongName ~ $pattern` | 🟡 Medium |
| `GetAttributesByRegexAsync()` | OUTBOUND with regex + SORT | `RawQuery` with regex + ORDER BY | 🟡 Medium |
| `GetLazyAttributeAsync()` | Like above, lazy evaluation | Same approach, lazy wrapper | 🟡 Medium |
| `GetAttributeWithInheritanceAsync()` | **Most complex query**: multi-graph traversal with PRUNE, parent chain, zone chain | Multi-step RawQuery with LET bindings or application-level iteration | 🔴 Very High |
| `GetLazyAttributeWithInheritanceAsync()` | Lazy version of above | Same as above with lazy wrapper | 🔴 Very High |
| `ClearAttributeAsync()` | Check children → update or delete | `RawQuery` conditional logic | 🟡 Medium |
| `WipeAttributeAsync()` | Recursive delete of attribute tree | `RawQuery` with recursive deletion | 🟡 Medium |
| `GetAllAttributeEntriesAsync()` | Stream all entries | `client.Select<T>("node_attribute_entries")` | 🟢 Low |
| `GetSharpAttributeEntry()` | Document get by name | `RawQuery` or `Select` | 🟢 Low |
| `CreateOrUpdateAttributeEntryAsync()` | `Document.CreateAsync()` with overwrite | `client.Upsert(...)` | 🟢 Low |
| `DeleteAttributeEntryAsync()` | `Document.DeleteAsync()` | `client.Delete(...)` | 🟢 Low |
| `SetAttributeFlagAsync()` | Edge create | `client.Relate()` | 🟢 Low |
| `UnsetAttributeFlagAsync()` | Edge query + delete | `RawQuery("DELETE ...")` | 🟢 Low |
| `GetAttributeFlagAsync()` | AQL filter | `RawQuery` | 🟢 Low |
| `GetAttributeFlagsAsync()` | Stream all | `client.Select<T>(...)` | 🟢 Low |

### Lock Methods

| ISharpDatabase Method | ArangoDB Implementation | SurrealDB Approach | Complexity |
|-----------------------|------------------------|-------------------|------------|
| `SetLockAsync()` | `Document.UpdateAsync()` merge into Locks dict | `client.Merge()` with Locks field | 🟢 Low |
| `UnsetLockAsync()` | `Document.UpdateAsync()` remove from Locks dict | `RawQuery("UPDATE node_objects:$key SET Locks.$lockName = NONE")` | 🟢 Low |

### Mail Methods

| ISharpDatabase Method | ArangoDB Implementation | SurrealDB Approach | Complexity |
|-----------------------|------------------------|-------------------|------------|
| `SendMailAsync()` | Transaction: Create mail + 2 edges | `RawQuery()` transaction with CREATE + RELATE | 🟡 Medium |
| `GetIncomingMailsAsync()` | 1-hop INBOUND on GraphMail | `RawQuery("SELECT <-edge_received_mail<-node_mails FROM $player WHERE Folder = $f")` | 🟢 Low |
| `GetAllIncomingMailsAsync()` | 1-hop INBOUND | `RawQuery("SELECT <-edge_received_mail<-node_mails FROM $player")` | 🟢 Low |
| `GetIncomingMailAsync()` | INBOUND + LIMIT skip, 1 | `RawQuery` with LIMIT/START | 🟢 Low |
| `GetSentMailsAsync()` | ALL_SHORTEST_PATH | `RawQuery` with `{..+shortest=target}` recursive path or simple edge query | 🟢 Low — SurrealDB `+shortest` is direct equivalent |
| `GetAllSentMailsAsync()` | INBOUND traversal | `RawQuery` reverse traversal | 🟢 Low |
| `GetSentMailAsync()` | ALL_SHORTEST_PATH + filter | `{..+shortest=target}` recursive path or simple edge query | 🟢 Low — SurrealDB `+shortest` is direct equivalent |
| `UpdateMailAsync()` | `Document.UpdateAsync()` | `client.Merge()` | 🟢 Low |
| `DeleteMailAsync()` | `Document.DeleteAsync()` | `client.Delete()` | 🟢 Low |
| `GetMailFoldersAsync()` | INBOUND + DISTINCT | `RawQuery` with SELECT DISTINCT | 🟢 Low |
| `RenameMailFolderAsync()` | `Document.UpdateManyAsync()` | `RawQuery("UPDATE node_mails SET Folder = $new WHERE Folder = $old AND ...")` | 🟢 Low |
| `MoveMailFolderAsync()` | `Document.UpdateAsync()` | `client.Merge()` | 🟢 Low |
| `GetAllSystemMailAsync()` | Stream all mails | `client.Select<T>("node_mails")` | 🟢 Low |

### Channel Methods

| ISharpDatabase Method | ArangoDB Implementation | SurrealDB Approach | Complexity |
|-----------------------|------------------------|-------------------|------------|
| `GetAllChannelsAsync()` | Stream all channels | `client.Select<T>("node_channels")` | 🟢 Low |
| `GetChannelAsync()` | AQL filter by name | `RawQuery("SELECT * FROM node_channels WHERE Name = $name")` | 🟢 Low |
| `GetMemberChannelsAsync()` | OUTBOUND on GraphChannels | `RawQuery("SELECT ->edge_member_of_channel->node_channels FROM $obj")` | 🟢 Low |
| `CreateChannelAsync()` | Transaction: Create channel + owner edge | `RawQuery()` transaction | 🟡 Medium |
| `UpdateChannelAsync()` | `Graph.Vertex.UpdateAsync()` | `client.Merge()` | 🟢 Low |
| `UpdateChannelOwnerAsync()` | Edge query + update | `RawQuery("UPDATE edge_owner_of_channel SET out = $new WHERE in = $ch")` | 🟢 Low |
| `DeleteChannelAsync()` | `Graph.Vertex.RemoveAsync()` + clean edges | `client.Delete()` + `RawQuery("DELETE ... WHERE ...")` | 🟡 Medium |
| `AddUserToChannelAsync()` | Edge create with status data | `RawQuery("RELATE $obj->edge_member_of_channel->$ch SET ...")` | 🟢 Low |
| `RemoveUserFromChannelAsync()` | Edge query + delete | `RawQuery("DELETE edge_member_of_channel WHERE ...")` | 🟢 Low |
| `UpdateChannelUserStatusAsync()` | Edge query + update | `RawQuery("UPDATE edge_member_of_channel SET ... WHERE ...")` | 🟢 Low |

### Expanded Data Methods

| ISharpDatabase Method | ArangoDB Implementation | SurrealDB Approach | Complexity |
|-----------------------|------------------------|-------------------|------------|
| `SetExpandedObjectData()` | Upsert with key = `{objectId}_{dataType}` | `client.Upsert()` or `RawQuery()` | 🟢 Low |
| `GetExpandedObjectData<T>()` | `Document.GetAsync()` by composed key | `client.Select<T>(recordId)` | 🟢 Low |
| `SetExpandedServerData()` | Transaction: Upsert ServerData | `client.Upsert()` | 🟢 Low |
| `GetExpandedServerData<T>()` | `Document.GetAsync()` | `client.Select<T>(recordId)` | 🟢 Low |

### Zone Methods

| ISharpDatabase Method | ArangoDB Implementation | SurrealDB Approach | Complexity |
|-----------------------|------------------------|-------------------|------------|
| `SetObjectZone()` | Delete old + create edge | `RawQuery("DELETE ... ; RELATE ...")` | 🟢 Low |
| `UnsetObjectZone()` | Edge query + delete | `RawQuery("DELETE edge_has_zone WHERE ...")` | 🟢 Low |
| `GetObjectsByZoneAsync()` | 1-hop INBOUND on GraphZones | `RawQuery("SELECT <-edge_has_zone<-* FROM $zone")` | 🟢 Low |

---

## 10. Complexity Summary

### By Difficulty Category

**🟢 Low (55 methods)** — Direct 1:1 mapping via SDK methods or simple `RawQuery`:
- All simple CRUD operations (Create, Select, Update, Delete, Merge)
- All 1-hop edge traversals (location, home, exits, contents, flags, powers)
- All lock operations
- Most mail operations
- All expanded data operations

**🟡 Medium (15 methods)** — Require `RawQuery` with moderate SurrealQL:
- Methods that need transactions (CreatePlayer, CreateThing, CreateExit, SendMail, CreateChannel)
- Methods requiring dynamic query building (GetFilteredObjectsAsync)
- Methods returning `IAsyncEnumerable` (need pagination wrapper)
- IsReachableViaParentOrZoneAsync (use `+collect` or `+shortest` recursive paths)
- GetParentsAsync (variable depth)

**🔴 High (4 methods)** — Significant rewriting required:
- `Migrate()` — Need custom migration framework
- `GetAttributeAsync()` — PRUNE-based tree traversal needs multi-step query
- `GetAttributeWithInheritanceAsync()` — Complex multi-graph inheritance query
- `GetLazyAttributeWithInheritanceAsync()` — Same complexity as above

---

## 11. Key Risks and Concerns

### 11.1 No IAsyncEnumerable Streaming
**Risk**: High  
**Impact**: 30+ methods currently return `IAsyncEnumerable<T>` using ArangoDB's `ExecuteStreamAsync`. SurrealDB's .NET SDK only returns `Task<IEnumerable<T>>`, loading all results into memory.  
**Mitigation**: Implement a pagination wrapper that yields results in batches using `LIMIT`/`START` clauses.

### 11.2 No Migration Framework
**Risk**: Medium  
**Impact**: Schema evolution, index management, and data migration need custom tooling.  
**Mitigation**: Build a lightweight migration runner (~100 lines) that tracks applied migrations in a `_migrations` table and executes SurrealQL DEFINE statements via `RawQuery`.

### 11.3 No Auto-Increment Keys
**Risk**: Medium  
**Impact**: SharpMUSH's `DBRef` system requires sequential integer IDs (0, 1, 2...). SurrealDB uses `table:key` record IDs.  
**Mitigation**: Use a counter record and atomic increment in a transaction: `UPDATE counter:objects SET val += 1 RETURN AFTER`.

### 11.4 PRUNE-Based Attribute Traversal
**Risk**: High  
**Impact**: Attribute tree traversal is one of the most-called operations and relies on AQL's PRUNE for efficiency.  
**Mitigation**: Rewrite as iterative multi-step queries or use SurrealDB's recursive path syntax with WHERE filtering. May need to denormalize attribute paths.

### 11.5 Transaction Model Difference
**Risk**: Medium  
**Impact**: ArangoDB allows multiple API calls within a transaction scope. SurrealDB requires all statements in a single query.  
**Mitigation**: Compose SurrealQL transaction strings using `LET` bindings. More verbose but functionally equivalent.

### ~~11.6 No BFS/Shortest-Path Algorithms~~ *(CORRECTED)*
**Risk**: ~~Low~~ **Resolved**  
**Status**: SurrealDB v2.2.0+ **does** support these algorithms via recursive path syntax:
- `{..+shortest=record:id}` — finds shortest path to a specified record
- `{..+collect}` — collects all unique nodes walked (BFS-like order, closest first)
- `{..+path}` — enumerates all walked paths as arrays
- `+inclusive` modifier — includes the originating record in results
- Bounded variants (e.g., `{..3+shortest=record:id}`) — limit traversal depth

These map directly to ArangoDB's `ALL_SHORTEST_PATH` and `OPTIONS {uniqueVertices: 'global', order: 'bfs'}` patterns used in `IsReachableViaParentOrZoneAsync()` and mail queries.

### 11.7 SDK Maturity
**Risk**: Medium  
**Impact**: SurrealDB .NET SDK is at version 0.9.0 — not yet 1.0. API may change. SurrealDB server v3.0.0 was released Feb 17, 2026, and the .NET SDK added v3 compatibility via [PR #222](https://github.com/surrealdb/surrealdb.net/pull/222) (merged Feb 23, 2026), but no new NuGet release has been published yet.  
**Mitigation**: Pin to specific version; most functionality is stable. Monitor for a new NuGet release incorporating v3 support.

### 11.8 No Client-Side Transactions in .NET SDK
**Risk**: Medium  
**Impact**: The JavaScript SDK (`surrealdb.js`) supports `db.beginTransaction()` for true client-side API-level transactions (individual SDK calls within a transaction scope with commit/cancel). The .NET SDK does **not** have this feature — `ISurrealDbClient` has no `BeginTransaction()` method. All transactions must be composed as a single `RawQuery()` string.  
**Mitigation**: Use `RawQuery()` with `BEGIN TRANSACTION; ... COMMIT TRANSACTION;` and `LET` bindings for intermediate results. Monitor the .NET SDK for `BeginTransaction()` support — the JS SDK's implementation proves the server-side protocol supports it.

---

## 12. Advantages of SurrealDB Over ArangoDB

1. **Embedded Mode**: Eliminates Docker/server dependency — single-process deployment
2. **Simpler Relationship Model**: RELATE is more intuitive than named graph + edge collection management
3. **Richer Field Types**: `record<table>` type provides type-safe foreign keys at the schema level
4. **Built-in Schema Validation**: `SCHEMAFULL` tables with `ASSERT` constraints and computed `VALUE` fields
5. **Computed Fields**: `DEFINE FIELD full_name VALUE string::concat(first, ' ', last)` — can simplify application code
6. **Live Queries**: `ListenLive<T>()` provides real-time change notifications — useful for MUSH event systems
7. **Multi-Tenancy**: Native namespace/database hierarchy could support multiple game worlds
8. **Built-in Auth**: Native authentication and record-level access control
9. **JSON Patch**: RFC 6902 compliant partial updates via `Patch()` method
10. **In-Memory Testing**: `SurrealDb.Embedded.InMemory` provides fast test isolation without Docker

---

## 13. Disadvantages of SurrealDB vs ArangoDB

1. **No IAsyncEnumerable**: All query results loaded into memory at once
2. **No Migration Framework**: Must build custom solution
3. **No Auto-Increment Keys**: Must simulate with counter documents
4. **No PRUNE Equivalent**: Complex attribute traversal needs rewriting
5. **No Client-Side Transactions in .NET SDK**: Must compose transaction as single query string (JS SDK has `beginTransaction()` but .NET SDK does not yet)
6. **SDK at 0.9.0**: Not yet stable release; v3 compatibility merged but not yet published to NuGet
7. **No Named Graphs**: Graph topology not formally declared (no validation that edges connect correct types)
8. **No Explicit Lock Control**: Relies on MVCC — no fine-grained locking
9. **Less Mature Ecosystem**: Fewer .NET community resources compared to ArangoDB

---

## 14. Recommendation

### Feasibility: **YES — SurrealDB can replace ArangoDB for SharpMUSH**

The embedded mode is a compelling reason to migrate. Approximately **76% of methods (57/75)** map directly with low complexity. The remaining methods require moderate to significant rewriting, primarily:
- Attribute tree traversal (PRUNE replacement)
- Inheritance resolution (multi-graph → multi-step queries)
- Transaction composition (API-level → query-level)
- Custom migration framework

### Suggested Approach if Migrating

1. **Phase 1**: Build foundation
   - Custom migration framework (~100 lines)
   - Auto-increment ID generator  
   - `IAsyncEnumerable` pagination wrapper

2. **Phase 2**: Implement simple methods (55 low-complexity methods)
   - CRUD operations
   - Simple graph traversals (1-hop)
   - Flags, powers, locks

3. **Phase 3**: Implement medium-complexity methods (15 methods)
   - Transaction-based creation methods
   - Dynamic query builders
   - Mail and channel systems

4. **Phase 4**: Implement complex methods (4 methods)
   - Attribute tree traversal (new algorithm)
   - Inheritance resolution (new algorithm)

5. **Phase 5**: Testing and optimization
   - Port all existing ArangoDB tests
   - Benchmark embedded mode vs Docker ArangoDB
   - Optimize hot paths (attribute lookup, location queries)

### Estimated Effort
- **Low-complexity methods**: ~2-3 days
- **Medium-complexity methods**: ~3-5 days
- **High-complexity methods**: ~5-7 days
- **Migration framework + infrastructure**: ~2-3 days
- **Testing**: ~3-5 days
- **Total**: ~15-23 developer-days

---

## Appendix A: SurrealDB .NET SDK Complete Method Reference

### ISurrealDbClient Methods (v0.9.0, with v3 compat pending NuGet release)

> **Note**: SurrealDB v3.0.0 compatibility was added to the .NET SDK via [PR #222](https://github.com/surrealdb/surrealdb.net/pull/222) (merged Feb 23, 2026) but has not been published as a NuGet release yet. The JS SDK's `beginTransaction()` for client-side transactions is not yet available in the .NET SDK.

| Category | Method | Description |
|----------|--------|-------------|
| **Connection** | `Connect()` | Initialize connection |
| | `Use(ns, db)` | Set namespace/database |
| | `Version()` | Get server version |
| | `Health()` | Health check |
| **Auth** | `Authenticate(Tokens)` | JWT authentication |
| | `SignIn(RootAuth)` | Root sign in |
| | `SignIn(NamespaceAuth)` | Namespace sign in |
| | `SignIn(DatabaseAuth)` | Database sign in |
| | `SignIn<T>(ScopeAuth)` | Scope sign in |
| | `SignUp<T>(ScopeAuth)` | Scope sign up |
| | `Invalidate()` | Clear session |
| | `Info<T>()` | Get session info |
| **CRUD** | `Create<T>(T data)` | Create from record |
| | `Create<T>(string table, T? data)` | Create in table |
| | `Create<TData, TOutput>(StringRecordId, TData?)` | Create with specific ID |
| | `Select<T>(string table)` | Get all from table |
| | `Select<T>(RecordId)` | Get by record ID |
| | `Select<T>(StringRecordId)` | Get by string record ID |
| | `Select<TStart,TEnd,T>(RecordIdRange)` | Range query |
| | `Update<T>(T data)` | Full replace |
| | `Update<TData,TOutput>(RecordId, TData)` | Replace specific record |
| | `Update<T>(string table, T data)` | Replace all in table |
| | `Upsert<T>(T data)` | Create or replace |
| | `Upsert<TData,TOutput>(RecordId, TData)` | Upsert specific record |
| | `Upsert<T>(string table, T data)` | Upsert in table |
| | `Delete(string table)` | Delete all from table |
| | `Delete(RecordId)` | Delete by record ID |
| | `Delete(StringRecordId)` | Delete by string record ID |
| **Partial Update** | `Merge<TMerge,TOutput>(TMerge data)` | Partial update from record |
| | `Merge<T>(RecordId, Dictionary)` | Partial update with dict |
| | `Merge<T>(StringRecordId, Dictionary)` | Partial update with dict |
| | `MergeAll<TMerge,TOutput>(string table, TMerge data)` | Partial update all in table |
| | `Patch<T>(RecordId, JsonPatchDocument)` | JSON Patch |
| | `Patch<T>(StringRecordId, JsonPatchDocument)` | JSON Patch |
| | `PatchAll<T>(string table, JsonPatchDocument)` | JSON Patch all in table |
| **Graph** | `Relate<TOutput,TData>(string table, IEnumerable ins, IEnumerable outs, TData?)` | Batch relate |
| | `Relate<TOutput,TData>(RecordId, RecordId in, RecordId out, TData?)` | Single relate |
| | `InsertRelation<T>(T data)` | Insert typed relation |
| | `InsertRelation<T>(string table, T data)` | Insert relation to table |
| **Batch** | `Insert<T>(string table, IEnumerable data)` | Bulk insert |
| **Query** | `RawQuery(string query, IDictionary params)` | Execute arbitrary SurrealQL |
| **Variables** | `Set(string key, object value)` | Set session variable |
| | `Unset(string key)` | Clear session variable |
| **Live** | `ListenLive<T>(Guid queryUuid)` | Listen to live query |
| | `LiveRawQuery<T>(string query, IDictionary)` | Start live raw query |
| | `LiveTable<T>(string table, bool diff)` | Start live table query |
| | `Kill(Guid queryUuid)` | Stop live query |
| **Functions** | `Run<T>(string name, string? version, object[]? args)` | Run server function |

### Embedded-Only Methods (ISurrealDbProviderEngine)

| Method | Description |
|--------|-------------|
| `Export(ExportOptions?)` | Export database as SurrealQL script |
| `Import(string input)` | Import SurrealQL data |

---

## Appendix B: ArangoDB Features Not Available in SurrealDB

| ArangoDB Feature | Used In | SurrealDB Alternative |
|------------------|---------|----------------------|
| Named Graphs | All graph operations | Implicit via RELATE tables |
| Graph.Vertex.CreateAsync | Object creation in graphs | `Create` + `Relate` separately |
| Graph.Edge.UpdateAsync | Edge mutation | `RawQuery("UPDATE ...")` |
| Graph.Edge.RemoveAsync | Edge deletion | `RawQuery("DELETE ...")` or `Delete()` |
| ArangoMigrator | Schema migrations | Custom migration runner |
| ArangoKeyType.Autoincrement | Sequential DBRef IDs | Counter document pattern |
| ExecuteStreamAsync (IAsyncEnumerable) | 30+ streaming methods | Pagination wrapper |
| AQL PRUNE | Attribute tree traversal | Multi-step queries |
| AQL ALL_SHORTEST_PATH | Mail sender queries | ✅ `{..+shortest=record:id}` (v2.2.0+) |
| OPTIONS {uniqueVertices, order: 'bfs'} | Reachability check | ✅ `{..+collect}` for unique nodes in BFS order (v2.2.0+) |
| ArangoTransactionScope (Exclusive/Read locks) | 7+ transactional methods | MVCC (automatic) |
| mergeObjects / keepNull | Partial updates | `Merge()` method |
| Cache: true/false | Query caching | Not available |
| Collection schema validation | Object structure | SCHEMAFULL + DEFINE FIELD |
