# Detached WebSocket Sessions (true session pinning) — Design

**Date:** 2026-07-01
**Branch:** `spike/webtransport`
**Status:** Design approved; pending spec review → implementation plan

## Goal

Make a SharpMUSH **WebSocket** play session survive a network switch / brief drop **without logging
the character out** — true session pinning, not just output replay. When the socket drops, keep the
session alive for a short grace window; when the client reconnects within it, rebind the new socket to
the *same* session so the character never left (queues keep running, `@adisconnect` does not fire).

Builds on the existing always-on sequencing + durable NATS replay (resume token + JetStream buffer).

### Scope / non-goals

- **WebSocket only.** Telnet uses `TelnetServer`, not `ConnectionPump`, and keeps hard-disconnect.
- **Single ConnectionServer instance (or sticky reconnect).** The detached session lives in one
  instance's memory; a reconnect must reach the same instance. Cross-instance pinning is out of scope.
- **Zero engine changes.** Pinning is achieved by the ConnectionServer withholding
  `ConnectionClosedMessage` during grace and rebinding to the same handle number.
- Token re-mint-on-login hardening is noted as a follow-up, not built in v1.

## Key enabling fact

The engine's `ConnectionClosedConsumer` (`SharpMUSH.Server/Consumers/InputMessageConsumers.cs`) calls
`connectionService.Disconnect(handle)` — that is what logs the character out and fires disconnect
attributes. If the ConnectionServer does **not** publish `ConnectionClosedMessage` during the grace
window and keeps the handle registered, the engine keeps the character connected. On reconnect, if the
ConnectionServer rebinds to the **same handle number**, the engine sees an uninterrupted session and
never re-fires connect attributes (no new `ConnectionEstablishedMessage`).

## Architecture (Approach A: mutable per-handle output sink + grace timer)

Today, handle-lifetime = connection-lifetime (`ConnectionPump` `finally` → `DisconnectAsync`). This
design decouples them: the session (handle) outlives the connection, and its output has a swappable sink.

### Components

**`SessionSink`** (new, `ConnectionServer.Services`)
: A per-handle mutable holder of the current transport.
```csharp
public sealed class SessionSink
{
    private volatile IDuplexTransport? _current;
    public IDuplexTransport? Current => _current;
    public void Attach(IDuplexTransport transport) => _current = transport;
    public void Detach() => _current = null;
}
```
The output delegate registered with `ConnectionServerService` closes over the sink + replay store:
append to replay (always, so detached output is buffered) → if `sink.Current` is non-null, send to it.

**`ConnectionServerService`** (extend)
: Keep the handle in `_sessionState` across a drop. Add:
- `void Detach(long handle, Func<Task> onGraceExpired, TimeSpan grace)` — mark the handle detached and
  start a one-shot grace timer that invokes `onGraceExpired` (the real disconnect) if not cancelled.
- `bool Reattach(long handle)` — cancel the grace timer, mark attached; returns false if the handle is
  gone (grace already expired).
- A per-handle detached-state + `CancellationTokenSource`/timer map. `DisconnectAsync` (the real one)
  cancels any pending grace timer.

**`ConnectionPump`** (extend): see protocol below. On loop exit (transport dropped) it calls the new
detach path instead of `DisconnectAsync`.

**`SessionSinkRegistry`** (new, or a `ConcurrentDictionary<long, SessionSink>` owned by the pump's DI
scope): maps handle → sink so a reconnecting pump can find and rebind the existing sink. Singleton.

### Connection protocol — mandatory first frame

The client always sends a first frame:
- Fresh connect → `{"hello":1}`
- Reconnect (holds a token) → `{"resume":"<token>","lastSeq":n}`

Pump decision on the first frame (a candidate handle was allocated at accept):

1. **`resume` → token resolves to a live *detached* handle** (within grace): **REBIND** —
   `Reattach(oldHandle)`, `sink.Attach(thisTransport)`, replay `AfterAsync(oldHandle, lastSeq)` to this
   transport, release the candidate handle, drive the loop for `oldHandle`. No `RegisterAsync`.
