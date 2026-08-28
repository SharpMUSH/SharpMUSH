# Attribute Follow-ups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close four attribute defects deferred from PR #808 — the `xattr` family's argument handling, `@CLONE` silently dropping attribute trees, a fail-open in the single-attribute read path, and a `@parent` cycle that hangs a query.

**Architecture:** Each is independent and small. Three are PennMUSH parity fixes verified against the C source; the fourth is a missing loop guard.

**Tech Stack:** .NET 10, C#, TUnit, ArangoDB / SurrealDB / Memgraph.

**Spec:** none — this plan carries its own findings, each verified against `/home/grave/RiderProjects/SharpMUSH/pennmush` (read-only).

## Global Constraints

- **Parity target is PennMUSH's C source, not its help text.** Penn lives at `/home/grave/RiderProjects/SharpMUSH/pennmush` — **read-only, never modify**. It is buildable if a claim needs live confirmation.
- C# style: **tabs**, indent size 2. On `FORMAT001`, run `dotnet format whitespace --folder <dir> --exclude "**/bin/**" --exclude "**/obj/**"` **twice**.
- **Never `git add -A`.** Sibling worktrees hold the two stacked branches this one sits on.
- `TreatWarningsAsErrors` is on in `SharpMUSH.Library`, `SharpMUSH.Implementation`, `SharpMUSH.Tests`.
- TUnit, not xUnit. `HasCount().EqualTo(n)` is `[Obsolete]` and fails the build — use `Count().IsEqualTo(n)`.
- **Fail-first is mandatory.** Twenty-plus tests were caught across the two predecessor branches being unable to fail. Every test here gets a positive control and must be shown red before its fix.
- Baseline: **5862 total, 0 failed, 5674 succeeded, 188 skipped**.

---

### Task 1: The `xattr` family's argument handling

**Files:**
- Modify: `SharpMUSH.Implementation/Functions/AttributeFunctions.cs` (six sites, below)
- Test: `SharpMUSH.Tests/Functions/AttributeFunctionUnitTests.cs` (extend), `SharpMUSH.Tests/Commands/AttributeTreeWildcardTests.cs:231` (tighten)

**Penn ground truth** — `fun_lattr` (`src/fundb.c:148-212`) backs `XATTR`, `XATTRP`, `REGXATTR`, and `REGXATTRP`; all four dispatch to that one C function (`function.c:707-831`), so their argument handling is *identical by construction*.

- `start` is **1-based**: `fundb.c:96-104` increments `nattr` before testing, and the window is `nattr >= start && nattr < count + start`, so `start=1` includes the first match.
- `start` exceeding the match count → **empty result, no error**.
- The only guard is `start < 1 || count < 1` → `e_argrange` (`fundb.c:167-168`). **There is no relation between `start` and `count`** — `xattr(me/*, 5, 2)` is legal.
- Non-integer → `e_int` (`fundb.c:159-162`).
- SharpMUSH's `ErrorMessages.Returns.Integer` and `.ArgRange` already match Penn's strings verbatim (`ErrorMessages.cs:47,60`) — no string changes.

**The scope is wider than reported.** `xattr`/`xattrp` have both bugs; `regxattr`/`regxattrp` already skip correctly but are **missing the `count < 1` guard entirely**, so `regxattr(me/*, 3, 0)` silently returns empty where Penn errors.

| Function | Guard now | Skip now | Correct guard | Correct skip |
|---|---|---|---|---|
| `xattr` (`~:1831`, `~:1851`) | `startInt > countInt \|\| startInt < 1` | `.Skip(startInt)` | `startInt < 1 \|\| countInt < 1` | `.Skip(startInt - 1)` |
| `xattrp` (`~:1880`, `~:1900`) | same | `.Skip(startInt)` | same | `.Skip(startInt - 1)` |
| `regxattr` (`~:1201`) | `startInt < 1` | already correct | `startInt < 1 \|\| countInt < 1` | unchanged |
| `regxattrp` (`~:1251`) | `startInt < 1` | already correct | `startInt < 1 \|\| countInt < 1` | unchanged |

- [ ] **Step 1: Write the failing tests**

Model them on `AttributeFunctionUnitTests.cs:196-206` (`Test_Regxattr_RangeWithRegex`), which already pins correct 1-based behaviour and is the template.

For **each** of `xattr` and `xattrp`:
- `xattr(obj/*,1,2)` — the **first** match is included (this is the off-by-one).
- `xattr(obj/*,5,2)` where only 3 attributes match — empty result, **no error** (this is the bogus guard).
- `xattr(obj/*,0,2)` → `ArgRange`.
- `xattr(obj/*,1,0)` → `ArgRange`.
- `xattr(obj/*,x,2)` → `Integer`.

For **each** of `regxattr` and `regxattrp`: `count = 0` → `ArgRange`.

