# Storage provider benchmark comparison

**Date:** 2026-09-07

`docs/design/adr-storage-engine.md` action item 2 asked for the uncached storage shapes to be
measured before any engine comparison was made: FusionCache already absorbs the hot reads, so what
decides an engine here is the set of operations the cache never serves. This report records that
measurement. All four `ISharpDatabase` providers - ArangoDB, Memgraph, SurrealDB and the new
Lightning (LMDB) provider - run the same seven shared read/write operations plus the eight extended
shapes (`ExtendedDatabaseBenchmarks`): inheritance-chain resolution, wildcard and regex attribute
listing, a pushed-down filtered search, subtree wipe, cascading object delete, parent-chain
reachability, and eight concurrent writers.

## Environment

| | |
|---|---|
| CPU | Intel Core Ultra 7 265F, 1 CPU, 20 logical / 20 physical cores |
| OS | Linux CachyOS |
| Runtime | .NET 10.0.8 (10.0.826.23019), X64 RyuJIT x86-64-v3; GC Concurrent Workstation |
| SDK | .NET SDK 11.0.100-preview.3.26207.106 |
| BenchmarkDotNet | v0.15.8 |
| Job | `SHARPMUSH_CI_BENCHMARK=true` selects `Job.ShortRun` (id `CI`): WarmupCount 3, IterationCount 3, LaunchCount 1 |
| Diagnosers | Memory, Threading, Exception |
| ArangoDB | `arangodb:latest` via Testcontainers on Podman |
| Memgraph | `memgraph/memgraph:3.8.1` via Testcontainers on Podman |
| SurrealDB | embedded, `rocksdb:///home/grave/.local/share/sharpmush-bench/surreal-rocksdb` (the production on-disk engine), set through `SHARPMUSH_SURREALDB_BENCH_ENDPOINT` |
| Lightning | embedded LMDB, `PageSize` 16384 (16 KB), `MapSize` default `64L << 30` (64 GiB file-size ceiling, not RAM) |

Filesystem note: the Lightning and SurrealDB-RocksDB data directories both sit under
`Environment.SpecialFolder.LocalApplicationData` (`/home/grave/.local/share/...`), which on this
machine is btrfs on an NVMe disk - not tmpfs. `LightningBaseBenchmark.CreateDataDirectory` picks
that path deliberately, because `Path.GetTempPath` here is a tmpfs mount that serves every write
from RAM and hides the `fdatasync` LMDB performs on each commit. Every Lightning write number below
therefore includes a real disk sync.

Provenance: the Lightning and SurrealDB rows come from the final re-run
(`SHARPMUSH_CI_BENCHMARK=true ... --filter '*Lightning*' '*Surreal*'`, 30 benchmarks), which
includes the page-resumption seek fix and puts SurrealDB on RocksDB. The ArangoDB and Memgraph rows
come from the four-provider run through Podman Testcontainers (60 benchmarks, ~48 min).

## Shared operations

The seven operations `LightningDatabaseBenchmarks` and its ArangoDB / Memgraph / SurrealDB
equivalents share. Units are as BenchmarkDotNet printed them; note that Lightning's read rows are in
nanoseconds and the others in microseconds.

### Read: `GetObjectNodeAsync(#1)` - God player

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 1,120.3 ns | 184.8 ns | 3264 B |
| SurrealDB (RocksDB) | 414.0 μs | 232.0 μs | 50.97 KB |
| ArangoDB | 706.7 μs | 1,408.3 μs | 17.37 KB |
| Memgraph | 307.4 μs | 253.7 μs | 52.89 KB |

### Read: `GetObjectNodeAsync(#2)` - Master Room

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 1,054.0 ns | 188.4 ns | 3152 B |
| SurrealDB (RocksDB) | 202.0 μs | 411.3 μs | 26.56 KB |
| ArangoDB | 771.0 μs | 2,180.3 μs | 14.21 KB |
| Memgraph | 290.0 μs | 329.7 μs | 49.67 KB |

### Read: `GetContentsAsync(Master Room)`

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 7,251.1 ns | 600.8 ns | 23297 B |
| SurrealDB (RocksDB) | 2,137.6 μs | 6,652.3 μs | 213.83 KB |
| ArangoDB | 918.7 μs | 691.3 μs | 54.43 KB |
| Memgraph | 1,458.3 μs | 3,810.4 μs | 223.6 KB |

