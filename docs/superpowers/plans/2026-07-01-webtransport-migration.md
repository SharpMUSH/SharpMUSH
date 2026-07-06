# WebTransport Migration — Speculation & Findings

> The implementation plan and the WebTransport code it produced were **removed** after the interop
> finding below. This document is kept as the record of *why* WebTransport was explored and *what was
> learned*. The design rationale is in
> [`../specs/2026-07-01-webtransport-migration-design.md`](../specs/2026-07-01-webtransport-migration-design.md);
> the work that actually shipped is in
> [`../specs/2026-07-01-detached-sessions-design.md`](../specs/2026-07-01-detached-sessions-design.md).

## Speculation (the idea)

Give a SharpMUSH WebSocket session **QUIC connection migration**: a WebTransport-over-HTTP/3 transport
alongside WebSocket so a session survives a network switch (WiFi→cellular) transparently at the
transport layer, with WebSocket as fallback. A transport seam (`IDuplexTransport` + `ConnectionPump`)
would let both transports share one connection lifecycle, and a JetStream-backed replay buffer would
cover the fallback path.

## Findings

### 1. There is no managed .NET WebTransport client
`dotnet/runtime#43641` (a `ClientWebSocket`-equivalent for WebTransport) is open, "Future" milestone.
In Blazor WASM the only path is JS interop to the browser's native `WebTransport`. The MAUI client has
no path at all.

### 2. Kestrel's server support is experimental draft-02
`EnablePreviewFeatures` + the `WebTransportAndH3Datagrams` runtime switch; Microsoft's own docs warn
the draft has drifted and "may no longer work" against current browsers.

### 3. Empirical interop gate — **NO-GO for current browsers** (2026-07-01)
Ran a real gate: assembled `libmsquic` 2.5.9 (+ `libnuma`, a stub `libxdp.so.1`) to get
`QuicListener.IsSupported = true` on the dev box, stood up a minimal host mounting the real
`WebTransportServer` + `ConnectionPump`, and drove it with **Playwright / Chromium 149** using the
browser WebTransport API with `serverCertificateHashes`.

- **HTTP/3 itself works** end-to-end: `curl --http3 https://localhost:4433/` returns `wthost up`.
- **The WebTransport session handshake fails**: Chromium 149 reports `ERR_QUIC_PROTOCOL_ERROR` /
  "Opening handshake failed"; the request never reaches `AcceptAsync` (404s). The failure is at
  Kestrel's WebTransport-session negotiation layer, **not** the app code — plain H3 proves the host
  wiring is correct, and the framing/pump/replay units all passed.

**Conclusion:** experimental Kestrel WebTransport (draft-02) does not interoperate with current
Chromium. There is also no managed .NET client. Revisit when Kestrel WebTransport is finalized
(tracked upstream for the .NET 11 timeframe).

## Retarget (what shipped instead)

The reconnect-resilience half of the idea was kept and delivered over plain WebSocket using infra
SharpMUSH already runs: **output sequencing + durable NATS JetStream replay + detached-session
pinning**. The transport seam (`IDuplexTransport` + `ConnectionPump`) was kept — it's the clean
insertion point if WebTransport ever becomes viable in .NET. See the detached-sessions design doc for
the shipped architecture.
