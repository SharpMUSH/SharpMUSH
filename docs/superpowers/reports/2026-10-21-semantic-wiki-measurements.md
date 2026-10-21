# Semantic wiki measurements (step 5c)

Recorded measurements, not gates. Nothing here passes or fails on a number. The limits at the end are
revised only as far as these runs show.

## What was measured, and how

- **Commit:** the measurements branch at `eeb3809005`. The semantic wiki stack is as merged in #2131,
  plus the writer and read timings and the rebuild validation fix in this branch.
- **Machine:** Homelab, an Intel Core Ultra 7 265F (20 cores), 30 GiB RAM, btrfs on NVMe, CachyOS
  (Linux 7.2.9), .NET SDK 11.0.100-rc.2. The OS page cache was never dropped. Every first pass is labelled
  **first run after process start, page cache not dropped**, and the second pass in the same process is
  labelled warm.
- **Two places, kept apart:**
  - **Disposable server.** Ordinary game commands with wiki reads off, on a private SharpMUSH stack. The
    stack is nats-server, Server, ConnectionServer renderer and SocketServer, all on free ports, started
    the parity harness's way. A private PennMUSH builds `tools/parity/world` and dumps it, and SharpMUSH
    imports it (`tools/semantic-load/command_load.py`). There were no package activations, no deployment
    and no root. Everything stopped at the end, and the existing `:48434` preview was not touched.
  - **Issued fixtures.** Semantic query load ran only against fixture-backed worlds
    (`SemanticLoadMeasurement`, driven by `tools/semantic-load/run-fixture.sh`). No real game had semantics
    activated.
- **Offered load:**
  - Queries: open loop at R ∈ {0, 5, 20, 50} per second for 10 minutes per pass, each R in its own fresh
    process (a cold pass, then a warm pass). Latency is measured from the scheduled time, so queueing
    counts. Shapes rotate Q1 relationship (every second Q1 names the hot target in generated sets), Q2
    integer range, Q2 keyword, Q3 conjunction and Q4 generated list. Readers rotate public, character,
    secret and administrator.
  - Game commands: 10 per second, look, say, pose and @set, alongside the queries throughout. On fixtures
    they go through the real command queue (`AdmitSocketWork`) and are timed from the scheduled time to
    their release. On the disposable server they go over telnet, each followed by a `think` sentinel.
  - A rebuild at the midpoint of every warm pass.