### Read: `GetLocationAsync(#1)` - 1-hop traversal

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 1,779.0 ns | 366.5 ns | 4592 B |
| SurrealDB (RocksDB) | 976.3 μs | 924.7 μs | 101.23 KB |
| ArangoDB | 1,725.4 μs | 5,432.9 μs | 40.97 KB |
| Memgraph | 644.4 μs | 584.1 μs | 103.66 KB |

### Read: `GetAttributeAsync(#1, AADESC)`

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 479.1 ns | 125.2 ns | 848 B |
| SurrealDB (RocksDB) | 300.1 μs | 696.7 μs | 48.42 KB |
| ArangoDB | 688.4 μs | 613.9 μs | 10.6 KB |
| Memgraph | 646.7 μs | 1,135.9 μs | 44.58 KB |

### Write: `CreateThingAsync` - unique name each call

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 4.413 ms | 1.605 ms | 2.91 KB |
| SurrealDB (RocksDB) | 599.6 μs | 2,240.5 μs | 34.53 KB |
| ArangoDB | 4.281 ms | 1.830 ms | 56.83 KB |
| Memgraph | 391.4 μs | 143.4 μs | 47.44 KB |

### Write: `SetAttributeAsync` on #1

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 5.384 ms | 6.704 ms | 5.23 KB |
| SurrealDB (RocksDB) | 2,127.9 μs | 13,057.0 μs | 90.5 KB |
| ArangoDB | 3.958 ms | 1.882 ms | 41.36 KB |
| Memgraph | 394.4 μs | 1,423.5 μs | 51.49 KB |

## Extended shapes

`ExtendedDatabaseBenchmarks` seeds every fixture once per benchmark process, then measures one
shape. `WipeAttributeAsync` and `DeleteObjectAsync` run with `InvocationCount=1, UnrollFactor=1`
because each needs a fresh `[IterationSetup]` fixture; the other six use the default unroll of 16.

### `GetAttributeWithInheritanceAsync` - self, 2 parents, then ancestor zone

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 3.601 μs | 0.4027 μs | 8.34 KB |
| SurrealDB (RocksDB) | 1.325 ms | 1.984 ms | 335.94 KB |
| ArangoDB | 2,332.3 μs | 2,315.2 μs | 25.65 KB |
| Memgraph | 6,300.8 μs | 9,281.3 μs | 250.59 KB |

### `GetAttributesAsync(ATTR1*)` over 200 flat attributes

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 118.634 μs | 16.7547 μs | 227.99 KB |
| SurrealDB (RocksDB) | 19.137 ms | 27.100 ms | 353.9 KB |
| ArangoDB | 2,767.4 μs | 6,469.2 μs | 183.76 KB |
| Memgraph | 13,780.4 μs | 6,538.0 μs | 2429.05 KB |

### `GetAttributesByRegexAsync(^TREE.*)` over a 50-leaf subtree

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 79.523 μs | 8.6392 μs | 155.64 KB |
| SurrealDB (RocksDB) | 14.030 ms | 9.208 ms | 365.27 KB |
| ArangoDB | 2,276.2 μs | 2,632.5 μs | 101.91 KB |
| Memgraph | 7,715.6 μs | 3,836.6 μs | 1259.75 KB |

### `GetFilteredObjectsAsync(Types=THING, HasFlag=WIZARD, Owner=God)` over 1,000 things

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 1,333.929 μs | 105.8919 μs | 2432.1 KB |
| SurrealDB (RocksDB) | 172.267 ms | 67.199 ms | 363.02 KB |
| ArangoDB | 81,781.5 μs | 211,543.2 μs | 1853.99 KB |
| Memgraph | 1,838.4 μs | 1,290.8 μs | 682.89 KB |

### `IsReachableViaParentOrZoneAsync` across a 30-deep parent chain

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 7.116 μs | 0.6487 μs | 15.36 KB |
| SurrealDB (RocksDB) | 4.415 ms | 5.502 ms | 1356.23 KB |
| ArangoDB | 626.0 μs | 286.2 μs | 9.89 KB |
| Memgraph | 158.0 μs | 150.4 μs | 23.34 KB |

