# WebTransport migration proof (manual go/no-go gate)

`webtransport-migration-proof.mjs` verifies the two things that **cannot** be covered by the
in-process unit tests, because they require a live HTTP/3 / QUIC loop and a real WebTransport client:

1. **Interop** — does the *experimental, draft-02* Kestrel `/wt` endpoint actually talk to a current
   WebTransport client at all? (Microsoft's own docs warn the draft has drifted and "may no longer
   work" against current browsers.)
2. **Migration** — does the session survive a network path change (NAT rebind) with no reconnect?

This is the gate the design front-loads: if interop fails, the WebTransport path is not viable yet
and the app simply keeps using the WebSocket fallback (which is already wired and default).

## Prerequisites

- Infra up: `docker compose up -d` (ArangoDB + NATS) from the repo root.
- Node 18+ and `npm i @fails-components/webtransport` in this folder.
- A platform with MsQuic available (Linux: `libmsquic`; it ships with .NET on Windows).

## Steps

```bash
# 1. Start the ConnectionServer with WebTransport enabled.
WebTransport__Enabled=true dotnet run --project ../SharpMUSH.ConnectionServer
#    Note the printed line:  [WebTransport] dev cert SHA-256: <HEX>

# 2. In another shell, run the proof against the HTTP/3 port (default 4203).
node webtransport-migration-proof.mjs https://localhost:4203/wt <HEX>
```

## Interpreting the result

- `✓ session ready … INTEROP OK` + a server response to `look` → **interop passes**; the client
  stack (WebTransportClientService + negotiator) is worth exercising end-to-end in the browser.
- Handshake/timeout failure → **interop fails**; keep `WebTransport:Enabled=false` (WebSocket only).
  The draft-02 mismatch is the expected culprit; revisit when Kestrel WebTransport is finalized
  (tracked upstream for the .NET 11 timeframe).
- `✓ … MIGRATION SURVIVED` → the whole point of the spike is demonstrated on the server side.

## What CI cannot do

There is no in-process automation for this: it needs UDP/HTTP-3, MsQuic, a WT-grade certificate, and
a real WT client. The C# unit tests cover framing, sequencing, replay, resume, negotiation, and the
transport adapters against fakes; this script covers the live transport those fakes stand in for.
