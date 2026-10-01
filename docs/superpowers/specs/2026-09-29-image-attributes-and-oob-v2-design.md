# Image attributes and OOB v2 — design

**Status:** approved in conversation 2026-09-29 (option A for image storage). Companion to
`docs/design/d1/README.md` §7, which this document makes concrete where the README assumed data
that does not exist yet.

## 1. Standard image attributes (engine)

Four seeded attribute entries in `SharpMUSH.Database/Seed/AttributeEntrySeed.cs`, each with the
flags `no_command, visual, prefixmatch, public`, so an owner sets them with `&IMAGE obj=<url>`:

| Attribute | Value | Used for |
|---|---|---|
| `IMAGE` | URL | character portrait, room banner, exit destination preview, thing thumbnail |
| `IMAGE`BANNER` | URL | wide art: character profile banner, Play room banner. Falls back to `IMAGE`. |
| `IMAGE`ALT` | text | alt text; softcode falls back to the object's name |
| `IMAGE`FOCAL` | `x y`, each 0–1 | `object-position`; optional |

No width or height attributes: every D1 surface is a fixed-height `object-fit: cover` box, so
dimensions never affect layout. `SeedDataTests` moves its pinned count from 216 to 220. The
attributes are documented in the SharpMUSH attribute helpfile.

## 2. Gallery bridge (server)

`GalleryEntry` gains `IsBanner` (at most one per character; README Q5 is answered by this). The
record is shared through `SharpMUSH.Contracts` instead of being declared twice. Every gallery write
mirrors the icon entry's URL into `IMAGE`, the banner entry's URL into `IMAGE`BANNER`, and the
caption of the icon into `IMAGE`ALT`. A hand-set `&IMAGE` on a non-player is never touched by the
gallery. The profile page reads the gallery for its portrait and banner and draws the letter avatar
only when no icon exists.

## 3. Publishing images (softcode packages)

`profile-handler` 1.5 adds `image`, `banner` and `color` to the `GET /http/profile` fields and
`image` to each `/http/characters` and `/http/online` row. `image`/`banner` read `IMAGE` and
`IMAGE`BANNER`; `color` reads `PROFILE`COLOR` under the existing opt-in `PROFILE`<key>` convention,
so the name colour stays game-defined and needs no seed.

## 4. OOB v2 (`room-contents` 2.0)

Payloads are as `docs/design/d1/README.md` §7.1 (`room.info`, `room.contents`, `room.exits`, all
carrying `"v": 2`). Decisions the README left open:

- **Per-viewer fan-out.** The handler iterates the connected players in the room and calls `oob()`
  once per viewer. `FN`WHOROW` and `FN`EXITROW` receive the viewer as `%1`, so `state: locked`
  comes from `elock()` against the viewer, dark exits are omitted per viewer, and the viewer's own
  row carries `"you": true`.
- **`room.info` triggers.** Sent on `move-in` and `connect`: to the mover alone when the enactor
  is in the room (the handler receives only the room and the cause, and `%#` is the causer — the
  mover for `goto`/connect, the teleporter for `@tel`), otherwise to every connected occupant. The
  engine has no attribute-change event, so a name, image or description edit does not re-push. The
  `query.*` mechanism cannot re-request it: the terminal evaluates a query as the player, so
  `u(#9/FN`PAYLOAD`INFO,…)` runs with `me` = the player. If a re-request is wanted, a global
  `@function roominfo=#9,FN`PAYLOAD`INFO` registered from a `STARTUP` attribute the package owns
  is the route (it also evaluates as #9 with the player as enactor). A `ROOM`INFO` engine event is
  deferred and does not change the payload shape.
- **Per-event cost.** Viewer-independent rows (destination previews, images, area, description)
  are built once per event into registers; per-viewer work is only visibility, lock state, hint,
  `you`, actions and scene. Order of growth is O(K+M) + O(N·(K+M)) for N viewers, K occupants and
  M exits. A very busy hub can blank `FN`DESC` / `FN`DEST` to skip the description evaluations.
- **Privacy.** DARK occupants are omitted for a viewer who cannot see them (self always visible);
  a locked exit carries no `dest`; `PROFILE`COLOR` is accepted only as `#rrggbb`; a malformed
  `IMAGE`FOCAL` is dropped rather than breaking the payload.
- **`scene`** comes from `scenewhere(%0)`; `cast` from `scenemembers()`.
- **`status` and `idle`** come from `idle()` seconds and an `FN`STATUS` helper that maps seconds
  to a word (`active`, `idle`, `away`), redefinable per game.
- **Images** are `IMAGE`/`IMAGE`ALT`/`IMAGE`FOCAL` from §1, as `{ "url", "alt", "focal" }`
  objects; a row without `IMAGE` carries no `image` key.
- `oob()` runs as the event handler object, which holds `Send_OOB`; no permission change.
- v1 consumers keep working: v2 only adds keys.

## 5. Scene events

`ActorObjId` is appended to both `SceneEventMessage` records (plugin and client) and resolved at
broadcast time. The Scene plugin ships an `ooc` command that records the pose through the existing
tag path with tag `ooc` and emits `<OOC> Name: text` to the room. Nothing provides an OOC command
today; this is the "game's OOC command" the README assumes.

## 6. Client contract

`OobEntryParser` becomes typed: `RoomInfo`, `RoomOccupant`, `RoomExit` records sharing an
`ImageRef(Url, Alt, Focal)` value. A payload without `v` parses as v1 into the same records with
null optional members. **Image URL policy:** the client renders a URL only if it is site-relative
(`/…`) or `https:`; anything else falls back to the no-image tile. Kit components that take an
image apply the same rule.

## 7. Order

Image attributes and the gallery bridge first, since OOB v2, the profile banner and the Play
banner all read them. Then README §8 as written; Phase 0 (tokens + kit) has no data dependency
and runs in parallel from the start.

## 8. Testing

- Seed count and helpfile tests for the four attributes.
- Gallery controller tests for the mirror writes and the single-banner rule.
- Package tests that evaluate the v2 handler to valid JSON per viewer, including a locked exit and
  a dark exit omitted for a mortal viewer.
- Parser tests for v1 and v2 input and for the URL policy.
- bUnit tests per kit component.
- Scene boundary test for the appended field.
