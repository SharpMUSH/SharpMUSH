# PennMUSH HTTP Oracle (dev-only)

A reference harness for verifying SharpMUSH's inbound HTTP handler (`help sharphttp`)
against **real PennMUSH behavior**. PennMUSH itself is never committed to this
repository — `.gitignore` already excludes `pennmush/` — and the oracle is **not**
part of CI (PennMUSH is single-threaded and stateful, which makes live parity tests
flaky). Instead: run the oracle locally, observe its responses, and bake the observed
behavior into deterministic SharpMUSH tests
(`SharpMUSH.Tests.Integration/Http/HttpHandlerApiTests.cs`,
`SharpMUSH.Tests/Commands/HttpCommandTests.cs`).

## Prerequisites

A built PennMUSH checkout. A ready one lives at `../SharpMUSH/pennmush` (binary at
`pennmush/src/netmud`, game dir at `pennmush/game`). To build fresh:

```bash
git clone https://github.com/pennmush/pennmush
cd pennmush && ./configure && make        # needs gcc, make, perl, libssl-dev, zlib1g-dev
cd game && ../src/netmud --no-session     # or use the restart script
```

## Running the oracle

```bash
tools/oracle/run-pennmush.sh [path-to-pennmush]    # defaults to ../SharpMUSH/pennmush
```

The script configures `game/mush.cnf` for HTTP (handler player + rate limit), starts
`netmud`, and prints connection info. Stop with Ctrl-C or `tools/oracle/run-pennmush.sh stop`.

## One-time in-game setup (as #1, connect with `connect god`)

```
@pcreate HTTPHandler=oraclepass
@config/set http_handler=[pmatch(HTTPHandler)]
&GET *HTTPHandler=think %0|%1; @respond 200 "TEST RESPONSE"
&POST *HTTPHandler=think got=[%1]
&PUT *HTTPHandler=think host=[%q<hdr.host>] names=[%q<headers>]
```

## Comparing against SharpMUSH

PennMUSH serves HTTP **on the mush port** itself; SharpMUSH (this milestone) serves it
on the portal server under the `/http/` prefix. Send the same request to both and diff:

```bash
# PennMUSH (port from game/mush.cnf, default 4201):
curl -isS 'http://localhost:4201/foo?bar=baz'

# SharpMUSH (Server, default https 8081):
curl -isSk 'https://localhost:8081/http/foo?bar=baz'
```

Compare: status line (`@respond <code> <text>` → reason phrase), default
`Content-Type: text/plain`, `@respond/type` / `@respond/header` overrides, the body
(everything `think`/`@pemit`'d at the handler, in order), `%0` = path **including**
query string, `%1` = request body, and the `%q<hdr.*>` / `%q<headers>` registers.

## Oracle-verified behavior (PennMUSH 1.8.8, captured 2026-06)

Observed with the handler softcode above and baked into the SharpMUSH tests:

- `GET /foo?bar=baz` → body `/foo?bar=baz|` + `\n` — `%0` carries the **full path including
  query string**; an empty request body leaves `%1` empty.
- `POST` with a JSON body → `got={"channel":"public","msg":"hi"}` — the body reaches `%1`
  **verbatim**, braces and all; `[ ]` around `%1` are evaluation brackets and vanish.
- `PUT` with `X-Sharp-Test: marker-value` → `host=localhost:4201 names=HOST USER-AGENT ACCEPT
  X-SHARP-TEST CONTENT-LENGTH CONTENT-TYPE` — header q-registers exist as `%q<hdr.name>`,
  `%q<headers>` lists **uppercased** names.
- `@respond 200 TEST RESPONSE` → status line `HTTP/1.1 200 TEST RESPONSE`.
- `@respond 200 "TEST RESPONSE"` → **rejected**: `@respond must be 3 digits, space, then text .`
  (src/cmds.c requires `isalnum` immediately after the space; the text part is required, so a
  bare `@respond 500` is also rejected). The rejection notify is itself emitted to the handler
  and therefore **appears in the HTTP response body**.
- Defaults when the handler runs: `HTTP/1.1 200 OK`, `Content-Type: text/plain`;
  `Content-Length` is computed by the server.

## Known, deliberate deviations (documented in help sharphttp)

| Behavior | PennMUSH | SharpMUSH |
|---|---|---|
| Handler exists, `<METHOD>` attribute missing | `200 OK`, empty body | `404 Not Found` |
| No `http_handler` configured | `mud_url` bounce page | plain `404` |
| HTTP entry point | the mush port | `/http/` prefix on the portal server (mush-port parity is a later phase) |
| `http_per_second` quota | enforced | not yet enforced (later phase) |
| `@sitelock` IP/method/path checks | enforced | not yet enforced (later phase) |
