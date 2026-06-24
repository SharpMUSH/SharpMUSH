# WebSocket Foundation + Event-Handler-Driven OOB — Design

**Date:** 2026-06-23
**Branch:** `feature/websocket-terminal-play`
**Status:** Approved design, pending implementation plan

## Goal

Improve the browser "Terminal" and "Play" experiences by turning the WebSocket path from a one-way text/markup pipe into a consolidated, bidirectional, structured channel — without inventing any game policy in C#. The engine and client supply *mechanism*; softcode (via the existing event-handler object) supplies *policy* (what structured data to push, when, and how filtered).

The headline user-visible outcome: the Play page's **"Here" / "Exits" sidebar shows real, live, softcode-filtered data** that updates when *anyone* in the room moves, connects, or disconnects — not just the viewing player.

## Scope

**In scope (this spec):**
1. Consolidate the duplicated client service stacks (command vs play) into one parameterized implementation each.
2. A bidirectional WebSocket frame protocol: keep plain-text commands client→server for back-compat, add JSON *control* frames; add a `package` channel to server→client OOB frames.
3. NAWS for the browser — measure a real character grid (Cascadia Mono) and report it through the same `NAWSUpdateMessage` path telnet uses.
4. A transport-agnostic OOB emit *mechanism* (one softcode-callable emit → WebSocket envelope or telnet GMCP, depending on the connection).
5. A new room-scoped engine event (`ROOM`CONTENTS`) plus event-handler-driven fan-out push, with a reference handler softcode shipped on object #9.
6. A client OOB-channel store and the live Play sidebar that consumes it.

**Out of scope (deferred to a later spec, sub-project #4):**
- Input ergonomics (command history, multi-line input, focus/paste handling).
- Scrollback/output features (search-in-buffer, copy/select polish, log export, persistent buffer).
- Prompt frames (a distinct sticky prompt line).
- East-Asian-Width-aware wrapping on the engine side (see Known Limitations).

**Already satisfied — no work required:**
- The `event_handler` config option exists (`DatabaseOptions.cs:104`, `uint?`, default `9`), wired by `OptionsService.Default()` (`OptionsService.cs:111`).
- Object #9 "Event Handler" is seeded in **all three** providers' initial migrations: ArangoDB (`Migration_CreateDatabase.cs:1010`), Memgraph (`MemgraphDatabase.Migration.cs:250`), SurrealDB (`SurrealDatabase.Migration.cs:253`). No migration/options change needed.

## Background: current state

- **Client:** two near-duplicate stacks — `TerminalService`/`WebSocketClientService` (docked command terminal) and `PlayTerminalService`/`PlayWebSocketClientService` (the `/play` page). Both wrap a raw `ClientWebSocket`. `TerminalFrameRenderer` parses server→client JSON envelopes (`{"type","data"}`) of kinds `markup` / `html` / `json`, plus legacy plaintext. The `json` kind is parsed into an `Oob` frame whose payload is then **discarded**.
- **Server (`ConnectionServer`):** `/ws` (port 4202) accepts the socket and publishes inbound UTF-8 text as `WebSocketInputMessage(handle, text)` — treated as a command. `MarkupOutputRenderer` emits `{"type":"markup","data":...}`. Telnet (4201) gets full negotiation (GMCP/MXP/Pueblo/NAWS/MSDP/MCCP); the WebSocket path has no handshake and no client→server structure.
- **Engine:** GMCP/OOB is plumbed but **empty** — `oob(players, package, message)` (`JSONFunctions.cs:502`) emits `GMCPOutputMessage` to telnet+GMCP connections only; `wsjson(json, [player])` (`HTMLFunctions.cs:63`) emits `{"type":"json","data":...}` to WebSocket connections only, with no package/channel. Nothing emits structured room/char data automatically — `look` is text-only (`CONFORMAT`/`EXITFORMAT`).
- **Event handler:** `EventService.TriggerEventAsync(parser, eventName, enactor, params args)` (`EventService.cs:25`) evaluates an attribute named after the event on object #9 as that object, args `%0..%N`, elevated perms. ~10 PennMUSH-named events fire today, including `OBJECT`MOVE` `(objid, newloc, origloc, issilent, cause)` and `PLAYER`CONNECT` / `PLAYER`DISCONNECT`.

## Design principles

- **C# = mechanism, softcode = policy.** No room/character/exit semantics in C#. The engine ships a generic structured-emit and a generic event; softcode on object #9 decides what to send, to whom, and with what filtering.
- **Portal imposes no game policy** (`portal-no-game-policy`). The client OOB store is a generic keyed cache; the sidebar renders whatever shape softcode supplies (labels, ordering, entries all come from the payload). No hardcoded categories.
- **Multi-DB parity** (`multi-database-backends`). Any engine/event/migration change must land equally across ArangoDB, Memgraph, SurrealDB, and be tested on all three.
- **Back-compat.** Existing plain-text command input and existing `markup`/`html`/`wsjson` envelopes keep working unchanged.

---

## Component design

### 1. Bidirectional frame protocol

**Client → server.** The ConnectionServer `/ws` reader (`WebSocketServer.HandleWebSocketAsync`) currently does `Publish(WebSocketInputMessage(handle, text))`. New behavior:

- Attempt to parse the incoming text as a control envelope `{"type": "<known-control-type>", ...}`.
- If it parses to a **known control type**, dispatch it as control (see below) and do **not** publish it as a command.
- Otherwise (plain text, or JSON that isn't a known control type), publish it as `WebSocketInputMessage` exactly as today. A leading `{` that fails control-parse is still treated as a command, preserving the ability to type literal JSON-looking commands.

Known client→server control types (initial set):
- `naws` — `{"type":"naws","cols":N,"rows":M}` (§3).
- `hello` — `{"type":"hello","caps":{...}}` — capability announce on connect (e.g. `supportsHtml`, requested OOB packages). Minimal in this spec; reserved for growth. Sending `hello` is optional; absence implies defaults.

Malformed / unknown control frames: log at debug and ignore. **Never** let a control-frame parse failure break the command path.

**Server → client.** Keep `{"type","data"}`. Add an OOB channel field so the client can route:
```json
{"type":"oob","package":"room.contents","data": { ... }}
```
- `markup`, `html` unchanged.
- `json` (legacy `wsjson`) continues to work; the renderer treats `json` and `oob` equivalently, surfacing `(package, data)` with `package` defaulting to empty/null for legacy `json`.

### 2. Stack consolidation

Collapse the two client stacks into one implementation each, parameterized by a `TerminalChannel` kind (`Command` | `Play`):

- One generic terminal service class implementing both `ITerminalService` and `IPlayTerminalService`; one generic websocket-client class implementing both `IWebSocketClientService` and `IPlayWebSocketClientService`. The `IPlay*` marker interfaces remain as **thin typed DI handles** so existing injection sites and the page-vs-drawer separation are untouched; both resolve to the same code with different channel configuration. Registration in `Program.cs` constructs two singletons with distinct channel kinds.
- All the behavior that currently exists twice — buffer management, reconnect/backoff, send buffering, fragment reassembly, frame parsing, request/response correlation, port-routing — lives once.
- This consolidation is a prerequisite: NAWS, control frames, and OOB routing are added **once** to the shared stack rather than twice.

This is a refactor with no intended behavior change; it is covered by characterization tests (existing bUnit/TUnit behavior must remain green) before new features layer on top.

### 3. NAWS — measuring a character grid in the browser

NAWS reports a grid of **standard (narrow) character cells**. We measure the real rendered advance width and line-box height of the terminal font and floor the content box against them. We never derive cells from `font-size`.

**Font.** Self-hosted **Cascadia Mono** (webfont, subset to include box-drawing `U+2500–257F` and block elements `U+2580–259F`), with fallback chain:
```css
font-family: "Cascadia Mono", "Sarasa Mono SC", "DejaVu Sans Mono", monospace;
```
Self-hosting is required so every client measures identical metrics — if some users got the face and others a system fallback, their reported `cols` would differ for the same pane. Terminal CSS must disable ligatures/kerning to keep the grid and probe measurements stable:
```css
font-variant-ligatures: none;
font-feature-settings: "liga" 0, "calt" 0;
font-kerning: none;
letter-spacing: 0;
```
(Cascadia *Mono* ships with ligatures off by default; these declarations are defensive.)

**New component: `terminalMetrics.js` + C# interop wrapper (`ITerminalMetrics`).** DOM/Canvas metrics can't be read from C#, so a small JS module owns measurement and the resize observer; the C# wrapper owns debounce, dedupe, and sending the NAWS control frame.

Measurement algorithm (`measure(paneElement)` → `{cols, rows}`):
1. **Advance width:** render a hidden probe of a long ASCII run (≈200×`0`) styled identically to the pane; `advance = probe.getBoundingClientRect().width / 200`. The long run averages out sub-pixel rounding that wrecks single-character measurement.
2. **Line height:** a multi-line probe; `lineHeight = probe.height / lineCount` (computed line box, not font-size).
3. **Content box:** measure the scrolling output element and subtract padding **and the scrollbar gutter**. Set `scrollbar-gutter: stable` on that element so the available width is deterministic regardless of scroll state.
4. `cols = clamp(floor(contentW / advance), 1, 1000)`, `rows = clamp(floor(contentH / lineHeight), 1, 1000)`.

Timing & triggers:
- **Gate the first measure on `document.fonts.ready`.** Measuring before the webfont loads yields fallback metrics that then jump when Cascadia Mono swaps in. Re-measure on font load and on theme font-size change.
- **`ResizeObserver` on the terminal pane element** (not `window.resize`) — the pane changes size without the window (command drawer open/close, Play sidebar toggle, flex reflow); zoom also fires it and layout stays in CSS px so measurement is zoom-stable.
- **Debounce ≈150 ms**, then **dedupe** (send only when cols/rows differ from last sent), then send `{"type":"naws","cols","rows"}`. Send once on connect after fonts-ready.

Server side: `WebSocketServer` recognizes the `naws` control frame and publishes the **same `NAWSUpdateMessage(handle, width, height)`** the telnet NAWS plugin publishes, so the engine updates connection capabilities and softcode `width()`/`height()` behave identically for browser and telnet. (Verify the existing `NAWSUpdateMessage` consumer updates connection width/height metadata; reuse it unchanged.)

### 4. Transport-agnostic OOB emit (mechanism, C# only)

One softcode-callable emit pushes structured data to a target **regardless of transport**:
- target is a WebSocket connection → emit `{"type":"oob","package":<pkg>,"data":<json>}` via `WebSocketOutputMessage`.
- target is a telnet connection with GMCP negotiated → emit `GMCPOutputMessage(handle, pkg, json)`.
- target connection supports neither → no-op (silently dropped).

Implementation: extend the existing `oob(targets, package, message)` so it also routes to WebSocket connections (today it is GMCP-only), emitting the websocket envelope with the `package` field. `wsjson()` is retained for back-compat (legacy `json` type, no package). The function takes a target list, so softcode controls fan-out (see §5). No room/char semantics here — it ships whatever softcode hands it.

### 5. Event-handler-driven push (policy, softcode)

**New engine event: `ROOM`CONTENTS`.** Fired whenever a room's *visible* contents change, carrying the affected room dbref + a cause string. It is **room-scoped, not actor-scoped** — this is what makes "update when *other* contents move/connect/disconnect" work: every connected occupant of the room is refreshed, not just the actor.

Fire sites (each fires once per affected room):
- **Movement** (the move handler / `ObjectEventHandlers`): fire for **both** `origloc` and `newloc` when an object enters/leaves a room.
- **Connect / disconnect** (`PLAYER`CONNECT` / `PLAYER`DISCONNECT` paths): fire for the player's current room.

Proposed args: `(roomobjid, cause)` where `cause` ∈ e.g. `move-in` / `move-out` / `connect` / `disconnect` (exact vocabulary finalized in the plan). This is a SharpMUSH extension event (no direct PennMUSH analogue); document it as such alongside the existing PennMUSH-parity events. It is added to `IEventService`/`EventService` consumers the same way existing events are, and tested across all three providers.

**Reference handler softcode** (shipped on object #9, as policy, overridable): a `ROOM`CONTENTS` attribute that, given a room dbref, computes the filtered visible contents and exits (this is exactly where an admin's preferred exit/contents filtering lives), then **fans out** — iterates the room's connected occupants and calls the §4 OOB emit per occupant with packages like `room.contents` and `room.exits`. Connect/disconnect additionally pushes any per-character vitals package the game defines. Because emit is per-target, the softcode owns who receives what.

The reference handler is *example policy*, not engine behavior: a game can replace it, change packages, change filtering, or disable it by clearing the attribute.

### 6. Client OOB store + live sidebar

**`IOobChannelStore`** (new client singleton): receives parsed OOB frames `(package, data)` from the shared terminal stack, caches the latest payload per package, and raises a change event per package. Generic keyed cache — no package-name knowledge baked in.

`TerminalFrameRenderer` is fixed to surface `(package, data)` for `oob`/`json` frames (instead of discarding), and the shared terminal service routes those into the store rather than the visible line buffer.

**Play sidebar:** the "Here" / "Exits" cards subscribe to their packages (e.g. `room.contents`, `room.exits`) and render whatever softcode supplied — a list of entries shaped like `{dbref, name, cmd?}`. Entries are clickable; clicking issues the supplied `cmd` (e.g. an exit's command or `look <dbref>`) back over the play connection. Labels, ordering, and emptiness all come from the payload; the client hardcodes no categories (`portal-no-game-policy`). Blank/untitled entries render gracefully.

---

## Data flow (end-to-end: someone else enters the room)

1. Player B walks from room A into room R (where player V is connected and on `/play`).
2. Move handler fires `OBJECT`MOVE` (existing) and the new `ROOM`CONTENTS` for both A and R.
3. `EventService` evaluates the `ROOM`CONTENTS` attribute on #9 for room R as #9 (elevated).
4. The handler computes filtered contents/exits for R and, for each connected occupant (including V), calls `oob(occupant, "room.contents", <json>)` / `oob(occupant, "room.exits", <json>)`.
5. The emit mechanism sees V is a WebSocket connection → `WebSocketOutputMessage` with `{"type":"oob","package":"room.contents","data":...}`.
6. ConnectionServer writes the envelope to V's socket.
7. V's shared terminal stack parses the frame, routes `(package, data)` into `IOobChannelStore`, which raises a change event.
8. V's Play sidebar "Here" card re-renders with B now present — without V having done anything.

## Error handling

- Malformed/unknown client→server control frame → log (debug) + ignore; command path unaffected.
- OOB frame whose `data` fails to deserialize on the client → drop that frame + log; render and store unaffected.
- NAWS: dims clamped to `[1,1000]`; resize debounced; dedupe avoids floods; first measure gated on fonts-ready.
- `ROOM`CONTENTS` attribute missing on #9 → `EventService` returns silently (existing behavior); no error surfaced to players.
- OOB emit to a disconnected/absent target, or a transport supporting neither WS nor GMCP → no-op.
- Consolidation refactor must not change observable behavior; guarded by characterization tests.

## Testing strategy

- **TUnit, all three providers (Podman/Testcontainers, per `podman-testcontainers`):**
  - `ROOM`CONTENTS` fires with correct args from movement (both origloc and newloc), connect, and disconnect.
  - Extended `oob()` routes to WebSocket connections, emitting the correct `{"type":"oob","package","data"}` envelope; still routes to GMCP for telnet.
  - NAWS control frame → `NAWSUpdateMessage` → connection width/height updated → `width()`/`height()` reflect it.
- **ConnectionServer unit tests:** control-frame-vs-command discrimination (plain text → command; `naws` envelope → control; malformed → command/ignored, never throws).
- **bUnit:** `IOobChannelStore` routing and change events; Play sidebar renders pushed `room.contents` / `room.exits`, including clickable entries issuing the supplied command; empty/untitled entries render gracefully.
- **Client metrics:** `terminalMetrics` measurement logic unit-tested where feasible (advance/lineHeight/clamp math), with DOM-dependent paths exercised via bUnit/JS-interop fakes.
- **Characterization (pre-refactor):** capture current command-terminal and play-terminal behavior so the stack consolidation is provably behavior-preserving.

## Known limitations (documented, not fixed here)

- **Wide-character wrapping:** browsers render CJK/fullwidth glyphs at 2 cells, but PennMUSH-style wrapping counts characters, so wide-character lines can still wrap imperfectly. We report NAWS in honest narrow-cell units; East-Asian-Width-aware wrapping on the engine is a follow-up. The Sarasa Mono / Noto fallback keeps CJK at 2× advance so the *grid* stays aligned even if wrapping doesn't.
- **No prompt frame** yet (deferred).
- **Optional Nerd Font icons** in-terminal are intentionally not shipped; a documented opt-in fallback slot (single-width "Mono" variant only) is left for a future game that wants softcode-emitted icon glyphs. Portal chrome uses MudBlazor SVG icons, not font glyphs.

## Open questions for the implementation plan

- Final `cause` vocabulary and exact arg order for `ROOM`CONTENTS` (align with existing event arg conventions).
- Whether `PLAYER`DISCONNECT` fan-out can reliably resolve the just-departed player's room before teardown, or whether the disconnect fire site needs the room captured earlier in the path.
- Exact package names for the reference handler (`room.contents`, `room.exits`, vitals?) and the entry payload schema (`{dbref, name, cmd}` vs richer).
- Cascadia Mono webfont subsetting/licensing packaging in `SharpMUSH.Client` (OFL attribution, which unicode ranges to include).