### 8 concurrent writers x 25 `SetAttributeAsync` calls each, on separate objects

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 924,038.149 μs | 542,690.1512 μs | 1004.7 KB |
| SurrealDB (RocksDB) | 20.217 ms | 20.752 ms | 18348.77 KB |
| ArangoDB | 856,407.7 μs | 1,349,180.7 μs | 8259.77 KB |
| Memgraph | 217,193.1 μs | 840,345.8 μs | 14685 KB |

### `WipeAttributeAsync` over a freshly-created 50-leaf subtree

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 4,249.572 μs | 12,299.7957 μs | 12.91 KB |
| SurrealDB (RocksDB) | 56.251 ms | 51.098 ms | 8802.95 KB |
| ArangoDB | 241,263.6 μs | 1,645,519.3 μs | 356.48 KB |
| Memgraph | 4,091.0 μs | 18,365.9 μs | 122.36 KB |

### `DeleteObjectAsync` on a freshly-created thing with 20 attributes

| Provider | Mean | Error | Allocated |
|---|---:|---:|---:|
| Lightning | 5,916.745 μs | 15,921.2367 μs | 17.11 KB |
| SurrealDB (RocksDB) | 30.065 ms | 38.797 ms | 1249.55 KB |
| ArangoDB | 53,467.5 μs | 115,345.6 μs | 469.79 KB |
| Memgraph | 2,787.4 μs | 18,023.3 μs | 78.28 KB |

## SurrealDB: `mem://` versus `rocksdb://`

The extended shapes on the in-memory engine (`t22-bench-embedded-final.log`) against the same
shapes on the production RocksDB engine. This is what a `mem://` test run hides.

| Shape | `mem://` Mean | `rocksdb://` Mean | Delta |
|---|---:|---:|---:|
| InheritanceWalk | 1.260 ms | 1.325 ms | +5% |
| WildcardLattr | 13.460 ms | 19.137 ms | +42% |
| RegexLattr | 13.218 ms | 14.030 ms | +6% |
| FilteredSearch | 168.590 ms | 172.267 ms | +2% |
| Reachability | 4.408 ms | 4.415 ms | 0% |
| ConcurrentAttributeWrites | 15.129 ms | 20.217 ms | +34% |
| WipeSubtree | 47.548 ms | 56.251 ms | +18% |
| DeleteObject | 32.012 ms | 30.065 ms | -6% |

The gap is smaller than the equivalent gap for Lightning would be, and the reason is structural:
RocksDB batches into a memtable and its WAL is not `fsync`ed per commit by default, so a
SurrealDB write returns before the data is durable on disk. LMDB's commit is a synchronous
page-write plus `fdatasync`. The two engines are not being asked for the same durability guarantee
in these rows.

## Analysis

**Point reads.** `GetAttributeAsync` costs Lightning 479.1 ns against 688.4 μs on ArangoDB - about
1,400x. `GetObjectNodeAsync(#1)` is ~630x faster than ArangoDB and ~270x faster than Memgraph. This
is the expected shape of the comparison rather than a surprise: LMDB resolves a key by walking a
memory-mapped B+tree in-process, while ArangoDB and Memgraph each pay a loopback TCP round trip,
request parsing, and result serialisation per call. The allocation column shows the same thing from
the other side - 848 B for Lightning against 10.6-48.42 KB for the server-backed providers, most of
which is response deserialisation.

**Prefix scans.** `GetAttributesAsync(ATTR1*)` is 118.634 μs on Lightning against 13,780.4 μs on
Memgraph (116x) and 19.137 ms on SurrealDB (161x). The flat, dbref-prefixed attribute keyspace
turns a wildcard `lattr` into one cursor seek and a sequential walk; the three graph/document
providers reconstruct the attribute tree per row. `IsReachableViaParentOrZoneAsync` shows the widest
spread of the read shapes: 7.116 μs against SurrealDB's 4.415 ms, ~620x.

