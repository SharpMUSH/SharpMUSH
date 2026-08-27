# Inherited syntax flags and `no_syntax`

**Date:** 2026-08-24
**Status:** design, awaiting implementation
**Sequencing:** third. Depends on `feat/attribute-syntax-formatting` (the flags)
and `fix/attr-tree-visual-propagation` (the ancestor-walk machinery). Build this
only after both have merged.

## Summary

Make `cmdsyntax` and `funsyntax` inherit — down attribute trees and across
object parents — so a player flags a branch once instead of every leaf. Add
`no_syntax` as the escape hatch, mirroring the `debug`/`no_debug` pairing
SharpMUSH already has.

This reverses the recommendation in the original syntax-flags spec, which placed
them in Penn's never-inherited bucket alongside `regexp` and `case`. That
reasoning was sound for parity but wrong for usability: a tree of code
attributes is exactly the case the feature exists to serve, and there is no
parity constraint here — these flags have no PennMUSH equivalent, so SharpMUSH
defines their semantics.

## Resolution rule: nearest ancestor wins

Walk from the leaf upward. The **first** level carrying any of the three flags
decides, and the walk stops there.

```
ParseType? EffectiveSyntax(path)          // path is root..leaf
  for level in path reversed:             // leaf first
    if level has no_syntax:  return null  // formatting off
    if level has cmdsyntax:  return CommandList
    if level has funsyntax:  return Function
  return null                             // no flag anywhere: unformatted
```

Every level is treated uniformly — the leaf is not special. A subtree can
override its parent in either direction:

```
&CODE obj=...                 @set obj/CODE=cmdsyntax        formats as commands
&CODE`HELPER obj=...          @set obj/CODE`HELPER=funsyntax formats as a function
&CODE`DATA obj=...            @set obj/CODE`DATA=no_syntax   not formatted
&CODE`DATA`BLOB obj=...       (inherits no_syntax)           not formatted
&CODE`DATA`CODE2 obj=...      @set …`CODE2=cmdsyntax         formats again
```

This deliberately diverges from Penn's taxonomy, under which a granting flag is
never inherited and a restricting flag denies from any ancestor. Penn's
asymmetry exists to make denial unskippable for security; nothing here grants or
withholds access, so the asymmetry buys nothing and costs expressiveness.

## `no_syntax`

New attribute flag. Name `no_syntax`, symbol **`X`** — chosen for the
`debug`/`no_debug` = `b`/`B` precedent. Caveat worth noting in the help text:
`X` pairs visually with `cmdsyntax`'s `x` but suppresses **both** dialects, not
just the command one.

`Inheritable = true`, so a parent object's suppression carries to a child, the
same as the two positive flags.

Seeded in all three providers plus an ArangoDB migration, per the usual
three-way duplication.

## The two axes, and why they compose cleanly

`Inheritable` governs the **object-parent** axis; the resolution walk above
governs the **attribute-tree** axis. They are independent and already compose:
object-parent resolution happens first and produces the effective attribute,
whose `LongName` path is then walked for tree resolution.

All three flags are `Inheritable = true`, so an attribute inherited from a
parent object arrives carrying whatever syntax flag it had there, and the tree
walk then runs over the inheriting object's path. No special casing needed —
but the implementation must not conflate the two, and each site gets a comment
saying which axis it serves.

## Call sites

`@examine` and `@grep/PRINT` currently call `attr.SyntaxParseType()` on the leaf
alone. That becomes a path-aware call taking the assembled ancestor array —
which is precisely what the tree-parity branch builds for its read gate, so this
reuses that machinery rather than adding a fourth ancestor walk.

Set-time validation uses the same resolution: an attribute resolving to
`no_syntax` produces no parse-failure warning. Suppressing the formatting but
still emitting warnings would be incoherent.

## Display

Per Penn's model — and the original spec's non-goal — **no computed flag is
stored on the leaf**. `@examine` continues to show an attribute's own flag
symbols only. So a leaf inheriting `cmdsyntax` from its branch formats as code
while showing no `x` symbol of its own.

That is mildly surprising and must be documented in the help text. The
alternative, materialising inherited flags into the display, would diverge from
how every other SharpMUSH flag reports and would make `@decompile` emit flags
the player never set.

## Testing

- Each row of the worked example above, asserted end to end through `@examine`.
- Nearest-wins specifically: a `funsyntax` leaf under a `cmdsyntax` branch
  formats as a *function*, proving the walk stops at the first flag rather than
  taking the outermost or accumulating.
- Re-enabling beneath `no_syntax`, which is the case the rejected
  "no_syntax always wins" rule could not express.
- Object-parent inheritance of all three flags, and the two axes together: a
  flagged attribute inherited from a parent object, whose inheriting object's
  branch carries a different flag.
- `no_syntax` suppresses the set-time parse warning.
- A leaf inheriting a flag shows no symbol of its own in `@examine` — pinning
  the deliberate display choice above.
- Fail-first on every one of these; six unfalsifiable tests were caught on the
  first syntax-flags branch, and the resolution walk is exactly the kind of code
  a vacuous test would sail past.

## Risks

**Behaviour change for anyone already using the flags.** An attribute under a
flagged branch starts formatting where it previously did not. The feature is new
and unmerged, so the exposure is small, but it lands in the same release.

**Cost.** One ancestor walk per displayed attribute, on top of the read gate's.
Both draw on the same cached `GetAttributeQuery`, so the second walk should be
cache hits — worth confirming rather than assuming.
