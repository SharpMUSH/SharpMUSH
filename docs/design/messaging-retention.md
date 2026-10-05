# Messaging retention, budgets and delivery

How long each kind of message lives on the broker, what bounds the space it takes, and what happens
when a handler fails. Binding for `SharpMUSH.Messaging/NATS` and the ConnectionServer's replay store.
Issues: #1455 (byte budgets), #1456 (transport vs replay retention), #1457 (retry and ACK
semantics), #1461 (bounded replay reads).

## Three stores, three jobs

| Store | Written by | Read by | Job | Retention | Full |
|---|---|---|---|---|---|
| `SHARPMUSH-CS` | ConnectionServer | engine (`Server`) | queue of client input and connection events | interest, `SHARPMUSH_NATS_MAX_AGE` (1h) | discard-new: the next publication is refused |
| `SHARPMUSH-MS` | engine | ConnectionServer | queue of output and session control | interest, `SHARPMUSH_NATS_MAX_AGE` (1h) | discard-new |
| `TERMINAL_REPLAY_V2` | ConnectionServer | ConnectionServer, on reconnect | archive of rendered browser frames | limits, `Replay:RetentionHours` (24h); purged at session end | discard-old: the oldest frames go |

The bus streams are queues, not archives. Interest retention removes a message once every consumer
that wants it has acknowledged it, so a bus stream's size is the unprocessed backlog: zero while the
consumers keep up. A subject no consumer wants is not retained at all — that is how transient
notifications behave. Browser replay is the only archive, kept by the store built for it, so the
bus no longer keeps a second, 24-hour copy of everything it carried.

Discard policy follows what the data is. On a bus stream the oldest message is work nobody has done
yet (a command, an output frame), so a full stream refuses the newest publication instead of
silently deleting it; the publisher sees a `NatsJSApiException` and the refusal is counted. On the
replay stream the oldest frames are the least useful, so they go first; a reconnect that needs one is
told its history is incomplete (below).

WorkQueue retention was not used: it cannot be reached by updating an existing stream, and interest
retention gives the same "acknowledged means gone" result for these single-consumer subjects.

## Budgets

| Budget | Setting | Default |
|---|---|---|
| Each bus stream | `SHARPMUSH_NATS_MAX_BYTES` (bytes) | 512 MiB |
| Bus backlog age | `SHARPMUSH_NATS_MAX_AGE` (`30m`, `1h`, seconds) | 1h |
| Replay stream | `Replay:MaxBytes` (`Replay__MaxBytes`) | 2 GiB |
| Replay age | `Replay:RetentionHours` | 24 |
| Frames one reconnect may replay | `Replay:MaxFrames`, 0 for none | 10 000 |
| JetStream on the server | `jetstream { max_file_store }` in `nats.conf` | 6 GB |

JetStream reserves each stream's `MaxBytes` against `max_file_store` when the stream is created, so
the server budget must exceed the sum of the stream budgets (about 3 GiB by default) or creation
fails with "insufficient storage resources". Left unset, `max_file_store` is 75% of the disk free at
startup; the test and dev containers set it explicitly so a nearly full disk does not break them.

`NatsOptions.Validate` rejects a missing or contradictory budget (no byte budget, a message limit
larger than the stream) at startup, and an unreadable environment value stops startup rather than
falling back to a budget nobody chose.

Only a stream's publisher reconciles its configuration (`NatsStreamPolicy.ApplyAsync`). The
consuming process creates the stream from the same definition when it is missing and otherwise
leaves it alone (`EnsureExistsAsync`), so the two processes cannot undo each other's settings on
restart. Change a budget through the settings above: the owning process re-applies them on its next
start, so an edit made with `nats stream edit` lasts only until then.

## Message families

| Family | Messages | Lifetime need | After a consumer outage |
|---|---|---|---|
| Client input | `TelnetInput`, `WebSocketInput` | until the engine queues it | delivered on restart if still within the backlog age |
| Connection metadata | `NAWSUpdate`, `GMCPSignal`, `MSDPUpdate`, `*Negotiated` | until applied; idempotent | delivered on restart; reapplying is harmless |
| Connection lifecycle | `ConnectionEstablished`, `ConnectionClosed`, `SessionResume*`, `SessionResumed` | until applied | delivered on restart; the engine checks the session incarnation |
| Output | `TelnetOutput`, `*Prompt`, `MarkupOutput`, `WebSocketOutput`, `GMCPOutput`, `Broadcast` | until sent to the socket, or the connection is gone | delivered on restart; output for a connection that no longer exists is dropped by the handler, deliberately |
| Engine control | `DisconnectConnection`, `UpdatePlayerPreferences`, `ClearPlayerOutputPreferences`, `UpdateColorStyle`, `MainProcessReady`, `MainProcessShutdown` | until applied | delivered on restart |
| Browser replay | rendered frames | until the session ends or the retention age | replayed after the client's last sequence |

