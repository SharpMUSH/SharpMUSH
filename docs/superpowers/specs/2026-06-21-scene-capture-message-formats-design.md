# Scene Capture via `@message` with Per-Player FORMAT Attributes

**Date:** 2026-06-21
**Status:** Approved (design)
**Scope:** Scene package softcode only (`examples/packages/scene/package.yaml`). No engine changes.
**Parallel sibling:** [Ancestor objects + inheritance](2026-06-21-ancestor-objects-inheritance-design.md) — supplies the `FORMAT`*` defaults via the Ancestor Player.

## Problem

The scene package captures roleplay by `@hook/override` on `POSE`/`SAY`/`SEMIPOSE`/`@EMIT`. The
override bodies re-broadcast the speech with hand-rolled `@remit`/`@pemit`/`@oemit` running **as the
Scene Logger**, so:

- The re-emitted notification's **sender is the Scene Logger, not the real speaker** (broke `@emit`
  attribution; surfaced as the CI failures that forced bundled-package isolation).
- The output format is **hard-coded** in each capture attribute — not customizable per player.

## Goal

Rewrite the four capture attributes so they:

1. Re-broadcast through **`@message/spoof`**, which sets the notification sender to the real speaker
   (`%#` / enactor) — restoring correct attribution while still running as the Logger.
2. Render via a **per-player `FORMAT`<TYPE>` attribute** on the speaker, evaluated per recipient
   (speaker sees "You…", others see "Name…"), with a built-in default when the speaker has none.
3. Continue to record the pose into the active scene (`@scene/addpose`) exactly as today.

## Why `@message`

`@message/spoof <recipients>=<default>,<obj>/<attr>,<args>` (see `MessageHelpers.ProcessMessageAsync`):

- With `/spoof` the notification's sender becomes the **enactor** (`%#` = the speaker), not the
  executor (the Logger). This is the attribution fix.
- The format attribute is evaluated **once per recipient** (`EvaluateMessageForRecipient`), so a
  single call produces speaker-specific and observer-specific text.
- A reserved argument token is replaced per recipient with that **recipient's dbref**, letting the
  format compare recipient vs. speaker to choose "You…" vs. "Name…".

## FORMAT attributes (read from the speaker `%#`)

| Attribute | Default render — to speaker / to others |
|---|---|
| `FORMAT`SAY` | `You say, "<msg>"` / `<Name> says, "<msg>"` |
| `FORMAT`POSE` | `<Name> <msg>` (both) |
| `FORMAT`SEMIPOSE` | `<Name><msg>` (both) |
| `FORMAT`EMIT` | `<msg>` verbatim (both; no name) |

- The capture reads `FORMAT`<TYPE>` off the **speaker** (`%#`). When absent, it falls back to a
  built-in default literal baked into the capture attribute (so the package works standalone, before
  the Ancestor Player seeds defaults).
- Each `FORMAT`<TYPE>` receives: `%0` = the message text, and a recipient-dbref arg (via the
  `@message` recipient token) so it can test `strmatch(<recipient>, %#)` for the "you vs. name" split.
- `%#` inside the format is the **speaker** (enactor), because `@message/spoof` sets the speaker as
  enactor for the format evaluation.

## Capture attribute shape (per type)

Each `CMD`CAPTURE`<TYPE>` attribute becomes, in order:

1. **Output:** `@message/spoof [lcon(loc(%#))]=<default-literal>,%#/FORMAT`<TYPE>`,<msg>,<recipient-token>`
   - Recipients = the contents of the speaker's room (`lcon(loc(%#))`), so everyone present hears it,
     each with per-recipient formatting.
   - `<default-literal>` is the built-in fallback used when `%#` has no `FORMAT`<TYPE>` attribute.
2. **Record (unchanged):** `@assert words(setr(sid,scenewhere(loc(%#))))`, then
   `@assert strmatch(scenefocus(%#),%q<sid>)`, then `@scene/addpose %q<sid>=...` with the rendered
   line, exactly as the current attributes do.

The captured pose text stored in the scene uses the **observer** rendering (the third-person form),
matching today's recorded output.

## Non-goals

- No engine/C# changes. `@message` already exists with the needed switches and per-recipient format
  evaluation.
- Core `SAY`/`POSE`/`@EMIT` commands are **not** changed — only the scene capture overrides.
- Ancestor Player seeding of `FORMAT`*` defaults is the sibling spec; this spec only *reads* the
  attribute and falls back to a literal when absent.

## Testing

Extend `SceneCapture_AllInputFormsCaptured` (and add focused cases) to assert:

- **Attribution:** a captured `say`/`pose`/`@emit` notification's **sender is the speaker**, not the
  Scene Logger (the regression that previously forced @emit out of the unit run).
- **Speaker vs. observer rendering:** the speaker receives the "You…" form; a co-located observer
  receives the "Name…" form, from a single capture.
- **Per-player override:** setting `FORMAT`SAY` on a player changes that player's rendered output,
  while a player without it gets the default literal.
- **Scene recording unchanged:** the pose is still recorded with the correct third-person text and
  ordering.

Tests live in `SharpMUSH.Tests.Integration` (scene area), where the scene plugin + bundled package
are loaded.

## Risks / edge cases

- `FORMAT` attributes must be **evaluated, not raw**, in the `@message` per-recipient pass — verify
  the format text (with `%0`/`%#`/recipient arg) renders correctly through `EvaluateMessageForRecipient`.
- Speech that contains `"`/`,` must survive `@message` argument splitting — escape as the current
  attributes already do (`\,` etc.).
- `/spoof` requires the executor (Logger) to pass the spoof permission check; the Logger is WIZARD,
  so it can spoof. Confirm in a test.
