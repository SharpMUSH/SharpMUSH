# Attribute-tree flag propagation: PennMUSH parity

**Date:** 2026-08-24
**Branch:** `fix/attr-tree-visual-propagation`
**Status:** design, pending approval

## Summary

Bring SharpMUSH's attribute-tree flag propagation to parity with PennMUSH. Penn
re-walks an attribute's `` ` ``-separated ancestor path on every access, through
three independent gates. SharpMUSH implements parts of two of them, and the
pattern-matching read path — used by roughly twenty player-facing functions —
bypasses the walk entirely, leaking attributes a `mortal_dark` branch should
hide.

This is a correctness and disclosure fix, not a feature.

## The security finding that motivates it

All three providers anchor a pattern against the full `LongName`
(`ArangoDatabase.Attributes.cs:329,335`; `SurrealDatabase.Attributes.cs:95,106`;
`MemgraphDatabase.Attributes.cs:93-95`). `FOO` does not match `^FOO\`BAR$`.

`GetAttributePatternAsync` (`AttributeService.cs:528-568`) builds its
`darkPrefixes` set **only from the query results**, then calls
`CanViewAttribute` with a **single** attribute. So for `lattr(me/FOO\`BAR)` the
result set holds only the leaf, `darkPrefixes` is empty, and the permission
check never sees the branch.

**A `mortal_dark` branch does not hide its leaf whenever the pattern does not
also match the branch.** Only `**` (which matches every ancestor) and `*` (which
returns only top-level attributes) are accidentally safe.

Exposed surfaces: `lattr`, `lattrp`, `nattr`, `nattrp`, `xattr`, `xattrp`, their
six `reg*` variants, `grepi`, `regrepi`, `wildgrepi`, `@examine`, `@decompile`,
`@edit`, `@grep`.

The existing test passes by luck: `MortalDark_HidesFromLattrForMortal`
(`AttributeTreePermissionTests.cs:177-202`) uses `lattr(me/**)`. Change the
pattern to match only the leaf and it fails.

## Binding decision: match Penn's code, not Penn's help

Penn's help (`game/txt/hlp/pennattr.hlp`) and Penn's source disagree in four
places. This project's rule is to copy PennMUSH's behaviour even where it looks
wrong, so **the code is the parity target**:

| Flag | Help says | Source actually does |
|---|---|---|
| `no_clone` | inherited down trees | `atr_cpy` tests `AF_NOCOPY` per-attribute only (`src/attrib.c:1701-1709`); no tree check exists |
| `veiled` | inherited down trees | no ancestor walk tests `AF_VEILED` |
| `debug` | never tree-affected | consistent — no walk |
| `wizard` | "mortals cannot read" | `AF_WIZARD` is absent from `can_read_attr_internal`; it gates **writes** only |

So `no_clone` and `veiled` do **not** propagate, and `wizard` propagates for
writes only. If Penn later fixes its implementation to match its help, this
follows Penn.

## The three gates

Penn implements propagation as three separate ancestor walks. SharpMUSH must do
the same, and must keep them separate — conflating them is how the current gaps
arose.

### Gate 1 — read

Penn: `can_read_attr_internal` (`src/attrib.c:311-338`). Denies if **any**
ancestor has `AF_INTERNAL` or `AF_MDARK`. For the grant path, requires
`AF_VISUAL` on **every** level, with `AF_NEARBY` gating the grant when the
viewer is remote.

SharpMUSH: `PermissionService.CanViewAttribute` already implements
`Any(IsMortalDark)` deny and `All(IsVisual)` grant correctly — **when handed the
whole path**. The exact-path API does that (`AttributeService.cs:87`); the
pattern path does not.

### Gate 2 — write

Penn: `can_write_attr_internal` / `can_create_attr` via
`Cannot_Write_This_Attr` (`src/attrib.c:364-368`), testing `AF_INTERNAL`,
`AF_SAFE`, `AF_WIZARD`, `AF_LOCKED` on each ancestor; `can_create_attr` adds
GOD-only `AF_NODUMP`.

SharpMUSH: `SetAttributeAsync` does walk the hierarchy and calls `CanSet` per
level, and `CanSet` tests `wizard` and `locked`. `safe`, `internal`, and
`nodump` are an explicit `// TODO` (`PermissionService.cs:31`).

Two write paths bypass the walk entirely:
- `SetAttributeFlagAsync` / `UnsetAttributeFlagAsync` gate on
  `AttributeMode.Execute` → `CanExecuteAttribute`, which never tests
  `wizard`/`locked`/`safe`. A mortal owner can strip `wizard` from their own
  attribute.
- `ClearAttributeAsync` uses the flat `GetAttributesQuery`, so `CanSet` sees
  each match alone rather than its ancestors — `@wipe` under a wizard branch is
  ungated.

### Gate 3 — command matching and object-parent inheritance

Penn: `atr_get_with_parent` re-walks when matching `$`-commands;
`atr_comm_match` skips whole subtrees via a `nocmd_roots` string tree.
`AF_PRIVATE` (`no_inherit`) blocks a subtree, but only across a parent-object
boundary.

SharpMUSH: `no_command` subtree skipping is implemented and correct
(`CommandAttributeScanner.cs:45-51,70-81`). `no_inherit` propagates down-tree
**only** in the command scan; every other path tests the leaf alone, so
`get()`/`lattrp` through an object parent leak.

