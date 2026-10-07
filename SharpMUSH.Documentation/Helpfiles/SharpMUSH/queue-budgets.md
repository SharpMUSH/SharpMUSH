<!-- help-article
{
  "corpus": "help",
  "id": "queue-budgets",
  "lookup": "queue budgets",
  "aliases": [],
  "sections": [
    {
      "id": "queue-limits",
      "heading": "Queue limits",
      "lookup": "queue budgets queue limits"
    },
    {
      "id": "typed-input-limits",
      "heading": "Typed input limits",
      "lookup": "queue budgets typed input limits"
    },
    {
      "id": "admission-rejection",
      "heading": "Admission rejection",
      "lookup": "queue budgets admission rejection"
    },
    {
      "id": "diagnostics",
      "heading": "Diagnostics",
      "lookup": "queue budgets diagnostics"
    }
  ]
}
-->
# Queue Budgets

Every admitted command occupies one slot until it finishes. Immediate commands,
@wait delays, semaphore waiters, and asynchronous attribute callbacks share the
same limits. Running work still occupies a slot. Cancellation retains a slot
until the immediate consumer removes the cancelled entry, preventing repeated
queue-and-cancel operations from retaining unlimited command bodies.

## Queue limits

`global_queue_limit` defaults to 10000 admitted jobs. `player_queue_limit` defaults
to 100 and applies to the executor's owner, across that owner's objects. Wizards
and executors with Queue power add the current database object count (including
garbage) to that owner allowance. The global ceiling still applies. Host
callbacks submitted without an executor or connection share the `system` bucket,
which also obeys `player_queue_limit`. Use the executor-aware admission overload
for player-owned work; omitting an actor never bypasses the owner cap.
Reducing a limit does not discard existing work; new submissions are rejected
until usage falls below the limit. Capacity rejection never waits for room in
the queue, including when a running command submits another command.

## Typed input limits

A line a player types is not part of `player_queue_limit`, as it is not part of
PennMUSH's: `run_user_input` hands its `QUEUE_SOCKET` entry straight to
`do_entry`, so it never reaches the tally `queue_limit` reads. A full owner queue
therefore never stops its owner typing, and a typed line is never the reason
another of that owner's commands is refused or its object halted as a runaway.
What bounds typed input instead is the connection it arrived on:
`command_burst_size` (default 100, PennMUSH's `COMMAND_BURST_SIZE`) typed lines
may be admitted and unfinished on one connection at a time. A running line still
occupies one of them, as it does for the other two limits. The count is per
socket, not per handle number: a reconnection on a reused handle starts with the
whole allowance, and logging in does not reset a burst already under way.
SharpMUSH applies this as a ceiling on outstanding lines rather than as
PennMUSH's replenishing per-second rate.

## Admission rejection

A rejected submission has no PID. Game users receive a queue rejection notice;
service callers receive `QueueAdmissionResult` with a reason and no PID. Reasons
are global capacity, owner capacity, connection capacity, shutdown, and a
missing executor. A delayed
or semaphore job retains its original reservation and PID when released. It
does not need to compete for capacity a second time. Immediate work is FIFO;
semaphore notifications and partial drains select ascending PIDs.

Draining removes only work still waiting on the semaphore. A timeout that has
already made a command runnable keeps its reservation until the consumer
finishes it. Partial drains subtract only removed waiters and preserve unused
notification credits; a full drain also clears unused notification credits.

If a failed semaphore submission cannot restore its counter, the cancelled PID
keeps its queue slot as a repair record. The next semaphore operation (including
`@halt/pid` of that PID) retries repair before changing any counters. A failed repair
blocks further semaphore mutations, but does not block ordinary queued commands.
Repair attempts have a one-second deadline and observe shutdown cancellation.
A conflicting raw attribute edit is preserved and reported; restore the original
counter, or remove the newly created attribute, before retrying. These repair
records are in memory; an unrepaired counter is logged if the engine shuts down.

`@notify` and `@drain` persist their counter before releasing or removing waiters.
If the provider cannot confirm whether a write committed, the selected waiters
keep their reservations until a bounded reconciliation succeeds. Later semaphore
operations retry that reconciliation first. An unchanged counter leaves waiters
waiting; the intended counter completes the original operation exactly once. A
conflicting counter or metadata edit requires administrator repair before these
operations can continue. Ordinary queued commands remain available.

## Diagnostics

Wizards can use `@ps/all` to inspect admitted totals, configured limits, and
rejection counts by reason. Counts last for the lifetime of the engine process.
The configuration interface exposes all three limits in the Limit category.
