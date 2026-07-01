# WebTransport (migration-focused) alongside WebSocket — Design

**Date:** 2026-07-01
**Branch:** `spike/webtransport`
**Status:** Design approved (sections 1–6), pending spec review → implementation plan

## Goal

Add WebTransport over HTTP/3 to the SharpMUSH terminal-play path **alongside** the
existing WebSocket transport, so that a browser client's connection survives
network changes (QUIC connection migration) without dropping the in-game session.

The primary motivation is **connection migration**, not session survival. A short
JetStream-backed replay safety net is included only for the fallback path (when
migration fails and the client must reconnect fresh).

### Non-goals

- Replacing WebSocket. WT is additive; WS remains the default/fallback.
- Multiplexing / exploiting multiple QUIC streams to avoid head-of-line blocking
  (a single bidirectional stream is used — WS-equivalent semantics).
- WebTransport for the SignalR layer (GameHub/SceneHub) — SignalR does not support
  WebTransport; that layer stays on WebSockets.
- WebTransport for the MAUI `SharpClient` — no managed .NET WT client exists; this is
  browser (Blazor WASM) only.
- Full session survival / detachable descriptors (deliberately out of scope).

## Background & feasibility

- **Client story:** No managed .NET WebTransport client exists
  ([dotnet/runtime#43641](https://github.com/dotnet/runtime/issues/43641), open/Future).
  In Blazor WASM the only path is JS interop to the browser's native `WebTransport`
  object (Baseline since Safari 26.4, March 2026).
- **Server story:** Kestrel has *experimental* WebTransport over HTTP/3
  ([devblogs](https://devblogs.microsoft.com/dotnet/experimental-webtransport-over-http-3-support-in-kestrel/)),
  draft-02, preview-gated. Finalization is an open, uncommitted ask for the .NET 11
  timeframe ([dotnet/aspnetcore#65406](https://github.com/dotnet/aspnetcore/issues/65406)).
- **Migration:** MsQuic (under Kestrel HTTP/3) supports **server-side** connection
  migration automatically, including NAT rebinding — "you don't need to do anything to
  use it" ([MsQuic Deployment docs](https://github.com/microsoft/msquic/blob/main/docs/Deployment.md)).
  The client's migration behavior is the **browser's** QUIC stack, not MsQuic.
- **Risk:** The server stack is experimental draft-02 and may not interoperate with
  current browsers. The design quarantines all WT code behind interfaces and a feature
  flag so it degrades to today's WS behavior and can be removed cleanly.

## Section 1 — Architecture & the transport seam

Both transports become adapters behind one duplex byte-pipe interface; the existing
connection lifecycle is refactored once to consume it. Migration then requires **zero**
session-logic changes — a migrated QUIC connection keeps feeding the same pump with the
same handle.

```csharp
interface IDuplexTransport
{
    string Kind { get; }        // "websocket" | "webtransport"
    string RemoteIp { get; }
    string Hostname { get; }
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct);
    Task<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken ct); // null = peer closed
    Task CloseAsync();
}
```

A single `ConnectionPump` owns the shared logic currently inline in
`WebSocketServer.HandleWebSocketAsync`: call `IConnectionServerService.RegisterAsync`
(passing `SendAsync` as the output function), loop `ReceiveAsync` → publish
`InputMessage` to NATS, and on a real close call `DisconnectAsync`.
`WebSocketServer` and a new `WebTransportServer` shrink to thin adapters.

**WebTransport modeling decision:** WT exposes streams + datagrams, not one message
channel. For a terminal we accept the session and use **one bidirectional stream** as
the byte pipe — ordered, reliable, in-order; WS-equivalent. Multiplexing is out of scope.

**Component boundaries:**
- `IDuplexTransport` + `ConnectionPump` — shared, transport-agnostic.
- `WebSocketTransport` / `WebSocketServer` — existing behavior, refactored onto the seam.
- `WebTransportTransport` / `WebTransportServer` — new, quarantined, deletable.
- `ConnectionServerService` (session state, handles) — **unchanged**.

## Section 2 — Server endpoint & Kestrel config

- Enable experimental stack: `<EnablePreviewFeatures>true` in
  `SharpMUSH.ConnectionServer.csproj` + runtime switch
  `Microsoft.AspNetCore.Server.Kestrel.Experimental.WebTransportAndH3Datagrams=true`.
- Kestrel listener: `HttpProtocols.Http1AndHttp2AndHttp3` + `UseHttps`, so `/ws`
  (H1/H2) and `/wt` (H3) coexist on one origin; Alt-Svc advertises H3 so browsers upgrade.
- `/wt` middleware: check `IHttpWebTransportFeature.IsWebTransportRequest` →
  `AcceptAsync()` → accept one bidirectional stream → wrap in
  `WebTransportTransport : IDuplexTransport` → hand to `ConnectionPump`. Handle
  allocation reuses `DescriptorGeneratorService`.
- Dev cert: HTTP/3 needs a trusted cert; the browser `serverCertificateHashes` option
  is the self-signed dev path.

## Section 3 — Client: abstraction, JS interop, negotiation

- New `ITransportClient` — superset of today's `IWebSocketClientService`
  (`ConnectAsync/SendAsync/MessageReceived/StateChanged/IsConnected` + `Kind`).
  `WebSocketClientService` already satisfies it.
- `WebTransportClientService`: C# over `IJSRuntime` calling `wwwroot/js/webtransport.js`,
  which wraps the browser `WebTransport` object — open session to `https://host/wt`, open
  a bidi stream, pump reads to a `DotNetObjectReference` callback, writes via the stream
  writer. `typeof WebTransport === 'undefined'` → throw → fallback.
- `TransportNegotiator`: feature-detect + try WT with a bounded timeout; any failure →
  construct WS. The rest of the client (`PlayTerminalService`) sees only
  `ITransportClient`. Migration is transparent to client code — do not tear down on
  transient state blips.

## Section 4 — Replay safety net (fallback path only)

- Each outbound frame to a handle carries a monotonic per-handle `seq` in the envelope;
  the client tracks the highest seq seen.
- The server keeps a bounded, short-TTL per-handle output history **backed by a JetStream
  subject** — the "NATS allows replay" realization; survives a ConnectionServer instance
  bounce, not just in-memory state.
- On a **fresh** reconnect (migration failed / WS fallback): the client sends a resume
  token (minted via the existing `IOttStore`/OTT infra, bound to the prior handle+player)
  + `lastSeq`. Within a short grace window (~30 s) the server rebinds to the surviving
  handle and replays JetStream messages after `lastSeq`; past the window → fresh session,
  exactly as today. The happy migration path never touches this.

## Section 5 — Error handling & fallback semantics

- WT connect failure (no H3 / untrusted cert / UDP blocked / unsupported browser /
  draft-02 handshake mismatch) → negotiator falls back to WS within timeout. Worst case
  equals today's behavior.
- Server + client `WebTransportEnabled` feature flag is the rip-out lever: off → `/wt`
  unmapped, client goes straight to WS.
- WT session lost and migration did not save it → brief WT reconnect, then WS fallback;
  replay covers the gap.

## Section 6 — Testing

- **Unit:** `ConnectionPump` against a fake `IDuplexTransport` (send/receive/close +
  publish assertions); `TransportNegotiator` fallback (WT throws → WS chosen); replay
  buffer (seq assignment, replay-after-lastSeq, grace expiry → fresh).
- **Integration:** existing WS tests stay green — proves the refactor is
  behavior-preserving.
- **WT E2E (not fully automatable in-process):** a scripted external client (Node
  `@fails-components/webtransport` or Python `aioquic`) connects, exchanges data, then
  **rebinds its source port to simulate migration** and asserts output continuity on the
  same handle. This is the decisive migration proof. Automation limits are noted
  explicitly; CI cannot cover the live-browser + HTTP/3 path.

## Open risks

1. Experimental Kestrel WT (draft-02) may not interoperate with current Chrome/Firefox
   WT. Mitigation: feature flag + WS fallback; prove interop early with the external
   client before building client UI wiring.
2. Active migration (WiFi→cellular) depends on the browser, not us; NAT-rebind migration
   is the reliably-testable case.
3. Replay drifts toward session survival; kept minimal (short grace, bounded history,
   fallback-only) to stay within scope.
