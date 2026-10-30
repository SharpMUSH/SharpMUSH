# Connections during deployment

The stable SocketServer owns telnet and `/ws` sockets, their protocol state, and NATS subscriptions. The rendering worker (project `SharpMUSH.RenderingWorker`, image `sharpmush/sharpmush-renderer`) is a replaceable process reached over HTTP/2 on the private Unix socket `/run/sharpmush/render.sock`. The engine talks to it through NATS, not through a client TCP connection that needs reconnecting after an engine update. Keep NATS and SocketServer running during engine and rendering-worker updates. The Compose service `socketserver` runs `sharpmush/sharpmush-socketserver`, and the `renderer` service runs `sharpmush/sharpmush-renderer`.

Where the two cannot share a volume (separate Fly.io apps or Railway services), set `Rendering__Address` on both to an address on the host's private network, such as `http://my-game-renderer.internal:5100`, and the worker listens there instead of on the socket. The worker asks for no credentials, so that address must never be public.

Each image name always means one process, so a Compose file never pulls a different process under the same tag. `ConnectionServer__*` settings configure SocketServer.

## Changes to browser control frames

A portal release that adds an inbound WebSocket control type also requires a SocketServer release
that recognizes it. Publish and update the socket owner during an announced window before exposing
the new portal sender. Engine and renderer updates alone cannot add input protocol support to the
running socket owner. Compare the running containers' `org.opencontainers.image.revision` labels
with the commit that added the control handler; a floating `dev` tag does not prove compatibility.

For example, the browser's `{"type":"environ","vars":{"TZ":"America/Chicago"}}` timezone report
needs a SocketServer that handles `environ`. A socket owner without that handler forwards this
JSON as a command. It can then enter softcode evaluation
and game command dispatch, instead of remaining connection metadata. JSON escaping in the portal
cannot repair that missing gateway handler. Updating the owner closes its
physical sockets, so use the SocketServer update procedure below and verify a fresh browser login
and resume: `clientzone()` should report the browser's zone without any room speech.

## What players see

