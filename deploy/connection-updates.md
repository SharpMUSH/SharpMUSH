# Connections during deployment

The stable SocketServer owns telnet and `/ws` sockets, their protocol state, and NATS subscriptions. ConnectionServer is now a replaceable rendering worker reached over HTTP/2 on the private Unix socket `/run/sharpmush/render.sock`. The engine talks to it through NATS, not through a client TCP connection that needs reconnecting after an engine update. Keep NATS and SocketServer running during engine and rendering-worker updates. The Compose service stays named `connectionserver` so existing proxy routes keep working; its image is now `sharpmush/sharpmush-socketserver`. The new `renderer` service uses `sharpmush/sharpmush-connectionserver`.

## What players see

On graceful engine shutdown (including Watchtower's SIGTERM), the engine publishes `MainProcessShutdownMessage` before hosted services stop. The gateway sends every registered session a restart notice through its normal output delegate. After engine bootstrap and connection reconciliation finish and input consumers are registered, `MainProcessReadyMessage` produces a ready notice. The notices work on telnet and gateway WebSockets; WebSocket notices participate in existing output sequencing/replay. Ordinary engine restarts need no Watchtower hook or public maintenance endpoint.

The lifecycle subjects are consumed independently, so the gateway orders them by timestamp and ignores duplicate/older events. This assumes synchronized clocks and one engine, as in the supplied Compose stack. Each slow client gets a bounded delivery wait; other clients continue. A still-pending notice is not overlapped by another notice for that session. Failed clients are logged, not disconnected by the broadcaster. A SIGKILL, host crash or unreachable NATS cannot provide a reliable pre-shutdown notice. This is a graceful lifecycle signal, not a heartbeat-based failure detector or an acknowledgement from every player.

Commands submitted while only the engine is down may remain in JetStream and execute when its durable consumers return (subject to configured retention). The notice asks players to wait. If publishing itself fails, telnet/WebSocket command handling retains the socket and reports uncertain delivery when possible. It does not automatically retry: an acknowledgement can be lost after acceptance, and replaying a non-idempotent command could execute it twice. This does not promise exactly-once commands.

## State and transport protections

* NATS setup and consumer loops retry infrastructure failures. Consumer retries re-establish their stream if it was removed. The gateway host and its sockets remain alive.
* Player bindings and metadata use revision-checked writes with bounded retries. A concurrent deletion or descriptor reincarnation cannot be overwritten by a stale mutation. Exhaustion or infrastructure errors are surfaced, not reported as successful persistence.
* The gateway refreshes live connection entries every minute. This renews the 24-hour KV TTL without replacing player bindings. Missing entries are not recreated from incomplete gateway data. An outage exceeding retention can still lose state.
* Reconciliation fails startup if shared state cannot be read; an unavailable store must not look like an empty session list.
* WebSocket data/close writes are serialized. Input bus failures do not escape the command path and tear down its transport. Local explicit disconnect cleanup still runs when bus publication fails.

Connection handles are opaque 64-bit values. The socket owner reserves never-reused IDs in blocks of 4,096 using a durable revision-checked high-water mark; this adds one KV reservation per block, not per byte or command. Retain the no-TTL `connection_descriptors` bucket with the other JetStream data: resetting it can let delayed output reach a later socket. The first new handle is 2,147,483,648, above the legacy default ranges. Deployments that customized legacy ranges above this value must seed the high-water mark above every historically allocated handle before migration.

Keep the NATS `/data` volume persistent. Replay, resume tokens and connection state need JetStream storage across NATS container replacement. A single host/volume is not protection against loss of that host or disk. The current startup cleanup assumes one SocketServer; do not horizontally scale it against the same state bucket.

## Authenticated WebSocket recovery

A SocketServer replacement still closes its kernel sockets. Browser clients reconnect with a rotating bearer token to recover the logical session. Tokens contain 256 bits of randomness, use hashed KV keys, and are atomically consumed with a revision check. Session revocation invalidates outstanding tokens. While a WebSocket remains attached, the minute heartbeat refreshes its credential after half the configured replay-retention window (12 hours by default), so a long-running session does not lose recovery after its first day. The previous token remains valid until its original TTL because a completed send cannot prove the client received its replacement. This creates a bounded overlap of credentials, all subject to single-use consumption and session revocation; detached sessions receive no refresh. The gateway checks the immutable session identity so a recycled descriptor cannot inherit another player's login or output.

The engine authorizes restored authenticated sessions against current durable and in-memory state: full player objid, account activity and ownership, login/guest policy, sitelocks, session expiry, and transport security. A remembered descriptor alone cannot authorize login. Reattachment does not create a second login event. Engine/NATS unavailability can prevent recovery until services return; disabled accounts, revoked sessions, expired credentials, and invalid security state require a fresh login.

Logging out or switching an authenticated identity permanently revokes resumption for that socket before its binding changes. The live connection continues working, including subsequent logins, but its existing credentials cannot recover another identity or that identity's replay. Open a fresh WebSocket connection to restore resumability. The initial login on a fresh socket retains resumption support.

Replay uses durable JetStream sequence numbers, including across SocketServer restarts, and pages through retained output beyond one 500-frame batch. Sequence numbers are monotonic but need not be contiguous because the stream contains multiple sessions. Replay/attachment and live output are serialized per session. The production token and replay stores retain data for 24 hours; the in-memory token fallback expires 30 seconds after mint and cannot survive a process restart. These are bounded recovery windows, not permanent credentials or unlimited history.

## Rollout and PR impact

The `Deployment impact` PR check reports three image closures and explicitly answers **SocketServer restart / live connection drops: YES or NO**. Dev publishing uses the same analyzer, including old and new project references, Docker inputs, linked resources and global build files. ConnectionServer-only rendering changes can now deploy without closing telnet or WebSocket sockets; changes to SocketServer or its shared dependencies still require socket-owner replacement. This is conservative source-impact analysis, not comparison of remote image digests. Forced publishing and release publishing can also replace unchanged services.

Before merging/publishing this migration, pause Watchtower for the existing public ConnectionServer service: its current image tag becomes the private renderer, so an automatic update using the old Compose definition would interrupt public service. Then publish all three images and apply the new Compose definition once: it switches the existing `connectionserver` service to the SocketServer image, creates `renderer`, and mounts their private `render-socket` volume. That first migration drops current sockets. Legacy v1 resume credentials and replay frames deliberately do not authorize v2 recovery; players must reconnect and log in once at this upgrade boundary. Publish all three images before applying the Compose migration. Do not point existing public ports at the new ConnectionServer rendering image.

The Cloudflare stack continues automatic updates for all three images. Watchtower uses `WATCHTOWER_TIMEOUT=60s`; Compose uses `stop_grace_period: 60s` for engine, socket owner and worker. Rendering requests retry across worker replacement while the stable process retains sockets and pending output. A prolonged rendering outage delays output; it does not reconstruct sockets after loss of the owner process. The worker has no public TCP listener and shares its Unix socket volume only with SocketServer.

For deployments requiring approval before socket loss, set `com.centurylinklabs.watchtower.enable=false` on the `connectionserver` Compose service (the SocketServer image) and replace it during an announced maintenance window. Engine and renderer automatic updates can remain enabled. Watchtower's [stop timeout](https://containrrr.dev/watchtower/arguments/#stop-timeout) controls forced termination; optional [lifecycle hooks](https://containrrr.dev/watchtower/lifecycle-hooks/) can add countdowns, but container startup alone does not establish game readiness. These repository changes do not deploy production automatically until merged, images published, and the Compose migration applied.

## Measured rendering boundary cost

A diagnostic run on 2026-09-07 used CachyOS, an Intel Core Ultra 7 265F and .NET 10.0.8, with the existing Debug worker/SocketServer assemblies and a Release harness. The worker ran in a separate process; `RemoteOutputRenderer.TransformAsync` reused its HTTP/2 Unix-socket connection. Each size received 500 warmups followed by five rounds of 1,000 serial calls, compared against `OutputTransformService.Transform` in process. Inputs were exactly 256 or 4,096 UTF-8 bytes: red ANSI prefix, repeated ASCII text, reset suffix; ANSI stripping was enabled. Warmup outputs were compared byte-for-byte and measured outputs checked for the expected length, with no failures.

| Input | Local mean | UDS mean | UDS round-mean range | UDS round-median range | Serial UDS throughput range |
| --- | --- | --- | --- | --- | --- |
| 256 B | 1.37 µs | 156.17 µs | 140.0–172.9 µs | 117.7–134.4 µs | 5,783–7,143 requests/s |
| 4 KiB | 2.95 µs | 188.91 µs | 178.8–208.7 µs | 147.0–168.4 µs | 4,792–5,594 requests/s |

The process boundary added roughly 0.15–0.19 ms to this simple transformation. It exchanges transport overhead for independent worker replacement. This is a warmed diagnostic measurement, not a production latency/throughput guarantee: Debug assemblies, host scheduling, workload complexity and concurrency affect the result. It does not measure full game commands, NATS, client network latency, allocations, or tail latency under load. Keep the persistent connection and send complete output chunks; measure representative concurrent game traffic before deciding whether batching or a different framing protocol is warranted.

## Remaining boundaries

| Event | Existing TCP connection | Logical session |
| --- | --- | --- |
| Engine update | Retained by SocketServer | Reconciled from durable state |
| ConnectionServer rendering-worker update | Retained by SocketServer | Retained; output waits for worker recovery |
| SocketServer update | Closed | Authenticated WebSocket reconnect can resume within its recovery window; ordinary telnet must reconnect |
| Host, disk or NATS retention loss | Not guaranteed | Limited by available durable state and replay |

Keep a single socket owner. Multi-host operation requires explicit ownership fencing, coordinated cleanup and replicated durable storage; the supplied singleton stack does not provide those guarantees. The embedded portal's engine-hosted SignalR/HTTP connections also restart with the engine; gateway recovery does not preserve those separate transports. No JSON state snapshot can preserve a terminated process's kernel TCP socket, TLS session, or telnet parser.