2. **`resume` → handle gone / unknown**: register the candidate handle fresh; if the durable buffer
   still has frames, replay `AfterAsync` into it (existing late-reconnect behavior). Mint + send a new
   resume token.
3. **`hello`** (or any non-resume first frame): register the candidate handle fresh; mint + send token.
4. **`resume` → handle currently *attached*** (connection-steal): treat as rebind — attach the sink to
   the newcomer and close the previous transport (last write wins).

On drop (receive returns null / socket error), the pump calls
`Detach(handle, onGraceExpired: () => DisconnectAsync(handle), grace)` and `sink.Detach()` — instead of
`DisconnectAsync`.

**Server → client acknowledgement (so the client knows whether to log in):**
- Rebind (case 1/4) → send `{"reattached":true}` (no new token; the existing session continues).
- Fresh register (case 2/3) → send `{"resumeToken":"<token>"}` as today.

The client, on reconnect, sends its resume frame first and then waits for this ack: `reattached` →
skip the OTT login (already authenticated); a new `resumeToken` → run the normal login flow.

### The three reconnection tiers

| Reconnect timing | Behavior |
|---|---|
| `< GraceSeconds` (default 120) | Rebind to the live detached session — **true pinning**, no logout |
| `Grace < t < RetentionHours` (24h) | Old handle logged out at grace expiry; fresh login; recent output replayed from the durable buffer |
| `> RetentionHours` | Fresh session; nothing to replay |

`Session:GraceSeconds` (new, default 120) is separate from `Replay:RetentionHours` (24). At grace
expiry the session is disconnected normally but the durable buffer is left to age out per retention, so
a late reconnect still replays into a fresh session.

## Auth / security

The 128-bit resume token is the reattach credential, delivered only over the client's own TLS
WebSocket (threat model = a session cookie). **Follow-up hardening (not v1):** re-mint the token bound
to the player immediately after login, invalidating the pre-auth token — deferred because it needs a
login-completion signal into the ConnectionServer that does not exist cleanly today.

## Error handling / edges

- Grace expiry, no reconnect → normal logout via the existing `DisconnectAsync` path.
- Connection-steal (token used while the handle is still attached) → rebind to the newcomer, close the
  stale transport.
- Output while detached → buffered in the replay store, delivered on reattach.
- Server shutdown while sessions are detached → grace timers fire or are cancelled on dispose; handles
  clean up via the existing shutdown path.

## Testing

- **Unit:** `SessionSink` routing (attached sends + buffers; detached buffers only). `ConnectionServerService`
  `Detach` → grace expiry via an injected clock/timer → `DisconnectAsync` fires exactly once; `Reattach`
  cancels the timer. `ConnectionPump` first-frame decision: resume-to-live → reattach the same handle
  with no new `RegisterAsync`; resume-to-dead → fresh register; `hello` → fresh register;
  connection-steal → sink points at newcomer, old transport closed.
- **Live (podman NATS):** the durable-replay path stays covered by `JetStreamReplayIntegrationTests`.
  The grace/rebind logic is in-memory (single instance) and unit-tested with fakes + a fake clock.

## Client changes

- Send the mandatory first frame (`{"hello":1}` fresh, `{"resume",…}` on reconnect) — extends the
  existing resume wiring in `WebSocketClientService`.
- No re-login on a within-grace reconnect: the client, on reconnect, sends the resume frame and treats a
  successful reattach as already-authenticated (skips the OTT login it would otherwise run).

## Files (anticipated)

- Create: `SharpMUSH.ConnectionServer/Services/SessionSink.cs`, `SessionSinkRegistry.cs`
- Modify: `Services/ConnectionServerService.cs` (detach/reattach + grace timer),
  `ProtocolHandlers/ConnectionPump.cs` (first-frame decision, detach-on-drop),
  `Program.cs` (`Session:GraceSeconds`, registry DI)
- Modify (client): `Services/WebSocketClientService.cs` (hello/resume first frame, skip re-login on reattach)
- Tests: `Tests/ConnectionServer/SessionSinkTests.cs`, `DetachedSessionTests.cs`, extend `ConnectionPumpTests`