## Separate the two inheritance axes explicitly

`SharpAttributeFlag.Inheritable` means "survives **object-parent** inheritance".
It is unrelated to tree descent. `CanSet` currently folds ancestor flags
filtered by `Inheritable` (`PermissionService.cs:25-29`), which conflates the
two axes and only works by coincidence — the seeded values happen to line up.

Tree propagation must be **explicit per flag**, matching Penn's hardcoded
checks, and must not consult `Inheritable`. Every propagation site gets a
comment naming which gate and which Penn function it mirrors.

## Target behaviour

| Flag | Read gate | Write gate | Notes |
|---|---|---|---|
| `internal` | deny on any ancestor | deny on any ancestor | currently seeded by no provider and entirely unimplemented |
| `mortal_dark` | deny on any ancestor | — | implemented on exact path; **broken on pattern path** |
| `visual` | grant requires **every** level | — | not `public`; see below |
| `public` | grant, leaf only | — | Penn's `AF_PUBLIC` overrides `SAFER_UFUN`, distinct from `AF_VISUAL` |
| `nearby` | gates the visual grant when remote | — | flag inert today |
| `wizard` | — | deny on any ancestor | already works |
| `locked` | — | deny on any ancestor unless owner | already works |
| `safe` | — | deny on any ancestor | TODO today |
| `nodump` | — | GOD-only on create | inert today |
| `no_command` | — | — | subtree skip in command scan; already correct |
| `no_inherit` | blocks subtree across parent boundary | — | only in command scan today |
| `no_clone`, `veiled` | — | — | do **not** propagate, matching Penn's code |
| `debug`, `no_debug`, `regexp`, `case`, `nospace`, `noname` | — | — | never tree-affected |

`IsVisual` currently returns `visual or public`
(`SharpAttributeExtensions.cs:29-33`), conflating two distinct Penn flags. The
read gate tests `AF_VISUAL` only. Splitting them is required for the
every-level rule to mean what Penn means.

## The pattern-path fix

The core change. A prefix-set approach mirroring `darkPrefixes` **does not
work** — it can only build prefixes from attributes already in the result set,
and the offending ancestor is precisely the one the pattern excluded. That would
re-ship the same defect in a new place.

Instead, assemble the real ancestor path per match:

1. Index the result set by `LongName`.
2. For each match, split `LongName` on `` ` `` and walk its prefixes.
3. Take each ancestor from the index; for prefixes genuinely absent, issue
   `GetAttributeQuery`, deduped across the whole result set.
4. Pass the assembled array to the existing `CanViewAttribute`, which already
   implements both rules correctly.

`GetAttributeQuery` is `ICacheable`, keyed `attribute:{DBRef}:{path}` with tag
`ObjectAttributes`, so repeated ancestor lookups are cache hits rather than
round-trips.

Two cases to settle in implementation:
- With `checkParents: true` the eager handler merges attributes from ancestor
  **objects** (`GetAttributeQueryHandler.cs:64-84`), so ancestor lookups must
  target the owning object, not `obj`.
- A genuinely orphaned `` FOO`BAR `` with no `FOO` node means *no ancestor*, not
  a denial.

## Non-goals

- Storing computed inherited flags on a leaf. Penn never does this; `@examine`,
  `flags()`, and `@decompile` must keep showing an attribute's own flags.
- `LazilyGetAttributePatternAsync` — it has zero callers and its provider
  implementations diverge badly. Fix or delete it in its own change.
- Implementing `no_clone`/`veiled` propagation, per the binding decision above.

## Testing

The current tests do not pin this behaviour, and two of them pass for the wrong
reason:

- `MortalDark_HidesFromLattrForMortal` uses `lattr(me/**)`, whose pattern pulls
  the ancestor into the result set.
- Every test in `AttributeTreePermissionTests` has the mortal examining
  **itself**, so `CanExamine` short-circuits and the `All(IsVisual)` branch never
  executes even when reached.

Required:
- A direct regression test for the leak: `mortal_dark` branch, pattern matching
  only the leaf, asserted hidden. Must fail without the fix.
- The visual-every-level rule with a viewer who is **not** the owner and cannot
  examine — the only configuration that exercises the grant path.
- `internal` denial on read and write.
- `safe` denial on write, including via `SetAttributeFlagAsync` and
  `ClearAttributeAsync`, the two paths that currently bypass the walk.
- `no_inherit` blocking a subtree through an object parent outside the command
  scan.
- Negative tests that `no_clone`, `veiled`, and `debug` do **not** propagate,
  pinning the deliberate divergence from Penn's help.
- Provider parity: the pattern-path fix must be exercised against all three
  providers, since production runs SurrealDB and the dev default is ArangoDB.

## Risks

**Behaviour change for existing games.** Attributes currently visible under a
`mortal_dark` branch will become hidden, and `visual` leaves under a non-visual
branch will stop being readable. Both are Penn-correct, but a game relying on
the buggy behaviour will notice. This belongs in release notes.

**Cost on wide patterns.** `lattr(me/**)` on an object with a deep tree now
assembles ancestors per match. Mitigated by the result-set index (most ancestors
are already present for wide patterns) and by `GetAttributeQuery`'s cache. Worth
measuring before merge.

**`internal` has no seed.** Adding the flag to three providers is a migration,
with the same duplication the flag table always imposes.