A consumer identity created for the first time (`DeliverPolicy.New`) starts at the next message: a
new durable name must not run a backlog of commands meant for a consumer that no longer exists. An
existing durable consumer keeps its position, so a restart resumes where it stopped.

## Handler failures

`NatsJetStreamConsumerService.HandleDeliveryAsync` decides each delivery:

| Case | Outcome |
|---|---|
| Handler returns | ACK |
| Payload empty or not the registered type | terminate (`AckTerminate`), never run |
| Handler throws `RetryableMessageException` | retried in place with doubling backoff (`HandlerRetryDelay`, 250 ms) up to `HandlerMaxAttempts` (3) runs, sending in-progress so the broker does not redeliver meanwhile; then terminate |
| Handler throws anything else | terminate after one run |
| A sequence this process already finished arrives again | ACK without running the handler |

Retrying is opt-in. Only the handler knows whether it failed before doing anything; a handler that
may have queued a command or sent output must not be run again, so an ordinary exception is
terminal. Retries happen in place rather than through a delayed NAK so later messages on the subject
stay behind the failing one and keep their order, and the attempt cap bounds how long one message
can hold its subject. A terminated message is removed from an interest stream and never redelivered.

## Durability boundaries

- **ACK means handled, not executed.** The input consumers hand a command to the in-memory scheduler
  and return; the ACK follows. If the engine process dies after that, the queued command is lost with
  the scheduler — the bus does not cover it.
- **Process loss before the ACK** leads to redelivery after `AckWait` (30 s), up to `MaxDeliver` (5)
  deliveries in all. The handler runs again, so a message whose handler had finished but whose ACK
  was never sent is handled twice. That window is the time between a handler returning and its ACK
  reaching the broker.
- **A lost ACK on a live process** (broker reconnect) is recognised: the consumer remembers the last
  4 096 stream sequences it finished per durable consumer and acknowledges a redelivery of one without
  running the handler. The memory is per process, so it does not span a restart.
- **A full bus stream** refuses new work. The publisher sees the failure; nothing already queued is
  lost.

## Browser replay reads

A reconnect opens the replay (`ITerminalReplayStore.OpenAsync`) before its resume token is consumed,
and gets either the frames or an `IncompleteReplay` with the reason:

- **Expired** — the client's last frame is no longer retained. The read starts at that frame, and a
  session's frames leave the stream oldest first (age, byte budget, purge), so finding it proves every
  later one is still there. A client with no frames yet (`lastSeq` 0) has no anchor to check.
- **OverBudget** — more frames are pending than `Replay:MaxFrames`.

Either way the client starts a fresh session rather than resuming with a silent gap.

The frames are read from the broker `PageSize` (64) at a time and each page is sent before the next is
fetched, so a replay holds at most one page of frames in memory however long the history, and
concurrent reconnects each hold their own page. The count is fixed when the replay opens, so a busy
session cannot make it endless. The resume holds the session's `OutputGate` for the whole replay, so
live output waits behind it and cannot overtake. If retained frames vanish while a replay is being
sent, reading throws `ReplayInterruptedException`: the resume fails without issuing a new token and
the socket is closed, so the client's next reconnect learns the history is gone.

## Observability

On the `SharpMUSH` meter (Prometheus `/metrics` on both hosts):

| Instrument | What |
|---|---|
| `sharpmush.messaging.publish.rejected` | refused or unconfirmed publications, by `stream` and `reason` (`full`, `timeout`, `error-<code>`) |
| `sharpmush.messaging.handler.retries` | in-place retries, by `consumer` |
| `sharpmush.messaging.handler.terminal_failures` | terminated messages, by `consumer` and `reason` |
| `sharpmush.messaging.handler.duplicates` | redeliveries acknowledged without running again |
| `sharpmush.messaging.stream.bytes` / `.messages` / `.max_bytes` | per stream, every `MonitorInterval` (30 s) |
| `sharpmush.messaging.consumer.pending` / `.ack_pending` / `.redelivered` | per durable consumer |
| `sharpmush.messaging.jetstream.storage.used` / `.available` | the account's file storage against `max_file_store` |

`NatsBrokerMonitor` takes the readings and logs a warning when a stream or the storage budget is
four-fifths full. The host's own disk free space is the operator's to watch; NATS reports its
reserved and used storage at `/jsz` on the monitoring port.