On graceful engine shutdown (including Watchtower's SIGTERM), the engine publishes `MainProcessShutdownMessage` before hosted services stop. The gateway sends every registered session a restart notice through its normal output delegate. After engine bootstrap and connection reconciliation finish and input consumers are registered, `MainProcessReadyMessage` produces a ready notice. The notices work on telnet and gateway WebSockets; WebSocket notices participate in existing output sequencing/replay. Ordinary engine restarts need no Watchtower hook or public maintenance endpoint.

The lifecycle subjects are consumed independently, so the gateway orders them by timestamp and ignores duplicate/older events. This assumes synchronized clocks and one engine, as in the supplied Compose stack. Each slow client gets a bounded delivery wait; other clients continue. A still-pending notice is not overlapped by another notice for that session. Failed clients are logged, not disconnected by the broadcaster. A SIGKILL, host crash or unreachable NATS cannot provide a reliable pre-shutdown notice. This is a graceful lifecycle signal, not a heartbeat-based failure detector or an acknowledgement from every player.

Commands submitted while only the engine is down may remain in JetStream and execute when its durable consumers return (subject to configured retention). The notice asks players to wait. If publishing itself fails, telnet/WebSocket command handling retains the socket and reports uncertain delivery when possible. It does not automatically retry: an acknowledgement can be lost after acceptance, and replaying a non-idempotent command could execute it twice. This does not promise exactly-once commands.

## State and transport protections

* New connections must persist their initial state before the engine is notified. Registration retries each phase up to five times within a 30-second deadline; publication retries preserve the existing record and player binding. Failure closes only the incomplete new connection.
* NATS setup and consumer loops retry infrastructure failures. Consumer retries re-establish their stream if it was removed. The gateway host and its sockets remain alive.
* Player bindings and metadata use revision-checked writes with bounded retries. A concurrent deletion or descriptor reincarnation cannot be overwritten by a stale mutation. Exhaustion or infrastructure errors are surfaced, not reported as successful persistence.
* The gateway refreshes live connection entries every minute. This renews the 24-hour KV TTL without replacing player bindings. Missing entries are not recreated from incomplete gateway data. An outage exceeding retention can still lose state.
* Reconciliation fails startup if shared state cannot be read; an unavailable store must not look like an empty session list.
* WebSocket data/close writes are serialized. Input bus failures do not escape the command path and tear down its transport. Local explicit disconnect cleanup still runs when bus publication fails.

Connection handles are opaque 64-bit values. The socket owner reserves never-reused IDs in blocks of 4,096 using a durable revision-checked high-water mark; this adds one KV reservation per block, not per byte or command. Retain the no-TTL `connection_descriptors` bucket with the other JetStream data: resetting it can let delayed output reach a later socket. The first new handle is 2,147,483,648, above the legacy default ranges. Deployments that customized legacy ranges above this value must seed the high-water mark above every historically allocated handle before migration.

Keep the NATS `/data` volume persistent. Replay, resume tokens and connection state need JetStream storage across NATS container replacement. A single host/volume is not protection against loss of that host or disk. The current startup cleanup assumes one SocketServer; do not horizontally scale it against the same state bucket.

## Authenticated WebSocket recovery

A SocketServer replacement still closes its kernel sockets. Browser clients reconnect with a rotating bearer token to recover the logical session. Tokens contain 256 bits of randomness, use hashed KV keys, and are atomically consumed with a revision check. Tokens are inspected before engine authorization and atomically consumed only after authorization and replay reads succeed. Transient authorization/storage failures leave the credential available for the next reconnect attempt; policy denial still requires a fresh login. Session revocation invalidates outstanding tokens. While a WebSocket remains attached, the minute heartbeat refreshes its credential after half the configured replay-retention window (12 hours by default), so a long-running session does not lose recovery after its first day. The previous token remains valid until its original TTL because a completed send cannot prove the client received its replacement. This creates a bounded overlap of credentials, all subject to single-use consumption and session revocation; detached sessions receive no refresh. The gateway checks the immutable session identity so a recycled descriptor cannot inherit another player's login or output.

The engine authorizes restored authenticated sessions against current durable and in-memory state: full player objid, account activity and ownership, login/guest policy, sitelocks, session expiry, and transport security. A remembered descriptor alone cannot authorize login. Reattachment does not create a second login event. Engine/NATS unavailability can prevent recovery until services return; disabled accounts, revoked sessions, expired credentials, and invalid security state require a fresh login.

Logging out or switching an authenticated identity permanently revokes resumption for that socket before its binding changes. The live connection continues working, including subsequent logins, but its existing credentials cannot recover another identity or that identity's replay. Open a fresh WebSocket connection to restore resumability. The initial login on a fresh socket retains resumption support.

Replay uses durable JetStream sequence numbers, including across SocketServer restarts, and pages through retained output beyond one 500-frame batch. Sequence numbers are monotonic but need not be contiguous because the stream contains multiple sessions. Replay/attachment and live output are serialized per session. Socket sends and closes have five-second deadlines; a stalled transport detaches while its output remains in replay. Incomplete replay caused by history expiring during a read requires a fresh session, while infrastructure errors preserve the credential for retry. Ended sessions purge their replay subject. The production token and replay stores retain data for 24 hours; the in-memory token fallback expires 30 seconds after mint and cannot survive a process restart. These are bounded recovery windows, not permanent credentials or unlimited history.

## Rollout and PR impact

The `Deployment impact` PR check reports three image closures and explicitly answers **SocketServer restart / live connection drops: YES or NO**. Dev publishing uses the same analyzer, including old and new project references, Docker inputs, linked resources and global build files. Rendering-worker-only changes deploy without closing telnet or WebSocket sockets; changes to SocketServer or its shared dependencies require socket-owner replacement. This is conservative source-impact analysis, not comparison of remote image digests. Forced publishing and release publishing can also replace unchanged services.

The Cloudflare stack continues automatic updates for all three images. Watchtower uses `WATCHTOWER_TIMEOUT=60s`; Compose uses `stop_grace_period: 60s` for engine, socket owner and worker. Rendering requests retry across worker replacement while the stable process retains sockets and pending output. A prolonged rendering outage delays output; it does not reconstruct sockets after loss of the owner process. The worker has no public TCP listener and shares its Unix socket volume only with SocketServer.

The supplied Compose files therefore label the `socketserver` service (the SocketServer image) `com.centurylinklabs.watchtower.enable=false`, and the engine and renderer `true`. Watchtower carries the two updates that keep sockets; the socket owner is replaced deliberately, during an announced window:

```bash
docker compose -f docker-compose.cloudflare.yml pull socketserver
docker compose -f docker-compose.cloudflare.yml up -d socketserver
```

Leaving that label at `true` is what makes a routine image publish disconnect every logged-in player without warning — the notices for the engine restart arrive, and then the socket dies when Watchtower reaches the owner in the same cycle. Set it to `true` only where unattended socket-owner updates are worth that. Watchtower's [stop timeout](https://containrrr.dev/watchtower/arguments/#stop-timeout) controls forced termination; optional [lifecycle hooks](https://containrrr.dev/watchtower/lifecycle-hooks/) can add countdowns, but container startup alone does not establish game readiness. These repository changes do not deploy production automatically until merged, images published, and the Compose migration applied.

## What each update keeps

| Event | Existing TCP connection | Logical session |
| --- | --- | --- |
| Engine update | Retained by SocketServer | Reconciled from durable state |
| Rendering worker update | Retained by SocketServer | Retained; output waits for worker recovery |
| SocketServer update | Closed | Authenticated WebSocket reconnect can resume within its recovery window; ordinary telnet must reconnect |
| Host, disk or NATS retention loss | Not guaranteed | Limited by available durable state and replay |

Keep a single socket owner. Multi-host operation requires explicit ownership fencing, coordinated cleanup and replicated durable storage; the supplied singleton stack does not provide those guarantees. The embedded portal's engine-hosted SignalR/HTTP connections also restart with the engine; gateway recovery does not preserve those separate transports. No JSON state snapshot can preserve a terminated process's kernel TCP socket, TLS session, or telnet parser.

Resume tokens remain retryable when engine authorization is unavailable. After a token is consumed, failure of the final durable revocation check fails closed; the client must establish a fresh session.
