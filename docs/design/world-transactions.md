# World Transactions

**Status:** Proposed only. Deferred and not scheduled: this records the design so it is not
lost, and is not a plan of work. Nothing here is built. Package operations use the compensation log
(`PackageWriteTransaction`) until this is taken up. If it is ever accepted, it amends
`engine-data-trunk.md` §1 (cache policy) and §8 (one engine process).

Package operations are atomic today only by compensation (`PackageWriteTransaction`). Each write
records its inverse, and a failure replays the inverses. That covers failures the process lives
through. It does not cover a process that dies part way through, and it does not isolate: other
code sees the half-applied state while the operation runs. This document proposes a real
transaction, a *world transaction*, whose writes all land in one provider commit or not at all.
It keeps every behaviour a PennMUSH game can observe.

## 1. The compatibility contract

PennMUSH has no transactions. What it guarantees comes from being single-threaded:

| | PennMUSH guarantee | Where |
|---|---|---|
| **P1** | One queue entry runs at a time, and nothing interleaves with it. Socket input and HTTP handler commands are queue entries too. | `cque.c` `do_top`, `run_user_input`, `run_http_command` |
| **P2** | A write is visible at once: to the rest of its entry, and to every later entry. | the database is memory |
| **P3** | Nothing is rolled back. An entry that fails part way keeps what it already did. `@break` stops the entry; it does not undo it. | no rollback exists |
| **P4** | What is persisted is always a state *between* two entries. A dump is a forked copy-on-write snapshot, or taken with the game paused, and is written to a temp file and renamed into place. A crash loses everything since the last dump, but never half an entry. | `game.c` `fork_and_dump`, `dump_database_internal` |

Where SharpMUSH stands:

- **P1** holds for the command queue, which has a single consumer (`TaskScheduler.ProcessQueueAsync`).
  It does not hold for HTTP handler commands, which `HttpHandlerCommandService` evaluates on the
  request thread (`CommandListParse`, line 120). Portal writes and package operations don't hold
  it either. All three can interleave with a running queue entry.
- **P2** and **P3** hold.
- **P4** differs in both directions. Every write is durable on its own, which is stronger than
  Penn's hourly dump. But a crash part way through an entry persists half of it, which Penn never
  does.

So softcode keeps P2 and P3 unconditionally. A world transaction is an engine facility, for
operations Penn has no equivalent of: package apply, rollback and uninstall, and imports.
Softcode never gets an implicit rollback. An opt-in atomic block for softcode is deliberately out
of scope (§8).

## 2. The shape

```csharp
await using var tx = await worldTransactions.BeginAsync(WorldTransactionOptions.Package, ct);
// ... ordinary Mediator writes and reads: no new parameters anywhere ...
await tx.CommitAsync(ct);   // one provider commit; then cache invalidation; then the outbox
// Disposing an uncommitted transaction aborts it: the provider discards, and nothing else happened.
```

`IWorldTransactionFactory` and `IWorldTransaction` live in `SharpMUSH.Library/Stores`. The open
transaction is *ambient*: an `AsyncLocal` carries it. Handlers, stores and services enlist without
any signature changing, the same way `ExecutionBudget` already flows today. A provider enlists by
checking the ambient transaction. It receives an opaque provider-owned handle, never the Mediator
or the cache, so §3 of the data trunk ("providers know nothing above them") holds.

A world transaction needs four things. Each has a section below: exclusion (§3), a provider
transaction (§4), a cache that sees only committed state (§5), and an outbox for side effects
(§6).

## 3. Exclusion: the world gate

A provider transaction isolates *storage*. It does not stop another writer from computing on the
old state and writing afterwards: a lost update. Penn gets exclusion for free (P1). SharpMUSH
needs it stated:

- **`IWorldGate`** is one async exclusive lock per engine process. It is held for the duration of
  a queue entry, an HTTP handler invocation, a portal write request, and a world transaction.
  Reads outside the gate are fine: they see the last commit (§4).
- **Queue entries** already run one at a time, so for them the gate costs one uncontended acquire.
- **HTTP handler commands** should become queue entries, as they are in Penn (`run_http_command`).
  That is P1 parity and worth doing on its own, before any of the rest (§7, phase 0).
- **A world transaction** holds the gate from `BeginAsync` to commit or abort. While it runs, the
  game waits. That is exactly what Penn does during a long command or a non-forking dump. The wait
  is bounded by `WorldTransactionOptions.Budget`, the same idea as `QueueEntryCpuTime`, and an
  expired budget aborts the transaction.

Every writer must take the gate, not just most of them. A write that skips it can deadlock
against an open Lightning session (§4), so the gate is enforced at the Mediator: a write command
outside both the gate and a transaction fails in debug builds.

## 4. The Lightning transaction

