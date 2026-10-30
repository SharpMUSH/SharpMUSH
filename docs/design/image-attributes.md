# Image attributes and room OOB payloads

How objects carry pictures, how the gallery fills them in, and how the `room-contents` package
sends them to the portal's Play page. `docs/design/d1/README.md` §7 has the payload shapes.

## Standard image attributes

`SharpMUSH.Database/Seed/AttributeEntrySeed.cs` seeds four attributes, each `no_command, visual,
prefixmatch, public`, so an owner sets them with `&IMAGE obj=<url>`:

| Attribute | Value | Used for |
|---|---|---|
| `IMAGE` | URL | character portrait, room banner, exit destination preview, thing thumbnail |
| `IMAGE`BANNER` | URL | wide art: character profile banner, Play room banner. Falls back to `IMAGE`. |
| `IMAGE`ALT` | text | alt text; softcode falls back to the object's name |
| `IMAGE`FOCAL` | `x y`, each 0–1 | `object-position`; optional |

There are no width or height attributes: every portal surface that shows them is a fixed-height
`object-fit: cover` box.

## Gallery

`GalleryEntry` (`SharpMUSH.Contracts`) has `IsBanner`; a character has at most one banner. Every
gallery write mirrors the icon's URL into `IMAGE`, the banner's URL into `IMAGE`BANNER` and the
icon's caption into `IMAGE`ALT` (`GalleryRules.Mirror`). A hand-set `&IMAGE` on a non-player is
never touched by the gallery. The profile page reads the gallery for its portrait and banner and
draws the letter avatar only when there is no icon.

## Publishing images

The `profile-handler` package returns `image`, `banner` and `color` from `GET /http/profile`, and
`image` on each `/http/characters` and `/http/online` row. `image` and `banner` read `IMAGE` and
`IMAGE`BANNER`; `color` reads `PROFILE`COLOR` under the opt-in `PROFILE`<key>` convention.

## Room payloads (`room-contents`)

`room.info`, `room.contents` and `room.exits` carry `"v": 2`.

- **Per viewer.** The handler calls `oob()` once for each connected player in the room.
  `FUN`ROOMCONTENTS`WHOROW` and `FUN`ROOMCONTENTS`EXITROW` get the viewer as `%1`, so `state: locked`
  comes from `elock()` against the viewer, dark exits are left out per viewer, and the viewer's own
  row carries `"you": true`.
- **When `room.info` is sent.** On `move-in` and `connect`: to the mover alone when the enactor is
  in the room, otherwise to every connected occupant. A name, image or description edit does not
  push it again, since the engine raises no attribute-change event.
- **Cost.** Rows that do not depend on the viewer (destination previews, images, area, description)
  are built once per event; per-viewer work is visibility, lock state, hint, `you`, actions and
  scene. A busy hub can blank `FUN`ROOMCONTENTS`DESCRIPTION` and `FUN`ROOMCONTENTS`DESTINATION` to
  skip the description evaluations.
- **Privacy.** A DARK occupant is left out for a viewer who cannot see them (a viewer always sees
  themself); a locked exit carries no `dest`; `PROFILE`COLOR` is accepted only as `#rrggbb`; a
  malformed `IMAGE`FOCAL` is dropped.
- `scene` comes from `scenewhere(%0)` and `cast` from `scenemembers()`. `status` and `idle` come
  from `idle()` and `FUN`ROOMCONTENTS`STATUS`, which maps seconds to `active`, `idle` or `away` and
  can be redefined per game.
- Images are `{ "url", "alt", "focal" }` objects from the attributes above; a row with no `IMAGE`
  has no `image` key.
- `oob()` runs as the event handler object, which holds `Send_OOB`.
- A version 1 reader still works: version 2 only adds keys.

## Client

`OobEntryParser` reads the payloads into `RoomInfo`, `RoomOccupant` and `RoomExit`
(`SharpMUSH.Client/Models/RoomPayloads.cs`), which share `ImageRef`. A payload without `v` is read
as version 1 into the same records, with the optional members null. The client shows an image URL
only if it is site-relative (`/…`) or `https:`; anything else gets the no-image tile, and kit
components that take an image follow the same rule.
