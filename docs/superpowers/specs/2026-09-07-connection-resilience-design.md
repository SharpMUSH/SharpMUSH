# Connection resilience during updates

## Process boundaries

SocketServer owns telnet/WebSocket sockets, telnet interpreter state, TLS, compression, descriptors, detached-session sinks and existing NATS input/output consumers. ConnectionServer becomes a pure rendering worker. A persistent, multiplexed HTTP/2 connection over a shared Unix-domain socket carries complete immutable rendering contexts and returns final bytes. Input keeps the existing direct NATS route; no per-byte state-store write is added. Pure rendering retries across replacement with bounded in-flight requests; only the owner writes client bytes. Engine lifecycle notices bypass rendering.

## Lifecycle and recovery

Existing shutdown/ready contracts notify every registered session. Shutdown runs in hosted StoppingAsync, readiness after bootstrap/reconciliation and durable input consumer registration. The owner serializes notices and ignores older/duplicate timestamps. Slow or failed writes do not disconnect healthy clients. NATS consumer setup/loops retry; revision-checked state mutation preserves concurrent metadata and does not resurrect deletions. Live state gets a periodic TTL refresh.

## Durable browser authentication

Each socket incarnation carries an immutable session ID. Tokens are random 256-bit credentials, indexed by hash and atomically consumed. After owner restart, retained unexpired browser records reconstruct detached output sinks and reserve descriptors. Engine authorization checks persisted/current incarnation, binding, full player objid, active account, login/sitelock policy, transport security and lease. Transport metadata changes use conditional CAS; no second Bind or login event fires. Replay sequences come from JetStream's durable sequence and paginate past 500 records. Replay/attach/live output share a gate. Periodic credential refresh prevents long-lived clients outlasting token TTL; old credentials overlap only until expiry. Logout/identity changes revoke resumption until a fresh socket, isolating later authentication from old credentials and history.

Registration, disconnect and queued input must be fenced by incarnation so recycled handles cannot inherit authentication or execute old commands. Session revocation errors cannot prevent local socket closure.

## Deployment

Three independently selected images share one source-impact analyzer between publishing and PR checks. Only SocketServer image replacement drops telnet TCP; engine/renderer replacement preserves it. The first split/token migration requires a planned reconnect. Compose retains existing public service routes, adds a private rendering volume/worker and gives orderly shutdown 60 seconds. Single owner only: replicated ownership/fencing across multiple socket hosts is not implemented. Kernel sockets still cannot survive destruction of the socket-owning process itself.

## Validation

Real NATS state/replay tests; atomic consumption/CAS and revoked/reused session regressions; reconstructed authenticated session tests; actual UDS worker replacement and real telnet socket preservation; lifecycle ordering; existing gateway suite; source-impact policy tests; formatting/build/PR CI. No live production deployment without a separate rollout.
