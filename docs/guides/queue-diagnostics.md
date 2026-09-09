# Queue diagnostics and short profiling sessions

`@ps/history [limit]` shows recent queue outcomes, and `@profile/start [seconds]`,
`@profile`, and `@profile/stop` control an opt-in invocation profile. The portal's
**Queue diagnostics** page provides active entries, paged recent outcomes and the
same profile. Select a linked character explicitly before loading portal data.
The default history page has 50 entries (maximum 100); the default profile lasts
60 seconds (range 1–300).

## Authority and privacy

Both game commands and HTTP endpoints use `IQueueDiagnosticsService`, backed by
`IQueueControlService`'s current inspection scope. The active character must still
be linked to the authenticated account, and its full object identity must match.
Own-resource inspection requires `queue.inspect.own`, current source ownership,
and normal object control. An explicit own-scope denial cannot fall back to the
global `queue.inspect` grant. Global inspection can inspect other owners' metadata,
including history whose source has since been deleted. Profiling additionally
requires `diagnostics.profile`.

The collector refreshes authority before accepting each bounded batch, and views
recheck source authority before returning rows. Lost authority discards the
profile. Raw commands, attribute values, arguments, registers, results and exception
messages are never recorded. Source attribute names are included only when the
scheduler can attribute the work to the executing object; otherwise attribution is
unknown. Arbitrary queue groups are reduced to a closed kind such as `semaphore`
or `other`. The profile's pre-authorization mailbox loss count is not returned,
since that count could expose activity outside the viewer's authority.

## Timing and limits

Queue wait time runs from admission until body execution starts. Execution time
runs until the body finishes, fails, is cancelled or reaches its execution budget.
Both use a monotonic clock; UTC timestamps are labels for display. Neither timing
is CPU consumption. Invocation timings reuse the existing telemetry hooks and are
inclusive elapsed durations: nested calls and awaited work can overlap, so their
sum must not be added to queue execution time.

History holds at most 1,024 completed/rejected entries for 15 minutes. It is
process-local and clears on restart. A profile has at most 256 aggregation keys;
all profiles share a 4,096-sample mailbox. Extra samples or keys are omitted.
There is one profile per account and at most eight per process. Starting a new
profile replaces that account's previous profile. Recording stops automatically
at its deadline; stopped results expire after 15 minutes and may be evicted sooner
to admit another profile. These bounds also apply under sustained input.

Instrumentation stays in `TelemetryService`; the observer does not add a second
parser visitor. The scheduler owns each live observation, avoiding a separate
unbounded registry. Its execution path records metadata and attempts bounded
mailbox writes. Permission/database work happens in the asynchronous collector,
which checks at most one source/owner pair per profile batch. Collector work and
HTTP/game reads honor cancellation.

## Validation and overhead measurement

The regression suites cover metadata attribution, closed failures/rejections,
monotonic timing, bounded retention and cardinality, permission revocation,
pagination after filtering, game/provider parity, actual portal rendering,
cancellation and unchanged telemetry exporters. Scheduler regressions verify
FIFO side effects and semaphore bookkeeping after a ready timeout is halted.

`QueueDiagnosticsSchedulerTests.RepresentativeQueueOverheadPreservesEveryInvocationAndSideEffect`
compares disabled observation, history recording and an active profile. Each mode
executes 5,000 FIFO entries with three existing telemetry invocations per entry.
It verifies every admission, execution order and side effect, plus each retained
entry's invocation count and outcome. Each mode has a warm-up and the first full
round is excluded from reported comparisons. Subsequent rounds report elapsed
milliseconds and process-wide managed allocation deltas.

Run it in an otherwise idle test process with the pinned SDK:

```sh
SHARPMUSH_DATABASE_PROVIDER=lightning dotnet run --project SharpMUSH.Tests --no-build -- \
  --treenode-filter '/*/*/QueueDiagnosticsSchedulerTests/RepresentativeQueueOverheadPreservesEveryInvocationAndSideEffect' \
  --output Detailed
```

This is a scheduler/observer microbenchmark, not a throughput guarantee. It excludes
asynchronous permission/database collection and network rendering. The active-profile
case includes mailbox saturation during the burst, so it is not a claim that every
invocation is retained. Process-wide
allocation measurements and wall-clock time remain sensitive to runtime and host
activity. Measure representative game workloads before choosing operational
profiling frequency.

### Local measurement

On Linux x64 with SDK 10.0.400 and .NET 10.0.11, the three post-warm-up
rounds produced the following results for 5,000 entries per mode:

| Mode | Median elapsed | Elapsed range | Median process allocation |
| --- | ---: | ---: | ---: |
| disabled | 140.19 ms | 76.58–189.15 ms | 28.25 MB |
| history | 135.43 ms | 125.52–205.18 ms | 35.50 MB |
| profile | 238.64 ms | 163.67–267.91 ms | 78.10 MB |

All four rounds preserved every FIFO side effect; history/profile rounds also
verified three invocations and a completed outcome for every retained entry.
The shared host was running other validation work. The overlapping elapsed ranges
(and history median below the disabled median) mean this run does not establish a
reliable latency percentage. Allocation figures cover the whole test process, not
retained diagnostic memory. The separate capacity tests establish retention bounds.