- **Sampled every second:** working set, page faults, allocated bytes, GC pauses, writer queue depth,
  writer waits and holds, read-transaction counts, the environment's map and file size, and the
  journal's length. Writer wait and hold and read-transaction duration percentiles are exact on fixtures,
  from every measurement. On the disposable server they come from `/metrics` histograms, so they are
  bucket upper bounds (OTel's default boundaries: 0, 5, 10, 25, 50, 75, 100, 250 ms and up).

## Datasets

| Set | Pages | Facts | Notes | Checksums (SHA-256) |
|---|---:|---:|---|---|
| pf2e 1× | 4,711 | 10,605 | `pf2e-remaster` 1.1.1 at `2659a733`, verified against the pinned lock by `tools/WikiSemantics dry-run` | pages `8d3dcec3…17b7`, facts `a02b5266…faa` |
| pf2e 10× | 47,111 | 151,850 | `generate.py --times 10`. 99.9% of pages are in category `sealed`, which a read rule closes to everyone but administrators. A hot target is the `pf2e:class` of all 45,800 subjects. | pages `a571bab3…78c7`, facts `9b65f485…910e` |
| pf2e 100× | 471,101 | 1,518,500 (generated) | as 10×, with 458,000 hot-target edges | pages `9f91d93f…4600`, facts `3a5ce558…b9c3` |

The full checksums are in each generated set's `manifest.json`. Page bodies are title-only (`# Title`);
semantic queries never read bodies.

## Results

### Disposable server, wiki reads off

Engine ready 27 s after start; 10.0 commands/s over 3 sessions.

| Pass | Commands | p50 ms | p95 ms | p99 ms | max ms | look p99 | say p99 | pose p99 | @set p99 | Peak RSS MiB | Minor faults | Major faults | GC pause s | Allocated MiB | Writer hold p95 ≤ ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| cold | 6000 | 4.3 | 38.4 | 72.4 | 283.8 | 5.7 | 7.8 | 7.6 | 113.1 | 402 | 29007 | 0 | 0.82 | 4742 | 75.0 |
| warm | 6000 | 4.5 | 37.2 | 69.1 | 305.2 | 6.0 | 8.3 | 8.2 | 137.3 | 402 | 10510 | 1 | 0.96 | 4817 | 75.0 |

### pf2e-100x

| R/s | Pass | Queries | q p50 | q p95 | q p99 | Outcomes | Cmd p50 | Cmd p95 | Cmd p99 | Writer wait p99 | Writer hold p99 | Read txn p99 | Peak reads | Peak WS MiB | Faults min/maj | GC pause ms | Alloc MiB | File MiB | Rebuild |
|---:|---|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---|

### pf2e-10x

Build: 47111 pages (0 refused), 45800 pages with 151850 facts; activate 0.4 s, create 467 s, import 392 s. File 149 MiB, live 149 MiB.

| R/s | Pass | Queries | q p50 | q p95 | q p99 | Outcomes | Cmd p50 | Cmd p95 | Cmd p99 | Writer wait p99 | Writer hold p99 | Read txn p99 | Peak reads | Peak WS MiB | Faults min/maj | GC pause ms | Alloc MiB | File MiB | Rebuild |
|---:|---|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---|
| 0 | cold | 0 | – | – | – | – | 2.7 | 45.1 | 105.5 | 0.0 | 165.7 | 0.1 | 2 | 469 | 28587/0 | 421 | 2974 | 179 | – |
| 0 | warm | 0 | – | – | – | – | 2.7 | 42.8 | 111.5 | 6.8 | 103.1 | 0.1 | 2 | 752 | 48224/48 | 660 | 5912 | 179 | 47113 subj in 36.0 s (1309/s), Flipped { Generation = 2 } |
| 5 | cold | 3001 | 0.9 | 16.3 | 19.6 | NotCertified.Unavailable 149, Page 2702, RefineRequired 150 | 2.9 | 48.4 | 129.5 | 0.0 | 177.8 | 0.2 | 3 | 532 | 37399/0 | 664 | 8389 | 179 | – |
| 5 | warm | 3001 | 0.9 | 15.9 | 19.2 | NotCertified.Unavailable 149, Page 2702, RefineRequired 150 | 2.8 | 48.6 | 98.0 | 13.1 | 105.1 | 0.2 | 3 | 716 | 41720/60 | 896 | 11326 | 180 | 47113 subj in 48.5 s (972/s), Flipped { Generation = 3 } |
| 20 | cold | 12000 | 1.0 | 15.1 | 18.4 | NotCertified.Unavailable 596, Page 10804, RefineRequired 600 | 2.9 | 40.2 | 81.2 | 0.0 | 119.5 | 12.5 | 2 | 520 | 39946/1 | 1330 | 24415 | 180 | – |
| 20 | warm | 12001 | 1.0 | 15.1 | 18.4 | NotCertified.Unavailable 596, Page 10805, RefineRequired 600 | 2.9 | 36.0 | 84.9 | 9.7 | 84.1 | 12.5 | 3 | 717 | 42993/0 | 1556 | 27353 | 180 | 47113 subj in 40.3 s (1168/s), Flipped { Generation = 4 } |
| 50 | cold | 30001 | 0.9 | 14.6 | 17.9 | NotCertified.Unavailable 1490, Page 27011, RefineRequired 1500 | 2.8 | 42.0 | 94.3 | 0.0 | 138.8 | 13.6 | 3 | 551 | 39574/0 | 2604 | 56376 | 180 | – |
| 50 | warm | 30001 | 0.8 | 14.8 | 17.9 | NotCertified.DataChanged 1, NotCertified.Unavailable 1490, Page 27010, RefineRequired 1500 | 2.7 | 30.8 | 75.7 | 7.4 | 81.7 | 13.6 | 4 | 724 | 43390/0 | 2827 | 59315 | 180 | 47113 subj in 44.3 s (1063/s), Flipped { Generation = 5 } |

Warm query p99 ms by shape:

| Shape | R=5 | R=20 | R=50 |
|---|---:|---:|---:|
| Q1-relationship | 2.1 | 1.7 | 1.7 |
| Q2-keyword | 1.8 | 1.8 | 1.6 |
| Q2-range | 2.2 | 1.7 | 1.6 |
| Q3-conjunction | 17.9 | 16.7 | 15.3 |
| Q4-list | 20.5 | 19.4 | 18.6 |

### pf2e-1x

Build: 4711 pages (0 refused), 4580 pages with 10605 facts; activate 0.1 s, create 38 s, import 42 s. File 15 MiB, live 14 MiB.

| R/s | Pass | Queries | q p50 | q p95 | q p99 | Outcomes | Cmd p50 | Cmd p95 | Cmd p99 | Writer wait p99 | Writer hold p99 | Read txn p99 | Peak reads | Peak WS MiB | Faults min/maj | GC pause ms | Alloc MiB | File MiB | Rebuild |
|---:|---|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---|
| 0 | cold | 0 | – | – | – | – | 2.5 | 67.0 | 234.6 | 3.0 | 284.8 | 0.1 | 2 | 399 | 32526/0 | 429 | 2572 | 15 | – |
| 0 | warm | 0 | – | – | – | – | 2.6 | 42.7 | 103.8 | 6.7 | 129.5 | 0.1 | 2 | 464 | 17420/5 | 435 | 2781 | 17 | 4713 subj in 4.4 s (1064/s), Flipped { Generation = 2 } |
| 5 | cold | 3000 | 1.4 | 10.8 | 12.3 | Page 3000 | 2.6 | 54.0 | 117.8 | 0.0 | 186.7 | 7.2 | 3 | 420 | 36436/2 | 767 | 9620 | 17 | – |
| 5 | warm | 3000 | 1.4 | 10.9 | 12.3 | Page 3000 | 2.8 | 34.2 | 75.4 | 0.0 | 110.3 | 7.0 | 3 | 494 | 20907/4 | 741 | 9827 | 17 | 4713 subj in 3.3 s (1427/s), Flipped { Generation = 3 } |
| 20 | cold | 12001 | 1.3 | 10.6 | 12.3 | Page 12001 | 2.8 | 39.3 | 95.4 | 0.0 | 150.1 | 9.2 | 3 | 433 | 43014/1 | 1697 | 30605 | 17 | – |
| 20 | warm | 12000 | 1.3 | 10.5 | 12.0 | Page 12000 | 2.8 | 39.3 | 87.4 | 0.0 | 112.9 | 9.2 | 3 | 506 | 25234/0 | 1715 | 30817 | 17 | 4713 subj in 3.0 s (1557/s), Flipped { Generation = 4 } |
| 50 | cold | 30000 | 1.2 | 9.8 | 10.6 | Page 30000 | 2.7 | 38.7 | 86.6 | 0.0 | 135.7 | 8.9 | 3 | 439 | 44072/0 | 2907 | 72435 | 17 | – |
| 50 | warm | 30000 | 1.2 | 9.8 | 10.6 | Page 30000 | 2.7 | 31.3 | 63.8 | 0.0 | 88.1 | 9.0 | 3 | 507 | 23629/6 | 2995 | 72648 | 18 | 4713 subj in 2.4 s (1987/s), Flipped { Generation = 5 } |

Warm query p99 ms by shape:

| Shape | R=5 | R=20 | R=50 |
|---|---:|---:|---:|
| Q1-relationship | 2.2 | 1.9 | 1.7 |
| Q2-keyword | 2.0 | 1.7 | 1.6 |
| Q2-range | 1.9 | 1.8 | 1.6 |
| Q3-conjunction | 5.1 | 4.5 | 3.3 |
| Q4-list | 14.2 | 13.2 | 11.5 |


The per-phase summaries are in [`2026-10-21-semantic-wiki-measurements/`](2026-10-21-semantic-wiki-measurements/).
The raw samples (per-query and per-command CSVs, plus per-second samples) stay on Homelab under
`/temp/codex/pf2e-settlement/5c`, with their SHA-256 sums in
[`raw-samples.sha256`](2026-10-21-semantic-wiki-measurements/raw-samples.sha256).

## What the numbers say

- **Query latency is flat in offered load up to 50/s, at both 1× and 10×.** Warm p99 is 10–12 ms at 1×
  and 18 ms at 10×, at every rate. The spread is mostly in the shape:
  - Relationship, keyword and range lookups stay under 2.2 ms at p99 at both sizes.
  - The conjunction (Q3) rises from 3–5 ms to 15–18 ms, and the generated list (Q4) from 12–14 ms to
    19–21 ms, as the candidate sets grow tenfold.
- **Every non-page outcome at 10× is the honest answer:**
  - About 5% of queries are `Unavailable`. These are the character reader's Q1 against class pages, which
    sit in the sealed category it cannot read. A denied target answers exactly like a missing one.
  - About 5% are `RefineRequired`. These are the administrator's Q4 list: ordering by level reaches the
    probe budget before seven arcane rows appear among the many level-1 subjects.
  - One query was `DataChanged`: it straddled a rebuild's flip.
  - Readers never scanned the sealed partitions they cannot see. The public and secret readers' hot-target
    queries returned pages, not refinements.
- **Game commands are untouched by semantic query load.** Command p50 stays at 2.5–2.9 ms on fixtures at
  every rate and size. p95 and p99 are set by @set, the only writing command:
  - On the disposable server, look, say and pose have p99 under 9 ms, while @set's p99 is 113–137 ms.
  - The writer's hold p95 there is at most 75 ms (25 ms at p50). That is a full-sync commit, unrelated to
    the semantic work: wiki reads are off on that server.
- **The writer is rarely contended.** Writer wait p99 is 0–13 ms. The higher values come in the warm
  passes, which run a rebuild. Hold p99 is 80–285 ms, highest in the cold passes. Read transactions are
  short (p99 under 14 ms), and at most 2–4 were open at once.
- **Rebuilds keep up with live writes.** At 1× a rebuild flipped 4,713 subjects in 2.4–4.4 s (1,064–1,987
  per second). At 10× it flipped 47,113 subjects in 36–49 s (972–1,309 per second). Every warm-pass rebuild
  flipped, and queries and commands continued through it.
- **Memory and GC:**
  - Peak working set is 400–510 MiB at 1× and 470–750 MiB at 10×. The higher figures are in warm passes,
    which include a rebuild.
  - Major faults are close to zero.
  - GC pause per 10-minute pass grows with query volume, from about 0.4 s at R=0 to about 3 s at R=50.
    Allocation grows with it, to about 71 GiB per pass at 1× and 58 GiB at 10× at R=50. That is about
    2–2.4 MiB per query, which a later step could cut.

## Defects the measurements found

- **A rebuild could not validate a world of 47,111 pages.** The reference evaluation ran every subject
  through one work, whose working-memory reservations (about 20 KB per subject) accumulated. It failed
  with `NativeReadLimitException: working-bytes`, and the warm passes of that first 10× attempt lost their
  results. That attempt's cold passes are kept as `pf2e-10x-attempt1-*`. Fixed in `eeb3809005`: each
  subject is now evaluated under its own bounded work, with the per-subject working memory an option
  (`ValidationWorkingBytes`). It is covered by `ValidationBoundsEachSubjectRatherThanTheWholeWorld`, which
  fails with the old evaluation. The 10× results above are the rerun.
- **Activation bootstraps at most 128 existing pages.** That is the activation certificate's count bound,
  a prototype limit. Fixture worlds are therefore activated first, and every dataset page is then created
  through the integrated commit path, as an edit on an active world is.

## Not measured

- **100×.** Loading 471,101 pages, then 1,518,500 facts, through the integrated commit path takes 8–10 ms
  per page for each step. That is about 3.5 hours before measuring can start, and it exceeded the 3-hour
  limit the build process ran under. At the limit the host began shutting down and the next import was
  refused, as a closing fixture should refuse. The world was removed. A rerun needs a longer build limit,
  or a bulk loader, and about 5 hours.
- **Native reader slots** are reported as the store's own count of open read transactions, not as LMDB's
  reader table.

## Limits, revised from this evidence

- **The rebuild's per-subject validation budget defaults to 32 MiB.** Its peak per subject is a transient
  native range batch, so 2 MiB per subject suffices at these sizes. The default leaves headroom for
  subjects with many requirement rows.
- **The query budgets** (4,096 candidate probes, 8,192 denied skips) produce honest `RefineRequired`
  answers for the administrator's Q4 at 10×. They are not raised: at the sizes measured, answering those
  queries really does need more than that.
- No query or command latency bound is claimed beyond the rates and sizes in the tables above.