**Concurrent writers.** This is where Lightning is worst. 924 ms for 8 x 25 writes - about 4.6 ms
per write - against SurrealDB's 20.217 ms for the whole shape (46x faster) and Memgraph's 217 ms.
LMDB permits exactly one writer at a time, and each of the 200 commits pays its own `fdatasync` on
btrfs. The single-write rows agree: `SetAttributeAsync` is 5.384 ms on Lightning against 394.4 μs on
Memgraph. ArangoDB is in the same band as Lightning here (856 ms), for a different reason - it
serialises on exclusive locks and pays a round trip per write.

**Memgraph's exception count.** Memgraph's 217 ms concurrent-writer row carries 460 exceptions per
operation in the ExceptionDiagnoser column, which is its 8-retry write-conflict loop firing under
contention. The mean is real, but it is a mean over a workload that is mostly retrying.

**Wipe and delete.** Lightning wipes a 50-leaf subtree in 4,249.572 μs, effectively tied with
Memgraph's 4,091.0 μs and 57x faster than ArangoDB's 241,263.6 μs. The subtree is a contiguous key
range, so the wipe is one cursor delete loop inside a single transaction with a single commit. The
same reasoning applies to `DeleteObjectAsync` (5,916.745 μs against ArangoDB's 53,467.5 μs), where
Lightning is nevertheless 2.1x slower than Memgraph - the cascade touches several tables and each
one's reverse index, and it is still one synchronous commit.

**FilteredSearch caveat.** `SeedAsync` creates every fixture the suite needs, not only the 1,000
things this shape filters: the inheritance ladder, the wide-attribute object, the 30-object
reachability chain, the eight concurrent-write targets and a second player all land in the same
object table. The scan therefore covers roughly 1,045 objects on every provider, and the filter
still matches exactly 100. BenchmarkDotNet isolates each benchmark case in its own process with its
own `[GlobalSetup]`, so the `[IterationSetup]` benchmarks (`WipeSubtree`, `DeleteObject`) never add
rows alongside this one - but if that isolation is ever removed, this row's object count would drift
upward within a run and the numbers would stop being comparable. The absolute figures also spread
enormously: 1,333.929 μs on Lightning, 1,838.4 μs on Memgraph, 81,781.5 μs on ArangoDB, 172.267 ms
on SurrealDB.

**Error bars.** The CI job runs 3 warmups and 3 iterations, which is enough to rank providers and
not enough to trust any single mean to better than its own order of magnitude. The worst row is
ArangoDB's `WipeAttributeAsync`: Error 1,645,519.3 μs on a Mean of 241,263.6 μs, an interval 6.8x
the mean. Memgraph's `DeleteObjectAsync` (6.5x), SurrealDB's `SetAttributeAsync` (6.1x) and
ArangoDB's `GetObjectNodeAsync(#2)` (2.8x) are in the same category. Treat every cross-provider
comparison narrower than about 5x as unresolved by this data.

**Scope.** One machine, one run, no repetition across boots, with Podman-hosted containers competing
for the same 20 cores as the benchmark process. The results are directional: they establish which
shapes each engine is good and bad at, not a throughput number anyone should plan capacity from.

## Follow-up: group commit and sync modes

The concurrent-writer row above was the case for changing how the Lightning writer commits. Two
changes followed (`SharpMUSH.Database.Lightning/Store`): the writer thread now folds every typed job
that queued up while the previous commit was in flight into one transaction, each job in its own
nested transaction, one parent commit and one sync for the batch (`MaxBatch` 64); and
`SHARPMUSH_LIGHTNING_SYNC` selects the durability mode - `full` (both fsyncs per commit, the
default and the mode every Lightning row above ran in), `nometasync` (one fsync per commit) or
`periodic` (no sync on commit, a timer forces one every `SHARPMUSH_LIGHTNING_FLUSH_MS` while
anything is unflushed). The same 15 Lightning shapes, same CI job, same btrfs directory, run once in
`full` and once in `periodic`.

| Shape | Before (one commit per job) | Group commit, `full` | Group commit, `periodic` |
|---|---:|---:|---:|
| 8 concurrent writers x 25 `SetAttributeAsync` | 924.0 ms | 251.7 ms | 1.588 ms |
| `SetAttributeAsync` on #1 | 5.384 ms | 5.051 ms | 14.59 μs |
| `CreateThingAsync` | 4.413 ms | 4.632 ms | 47.82 μs |
| `WipeAttributeAsync`, 50-leaf subtree | 4.250 ms | 5.014 ms | 267.5 μs |
| `DeleteObjectAsync`, 20 attributes | 5.917 ms | 6.286 ms | 577.6 μs |
| `GetAttributeAsync(#1, AADESC)` | 479.1 ns | 539.5 ns | 489.2 ns |
| `GetAttributesAsync(ATTR1*)` | 118.6 μs | 117.7 μs | 107.8 μs |

**Group commit under `full`.** The contended shape drops from 924 ms to 252 ms, 3.7x. Not 8x,
and the reason is the benchmark's own shape: each of the eight writers awaits its commit before
issuing its next write, so at most eight jobs can ever be waiting, and a writer's continuation has
to be scheduled and reach the channel before the writer thread takes the next batch. At the 4.6 ms
per sync the single-write rows show, 252 ms is about 55 syncs for 200 writes - batches of three to
four on average (inferred from the timing; the commit counter is not exposed to the benchmark). A
workload that queues writes without waiting on each one, such as a `@dolist` over attributes or a
mail delivery fan-out, would fill batches to the cap. Every uncontended row is unchanged within its
error bar: a lone job still runs straight in the top-level transaction and pays its own sync.

**`periodic`.** With the per-commit sync gone, every write shape lands where the tmpfs run put it
(`CreateThingAsync` 36.9 μs and `SetAttributeAsync` 14.6 μs there against 47.8 μs and 14.6 μs
here), which confirms that the sync was the entire write cost and nothing else in the provider was
hiding behind it. This mode gives the same guarantee the SurrealDB-RocksDB rows were measured
under - a crash loses the unflushed window, the file stays consistent - so those are the fair
comparison: 1.59 ms against 20.2 ms for the concurrent writers (12.7x), 14.6 μs against 2.13 ms for
one attribute set, 267 μs against 56.3 ms for the subtree wipe. Against Memgraph, the fastest of
the server-backed providers on writes, one attribute set is 14.6 μs against 394 μs.

Reads are unaffected by either change, as they should be: the sync mode only changes what commit
does, and a read transaction never commits.

## Reproducing

```bash
cd /path/to/SharpMUSH

# Embedded providers only (Lightning + SurrealDB), SurrealDB on its production RocksDB engine.
export SHARPMUSH_CI_BENCHMARK=true
export SHARPMUSH_SURREALDB_BENCH_ENDPOINT="rocksdb://$HOME/.local/share/sharpmush-bench/surreal-rocksdb"
dotnet run --project SharpMUSH.Benchmarks -c Release -- --filter '*Lightning*' '*Surreal*'

# All four providers. ArangoDB and Memgraph come up as Testcontainers, so a container runtime
# is required; on rootless Podman set these first.
export DOCKER_HOST="unix:///run/user/$(id -u)/podman/podman.sock"
export TESTCONTAINERS_RYUK_DISABLED=true
dotnet run --project SharpMUSH.Benchmarks -c Release -- --filter '*DatabaseBenchmarks*' '*Extended*'
```

Other variables:

- `SHARPMUSH_CI_BENCHMARK=true` selects the 3-warmup / 3-iteration short job. Omit it for the
  nightly job (`Job.Default`), which is far slower and much tighter.
- `SHARPMUSH_SURREALDB_BENCH_ENDPOINT` defaults to `mem://`. Point it at
  `rocksdb://<absolute path>` on a real (non-tmpfs) filesystem to measure the production engine.
- `SHARPMUSH_LIGHTNING_SYNC=periodic` (or `nometasync`) runs the Lightning benchmarks in that sync
  mode; the benchmark host reads it exactly as the server does. Unset means `full`.
- `SHARPMUSH_LIGHTNING_BENCH_PATH` overrides the LMDB data-directory root; a fresh GUID
  subdirectory is still created beneath it. It defaults to `LocalApplicationData`, never
  `Path.GetTempPath`, so that fsync cost is measured rather than hidden by tmpfs.
- With Ryuk disabled nothing reaps containers when a run is killed. Clean up with
  `podman rm -f $(podman ps -aq --filter label=org.testcontainers=true)` between runs.

Results land in `BenchmarkDotNet.Artifacts/results/*-report-github.md`, one file per benchmark
class, which is what the tables above are copied from.