Lightning is the only storage engine; SurrealDB support was dropped (#1182). A provider added
later has to supply a transaction with the same semantics before it can support world
transactions.

Writes today are synchronous jobs on one writer thread (`LightningWriter`), group-committed, with
each job in a nested child transaction under one parent (`LightningStore.WriteBatch`). An LMDB
write transaction belongs to its thread, so it cannot stay open across the `await`s of an
operation. The writer thread has to own it instead:

- **`BeginSession`** queues a solo job. That job opens a parent write transaction and then serves
  the session's own channel until commit or abort. Each session job runs in a nested child
  transaction, as batch items already do, so a failed statement aborts only itself. That is P3
  inside the transaction.
- **Reads inside the session** go to the writer thread and run in the session's transaction,
  because only there are its own writes visible (P2). `LightningStore.Read` checks the ambient
  transaction and routes accordingly. Reads outside it keep running on read-only transactions and
  see the last commit, which is LMDB's MVCC. That gives isolation with no extra mechanism.
- **Commit** is the parent's `Commit`: one sync under the configured `SHARPMUSH_LIGHTNING_SYNC`
  mode. **Abort** disposes the parent. An uncommitted session never reaches the file, so a crash
  mid-transaction loses the transaction whole.
- Ordinary write jobs wait in the main channel while a session is open. Because every writer holds
  the gate (§3), none should be there.

## 5. The cache

This is the part that breaks first. The data trunk (§1) keeps one shared cache, and one shared
instance of each object. Handlers mutate that instance in place: `SetLockCommandHandler` calls
`target.Object().WithLock(...)` and `request.Target.WithLock(...)`. Three rules follow.

1. **Queries inside a transaction bypass the shared cache.** `QueryCachingBehavior` and
   `StreamQueryCachingBehavior` see the ambient transaction and call the handler directly, storing
   nothing. Otherwise uncommitted state would reach other readers (a dirty read), and an abort
   would leave phantom entries behind.
2. **Invalidation inside a transaction is recorded, not performed.** `CacheInvalidationBehavior`
   adds the keys, tags and result keys to the transaction's invalidation set. Until commit, the
   shared entries still describe committed state, which is correct for every reader outside. On
   commit, the set is applied *after* the provider commits, bumping `ObjectVersions` exactly as the
   post-handler pass does today (§7 of the data trunk). On abort there is nothing to undo, because
   the shared cache never saw the transaction.
3. **A transaction writes only objects it loaded itself.** Rule 1 means objects loaded inside the
   transaction are private instances, so the in-place `With…` mutations stay private. An instance
   taken from the shared cache *before* the transaction must not be written through. A debug
   assertion catches that: a write command whose target is the cached instance.

`AsyncRelation` and `IObjectRelationLoader` resolve through the Mediator's queries, so rule 1
covers them without further work.

## 6. The outbox

A write can have effects outside the process: a notification sent to a connection, a NATS
publish, a queue entry submitted (`@trigger`, `@wait`), a lifecycle hook. Sent inside a
transaction that later aborts, those would announce something that never happened. Penn cannot
have this problem, because it never rolls back.

- Inside a world transaction those effects go to `tx.Outbox` and are released in order after the
  commit and the cache invalidation. An abort drops them.
- `INotifyService`, the NATS publisher and `ITaskScheduler` check the ambient transaction. Those
  are the known places; phase 1 starts with an audit for any others.
- Package lifecycle hooks (`AINSTALL`/`AUPDATE`) already run after the commit. They stay as they
  are.

## 7. Phases

| Phase | Scope | Value on its own |
|---|---|---|
| **0** | HTTP handler commands become queue entries. Introduce `IWorldGate`, and have portal writes and package operations take it. | P1 parity with Penn. Package operations stop interleaving with play. |
| **1** | The `IWorldTransaction` seam, the Lightning session (§4), cache deferral (§5) and the outbox (§6). Package apply, rollback and uninstall run in a world transaction, and `PackageWriteTransaction` is retired. | Crash-safe, isolated package operations. |
| **2** *(optional)* | Run every queue entry in a world transaction that **always commits**: at completion, and on error too, because P3 says an error keeps its effects. Only a crash or a killed process discards it. | P4 parity: what is persisted is always a state between two entries, as a Penn dump is. The cost is that entry reads go to the writer thread, so it would be a configuration switch, measured before it is ever the default. |

## 8. Out of scope

- **Rollback for softcode.** Penn has none (P3), and games are written assuming effects stick. An
  opt-in `@atomic` block would have to hold back `@pemit` output until commit, which changes
  output order in ways no Penn game expects. It is not proposed.
- **More than one engine process.** A world transaction assumes one gate, the same assumption as
  §8 of the data trunk. A backplane would need a distributed lock as well, which is out of scope.

## 9. Risks and open questions

- **`AsyncLocal` leaks into background work.** A `Task.Run` or fire-and-forget started inside a
  transaction inherits it, and could enlist after the commit, or deadlock on the writer thread.
  The session handle must refuse use once it is closed. The known boundaries, such as the
  scheduler's consumer, already suppress flow (`TaskScheduler.EnsureConsumerStarted`), and the
  rest need auditing.
- **Deadlock through a path that skips the gate.** A write that reaches the Lightning writer
  without the ambient transaction waits behind the open session forever. §3's debug enforcement
  and the budget's abort are the mitigations. The first real use should run under a watchdog.
- **Stalls.** A long transaction pauses the game, as a long Penn command does. The budget bounds
  it, and package operations report their duration.
- **Reads hop threads under Lightning.** That is fine for package operations. It is the open cost
  of phase 2, and the reason phase 2 is optional.

## Tests the design needs

- **Crash.** A Lightning session with writes that is never committed: after the environment is
  reopened, none of the writes exist.
- **Isolation.** A reader outside an open transaction sees the committed state, and the shared
  cache holds no uncommitted entry. After commit, the reader sees the new state.
- **Abort.** After an abort, the shared cache and `ObjectVersions` are exactly as they were, and
  the outbox sent nothing.
- **Gate.** A queue entry submitted during a transaction runs after it, and sees its writes.
- **Penn parity.** An HTTP handler command and a queue entry never interleave (phase 0). With
  phase 2 enabled, killing the process mid-entry leaves the entry's state entirely absent.
