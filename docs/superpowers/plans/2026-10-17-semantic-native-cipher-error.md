# Supported native cursor error proof implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. This is one dependent task with independent review. Existing fixture and atomic integration tasks are separate.

**Goal:** Observe an actual LMDB non-NotFound cursor error after the real bounded reader copied a row, with no partial batch or leaked reservation.

**Architecture:** A valid disposable encrypted LMDB fixture delegates normal encryption/decryption to the pinned package's real AES-GCM cipher. A supported Decrypt(false) callback on a later overflow-page read makes installed LMDB itself return CryptoFail. A test-only managed reflection bridge constructs the actual private Tx and TableSnapshot using the same live transaction/DBIs; no provider encryption feature or synthetic cursor status is added.

**Tech Stack:** Existing global.json SDK, LightningDB0.23.1/installed LMDB1.0.1 encryption API, TUnit and existing bounded child probe.

**Spec:** docs/superpowers/specs/2026-10-16-semantic-wiki-architecture-design.md; docs/superpowers/plans/2026-10-17-semantic-wiki-phase-b1.md Task2/Task5/exit criteria; docs/superpowers/reports/2026-10-17-semantic-native-foundation.md. This supplies one missing runtime witness, not overall PhaseB acceptance.

## Global constraints

- Only newly generated test-owned worlds, ordinary valid writes, supported callback failure, sequential fully closed environments. No copied production world, malformed native files, invalid/reset/disposed handles, live field replacement, native handle reflection, pointers, syscall fault or OS exhaustion.
- No public provider encryption option, production fault hook, new dependency or native replacement/shim. Reflection only accesses actual private managed Tx construction and TableSnapshot.Capture, never native handles. Fail clearly on shape change.
- Pinned package managed commit46f8ca97ef9eb992db93e493b29493179d2e5343 documents LightningCipher.Decrypt(false). Its bundled mdb_rpage_decrypt generates CryptoFail; retain exact package/native binary provenance in evidence. API/source are authoritative, not guessed signatures.
- Cipher delegates real AES-GCM for all valid operations and holds key/cipher alive until environment closure. Failure callback only records bounded in-memory counters and returns false, no throw/I/O/read reentry/budget mutation. Failed native transaction AND environment receive no further reads/retries; dispose them before a new healthy environment.
- Initial data: ordered 1-byte keys, first inline1-byte value, second8KiB value on4KiB pages; verify overflow statistics and complete control records. Fresh environment between seeding, raw control/failure, bounded failure and healthy reopen prevents warm decrypted-page cache affecting timing.
- Minimal bounded pass uses fresh zeroed NativeReadBudget, sufficient existing limits, and selected callback only when Probes>=3 and Bytes>=2. Require exact callback trace Probes==3, Bytes==2, WorkingBytes above initial range reservation: actual source copies/stores first entry before charging probe3 for Next. No previous result through that budget; reservation-isolation variants are outside this minimal proof.
- Build/test with SDK satisfying global.json, disk-backed task scratch and TMPDIR/TMP/TEMP, current isolated checkout, serialized shared outputs and explicit source freeze. No /tmp or /var/tmp. Root owns Git/build/runtime; workers source-only until released.
- Scope is qualified encrypted callback failure through actual compiled bounded reader. It proves no default unencrypted I/O failure, store scheduler/gate concurrency or every cursor direction. Gate stays OPEN until actual correct observations and independent review.

## Review focus

1. Setup/first-row/open-time failure must not masquerade as mid-iteration evidence; assert control+raw advance and exact production-budget trace.
2. A classifier-only or shim error cannot satisfy actual populated ReadRange failure; invoke the real existing IBoundedReadTx implementation.
3. Failed decrypted cache must never be retried in the same environment; explicit native lifetime scopes close before healthy reopen.
4. Exceptions must bypass successful batch publication, release working reservation to baseline, and retain actual CryptoFail code.
5. Parent process must drain owned child/stdout/stderr on every failure/timeout before cleanup; normal zero exit and healthy full reopen are required.

## Task 1: Supported cipher failure through actual bounded reader

**Own files:** tools/WikiSemantics.NativeProbe/Program.cs for narrow scenario dispatch; new focused cipher scenario/helper files inside that tool; new SharpMUSH.Tests/Wiki/Semantics/NativeSemanticCursorErrorTests.cs; focused report docs/superpowers/reports/2026-10-17-semantic-native-cipher-error.md after observed success. Preserve existing crash/persisted-flags scenarios and recovery coordinator. Production provider/Library source remains unchanged.

**Consumes:** installed EnvironmentConfiguration.Encryption, LightningCipher/AesGcmCipher and native cursor API; real LightningStore.Tx(LightningTransaction,TableSnapshot), TableSnapshot.Capture(sameTransaction,Dictionary<TableDef,LightningDatabase>); IBoundedReadTx.ReadRange; NativeReadBudget.Probes/Bytes/WorkingBytes; existing child scenario/path/watchdog and known task-drain patterns.

**Produces:** isolated child scenario cipher-cursor-error and compact JSON evidence. Test invokes actual child, observes successful control, raw native CryptoFail after a successful row, actual bounded LightningStoreException/no returned batch/exact3-2trace, reservation restoration and healthy complete reopen; normal child exit0. No new production APIs.

- [ ] Add compiling parent test requesting cipher-cursor-error using existing process patterns, strict new-owned-world/evidence paths and zero-exit/evidence expectations. No cipher implementation yet. Root normal-builds and observes existing unsupported-scenario runtime RED, not a compiler failure. Freeze for root before GREEN.
- [ ] Implement helper with explicit native scopes and real delegated AES-GCM. Seed valid data and close; healthy raw control validates both complete rows and overflow/later callback; separate raw failing pass arms only after exact first row and requires actual native Next CryptoFail. Close failed transaction/DBI/environment, no retry.
- [ ] Construct actual private managed reader with valid same-environment transaction/table through narrowly contained reflection. Use real Capture for actual flags. Never construct a second LightningStore/environment or manipulate raw handles. Configure callback to observe fresh production budget, require exact3/2trace and working-above-baseline. Call actual ReadRange once; capture actual LightningStoreException CryptoFail, assert no returned batch and WorkingBytes restored. Counter charges remain cumulative.
- [ ] Close all failed native owners before healthy full reopen with failure disabled. Verify every original key/value and normal child completion. Serialize only bounded JSON provenance/phases/counters/digests after callback; no key material or full payload dumps.
- [ ] Root normal-builds and runs focused child/test. Record every actual failure accurately: setup/first-row failure, missing callback, wrong timing, managed limit, returned data, crash, timeout or missing healthy reopen leaves gate OPEN. No repair by weakening or forging observations.
- [ ] Run affected native preparation/commit/bounded-reader/cursor/recovery regressions, formatting twice and complete CI appropriate to PR. Independent task and whole-branch review; only scoped rereview after fixes. Update existing foundation report/plan missing-proof status only after reviewed actual successful evidence, with encrypted callback qualification preserved. Keep wider PhaseB work open.

## Completion claim

Only after actual success: a deliberately failed supported page-decryption callback on a valid disposable encrypted fixture caused installed LMDB cursor advancement to return CryptoFail after the actual bounded reader copied its first row. Actual ReadRange threw, returned no partial batch, released working reservation and preserved complete data on healthy reopen. No broader default-I/O or full PhaseB guarantee follows.