Assert on returned **names**, not counts — see step 4 for why.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AttributeFunctionUnitTests/*"`
Expected: the `xattr`/`xattrp` first-match and `start > count` cases FAIL; the `reg*` `count = 0` cases FAIL.

- [ ] **Step 3: Apply the six changes** from the table above.

- [ ] **Step 4: Tighten a test that cannot fail**

`AttributeTreeWildcardTests.cs:231` (`Test_Xattr_RangeInTree`, `xattr(%!/ROOT**,1,2)`) asserts only `attrs.Length == 2`. With the current bug — `Skip(1).Take(2)` over 3 matches — it *coincidentally* also returns 2, so it will **not** turn red and pins nothing. Change it to assert the actual names.

- [ ] **Step 5: Run tests, then commit**

```bash
git add SharpMUSH.Implementation/Functions/AttributeFunctions.cs \
        SharpMUSH.Tests/Functions/AttributeFunctionUnitTests.cs \
        SharpMUSH.Tests/Commands/AttributeTreeWildcardTests.cs
git commit -m "Match Penn's start/count handling across the xattr family"
```

---

### Task 2: `@CLONE` drops attribute trees

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/BuildingCommands.cs:1443-1449`
- Test: `SharpMUSH.Tests/Commands/` (new file)

**Penn ground truth** — `atr_cpy` (`src/attrib.c:1692-1710`) walks the source's **flat, sorted** attribute list; branch/leaf is purely a naming convention over one namespace. Per attribute it checks `AF_Nocopy` alone, then calls `atr_new_add(..., makeroots=false)`.

**That trailing `false` is the crux.** `atr_new_add` (`:756-820`) auto-creates a missing parent only when `makeroots` is true. With it false (`:804-806`) it **silently returns without adding** if the immediate parent isn't already on the destination. Because the list is sorted and `"FOO"` precedes `"FOO\`BAR"`, ordinary trees clone fine — but a `no_clone` **branch** is skipped by `atr_cpy`, and then its leaves find no parent and are dropped too.

So `no_clone` *does* take a subtree down — incidentally, via the missing-root abort, not via any permission walk. Replicating that is the point of step 2 below.

`AL_CREATOR(ptr)` is passed through unchanged (`:1706`), so a cloned attribute keeps its **original creator**, not the cloner.

**SharpMUSH today:** `@CLONE` iterates `obj.Object().Attributes.Value` → `GetTopLevelAttributesAsync` (`ArangoDatabase.Attributes.cs:162-177`), a 1-hop traversal. Every leaf is dropped, flagged or not. `IsNoCopy()` has **zero production callers**.

**Critical:** SharpMUSH's `SetAttributeAsync` **auto-vivifies** missing intermediate nodes (`ArangoDatabase.Attributes.cs:608-675`) — more permissive than Penn. So fixing the enumeration *without* the skip-propagation would newly copy attributes Penn deliberately drops. That would be a regression introduced by the fix.

- [ ] **Step 1: Write the failing tests**

- Plain tree, no flags → branch and leaf both cloned, `LongName` preserved.
- `no_clone` on the **leaf** only → branch cloned, leaf dropped.
- `no_clone` on the **branch**, leaf unflagged → **both** dropped.
- Three levels `A\`B\`C` with `no_clone` on `B` → `A` copied, `B` and `C` dropped.
- Creator preservation: clone as an executor *different* from the attribute's original setter; assert the clone's attribute owner is the **original** creator.

- [ ] **Step 2: Run to verify failure**

Expected: every tree assertion fails — no leaf is cloned at all today.

- [ ] **Step 3: Implement**

1. Replace the depth-1 enumeration with `GetAttributesByRegexAsync(dbref, "**")` (`ArangoDatabase.Attributes.cs:344-387`), which walks `1..99999 OUTBOUND` **and sorts `LongName` ascending** (`:374`) — parent before child, which the skip-propagation depends on. Note its sibling `GetAttributesAsync` has **no** sort and is unsuitable.
2. Track a `skipped` set of `LongName`s. For each attribute in order: if `IsNoCopy()`, add its `LongName` to `skipped` and don't copy. Otherwise compute the immediate parent (`LongName` minus the last backtick segment); if that parent is in `skipped`, add this `LongName` to `skipped` too and don't copy. This replicates Penn's abort-on-missing-root.
3. Preserve the source attribute's owner rather than the executor. `SetAttributeAsync` takes owner from the caller (`AttributeService.cs:961-962`), so this may need a creator-preserving overload — if it does, say so in your report rather than silently using the executor.
4. Keep the existing `_`-prefix skip (`BuildingCommands.cs:1447`) as an orthogonal filter.

- [ ] **Step 4: Run tests and the `*Clone*` gate, then commit**

```bash
git add SharpMUSH.Implementation/Commands/BuildingCommands.cs SharpMUSH.Tests/Commands/
git commit -m "Clone attribute trees, honouring no_clone the way Penn does"
```

---

### Task 3: The single-attribute read path is fail-open

**Files:**
- Modify: `SharpMUSH.Library/Services/AttributeService.cs:87` (`GetAttributeAsync`), `:183` (`LazilyGetAttributeAsync`)
- Test: `SharpMUSH.Tests/Services/AttributeAncestryTests.cs` (extend), plus a new service-level test file

**This is not where the deferred finding said it was.** The pattern paths (`GetAttributePatternAsync`, `FilterLazyAttributes`) are **already correct** for every scenario. The live divergence is in the single-attribute path — the one behind `get()`, `xget()`, `ufun`, and `@examine obj/attr`, which is far more heavily used. It still tests only `result.Attributes`, the path as resolved **on the source object**, and never walks targets outward from `obj`. `AttributeAncestry` isn't called there at all.

**And the model change is not needed.** `AttributeWithInheritance` already carries `SourceObject` and `Source` (`Models/AttributeWithInheritance.cs:45,50`). Blast radius on `SharpAttribute`: **zero** — no construction sites, no provider code.

Three scenarios fail open today (chain: child `#10` → parent `#11` → grandparent `#12`, mortal viewer):

| | Setup | Penn | SharpMUSH now |
|---|---|---|---|
| b | `#10` has `FOO` mortal_dark, no leaf; `#11` has visual `FOO` + `FOO\`BAR` | **deny** at `attrib.c:331` — a prefix present on `obj` that fails returns 0, it does **not** `continue_target` | **allow** |
| d | branch absent on both, leaf on `#11` | **deny** — the path never resolves, falls off the end at `:356` | **allow** |
| e | `#11` has mortal_dark `FOO` only; `#12` has visual `FOO` + leaf | **deny** at `#11` | **allow** — `EvaluateInheritanceCandidateAsync` treats `#11` as incomplete and falls through to `#12` (`ArangoDatabase.Attributes.cs:1192-1209`); only `no_inherit` aborts |

- [ ] **Step 1: Write the failing tests**

Service-level, against `GetAttributeAsync` and `LazilyGetAttributeAsync` with `checkParent: true`, for scenarios b, d, and e. Each needs a positive control proving the `@parent` chain resolves — otherwise a denial could mean "chain broken."

Also extend `AttributeAncestryTests` with scenario **c** (`#10` has visual `FOO`; `#11` has mortal_dark `FOO` + leaf → deny at `#11`), which is handled correctly today but **has no test**.

- [ ] **Step 2: Run to verify failure** — b, d, e must show the attribute being read.

- [ ] **Step 3: Implement**

In `GetAttributeAsync`, for `mode == Read` **only**, replace the bare `permissionPredicate(executor, obj, result.Attributes)` at `:87` with a call to `AttributeAncestry.CanReadAsync`, passing `result.Attributes[^1]`, `result.SourceObject`, the parent chain, and `obj`'s dbref. Everything needed already exists: `ParentChainAsync` (`:606`), `FetchAncestorAsync` (`:635`), `FetchLazyAncestorAsync` (`:650`). Apply the identical change to `LazilyGetAttributeAsync:183` with the lazy overload.

**Three guards are load-bearing — get these wrong and you break working reads:**
- **Flat names short-circuit** when `attributePath.Length == 1`. Penn does the same (`attrib.c:311-312`).
- **Zone and Ancestor sources must skip the walk.** `ParentChainAsync` follows `@parent` only, so a zone-sourced result isn't in the chain and `CanReadAsync` would fall off the end and **deny** — a fail-closed regression on zone reads. Keep the existing predicate for `AttributeSource.Zone`. For the ancestor fall-through (`:71-79`), either skip it or append `ancestorRef` to the chain (Penn does walk the ancestor, `attrib.c:352-353`) — say which you chose.
- **Scope to `Read`.** `Set`/`SystemSet` go through Penn's `can_write_attr_internal`, a different function. `Execute` is defensible (Penn's `fun_ufun` gates on `Can_Read_Attr`) but is a behaviour change beyond this fix — leave it and note it.

- [ ] **Step 4: Run tests plus `Attribute*` and `*Permission*` gates, then commit**

---

### Task 4: A `@parent` cycle hangs the query

**Files:**
- Modify: `SharpMUSH.Implementation/Handlers/Database/GetAttributeQueryHandler.cs:69`
- Test: alongside Task 3's

`GetAttributeQueryHandler.cs:69` walks the `@parent` chain with `while (true)` and **no depth cap and no cycle guard**. `ParentChainAsync` (`AttributeService.cs:612-620`) does it correctly — capped at `Limit.MaxParents`, with a cycle break. A `@parent` cycle hangs the query.

- [ ] **Step 1:** Write a test that builds a `@parent` cycle and reads an attribute through it, asserting it terminates. Give it a timeout so a regression fails rather than hanging the suite.
- [ ] **Step 2:** Confirm it hangs (or times out) before the fix.
- [ ] **Step 3:** Mirror `ParentChainAsync`'s cap and cycle guard.
- [ ] **Step 4:** Run, then commit.

---

## Deferred

- `GetLazyAttributesQueryHandler` ignores `request.CheckParents` entirely, so lazy listing never returns inherited attributes. Documented in-file and fail-closed.
- `CommandAttributeScanner.cs` uses the same case-sensitive flag comparison pattern that PR #808 fixed everywhere else.
- `MushCodeAnalyzer.FormatIndented` wraps its body in `catch (Exception) { return code; }`, indistinguishable from "nothing to format" — wants narrowing and logging.
- `@cpattr`/`@mvattr` may share `@CLONE`'s flat-vs-tree gap; unaudited.
