# Ancestor Objects, Inheritance, and Standard-Slot Renumber

**Date:** 2026-06-21
**Status:** Approved (design)
**Scope:** Engine + all 3 DB providers + config. High blast radius (renumbers standard dbrefs).
**Parallel sibling:** [Scene capture via `@message`](2026-06-21-scene-capture-message-formats-design.md) —
the Ancestor Player seeds the default `FORMAT`*` attributes that spec reads.

## Problem / Goal

Add the four PennMUSH-style **ancestor objects** as standard database objects in slots **#3–#6**, with
config options pointing at them, and implement **full ancestor attribute/command/listen inheritance**
(parent-of-last-resort per object type). Because #3–#6 are currently occupied, the displaced standard
objects renumber to #7–#9, and their config defaults move with them.

## B1 — Standard slot layout (migrations: Arango, Memgraph, Surreal)

| Slot | Before | After | Type |
|---|---|---|---|
| #0 | Room Zero | Room Zero | room |
| #1 | God | God | player |
| #2 | Master Room | Master Room | room |
| #3 | Package Manager | **Ancestor Room** | room |
| #4 | HTTP Handler | **Ancestor Player** | **thing** |
| #5 | Event Handler | **Ancestor Exit** | **thing** |
| #6 | *(first free)* | **Ancestor Thing** | **thing** |
| #7 | — | Package Manager | player |
| #8 | — | HTTP Handler | thing |
| #9 | — | Event Handler | thing |

- **Ancestor Room** is a real `room`; **Ancestor Player/Exit/Thing** are `thing`s (decided: avoid a
  loginable player ancestor and a dangling exit). They are attribute holders, named accordingly,
  living in the Master Room (#2), owned by God (#1).
- First free game dbref shifts **#6 → #10**.
- All three providers must produce byte-identical slot assignments (per the multi-DB invariant).

## B2 — Configuration

In `SharpMUSH.Configuration/Options/DatabaseOptions.cs`, `SharpMUSH.Library/Services/OptionsService.cs`
defaults, the generated config accessors/metadata, and `mushcnf.dst`:

- **Add** (nullable `uint?`, `Group = "Core Rooms"` or a new "Ancestors" group; unset/`-1` = disabled,
  matching PennMUSH `ANCESTOR_*`):
  - `ancestor_room` = 3
  - `ancestor_player` = 4
  - `ancestor_exit` = 5
  - `ancestor_thing` = 6
- **Move** displaced defaults:
  - `package_manager` 3 → 7
  - `http_handler` 4 → 8
  - `event_handler` 5 → 9

Consumers already read these via config (`options.Database.HttpHandler`, etc.), so no hardcoded dbref
fixes are needed in services — only the defaults change.

## B3 — Inheritance behavior (engine)

PennMUSH semantics: an object's attribute/command/listen lookup, after exhausting its own `@parent`
chain, falls through to the **ancestor for its type**.

- **Attribute resolution** — in the inheritance query handlers behind
  `AttributeService.GetAttributeAsync(..., checkParent)` / `GetAttributesAsync(..., checkParents)`
  (`GetAttributeWithInheritanceQuery`, `GetLazyAttributeWithInheritanceQuery`, `GetAttributesQuery`):
  after the object's parent chain yields nothing, consult the **type ancestor** (from config).
- **`$`-command matching** — add the type ancestor to the candidate object set in command discovery,
  so ancestor-defined `$`-commands fire for every object of that type.
- **`^`-listen matching** — same inclusion for listen-pattern evaluation.
- **Skip conditions:** ancestor disabled (null/`-1`); the object **is** its own type ancestor
  (no self-loop); the attribute is flagged `no_inherit`; standard `no_command`/visibility rules still
  apply.
- **Termination:** the ancestor's *own* `@parent` chain is honored, then stops — no ancestor-of-
  ancestor recursion, no cycles. Per-type lookups are independent.

### Inheritance precedence (explicit)

For attribute `A` on object `O` of type `T`:
1. `O`'s own attribute `A`.
2. `O`'s `@parent` chain (existing behavior), nearest first.
3. The **type-`T` ancestor** object's attribute `A` (this spec), if ancestor enabled and `A` not
   `no_inherit`.
4. Not found.

## B4 — Renumber blast radius (tests & assumptions)

- **3 provider migrations** create #3–#6 (ancestors) and #7–#9 (displaced objects).
- **Config defaults** updated (B2).
- **~10 tests** assume #3/#4/#5 are Package Manager/HTTP Handler/Event Handler (profile, package,
  MyrddinBBS, HTTP hooks): repoint via config or to #7/#8/#9.
- **Tests that assert "first created object = #6"** → #10.
- `DefaultPackagesBootstrapService` already resolves the HTTP handler via config — verify it picks up
  the new #8 default and that attach-mode packages still land correctly.

## B5 — Testing (all 3 providers)

- **Migration:** #3–#6 created with correct names/types; #7–#9 are the displaced standard objects;
  next created object is #10. Parity across Arango/Memgraph/Surreal.
- **Attribute inheritance:** an attribute defined only on the type ancestor is readable on a plain
  object of that type; an own/parent attribute shadows the ancestor; `no_inherit` ancestor attribute
  is **not** inherited; an object that *is* the ancestor does not self-loop.
- **`$`-command via ancestor:** a `$`-command on the Ancestor Thing fires for an unrelated thing;
  same for room/player/exit ancestors with their types.
- **`^`-listen via ancestor.**
- **Disable:** setting `ancestor_thing` to `-1`/null stops thing inheritance.
- **Config round-trip:** new options read/write/validate; displaced defaults resolve to #7–#9.
- **FORMAT bridge:** the Ancestor Player carries default `FORMAT`SAY`/`POSE`/`SEMIPOSE`/`EMIT`
  attributes, so a plain player inherits them (consumed by the sibling scene-capture spec).

## Risks

- **Migration determinism across providers** — the autoincrement/explicit-key strategies differ per
  provider (Arango autoincrement, Memgraph counters, Surreal record ids). Each must still yield the
  exact #3–#9 layout and continue at #10. This is the highest-risk area.
- **Inheritance hot path** — attribute lookup is performance-sensitive; the ancestor fall-through must
  add at most one extra lookup per type and must be cached/guarded against cycles.
- **Hidden dbref assumptions** — any test or seed data assuming the old #3–#6 meanings must be found;
  the renumber is not complete until the full suite is green on all providers.
- **Ordering vs. the FORMAT bridge** — the Ancestor Player's `FORMAT`*` defaults should match the
  fallback literals in the scene-capture spec so behavior is identical whether or not a player has
  overridden them.
