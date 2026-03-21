# LocateService vs PennMUSH locate/match – Behavior & Code-quality Comparison

## Overview

This document is a full behavior and code-quality comparison between PennMUSH's
`match.c` / `locate_object` logic and SharpMUSH's `LocateService`.

| SharpMUSH | PennMUSH reference |
|---|---|
| `SharpMUSH.Library/Services/LocateService.cs` | `src/match.c` |
| `SharpMUSH.Library/Services/Interfaces/ILocateService.cs` (LocateFlags) | `hdrs/match.h` |
| `SharpMUSH.Implementation/Functions/DbrefFunctions.cs` (locate()) | `src/fundb.c` |
| `SharpMUSH.Implementation/Handlers/LocateObjectQueryHandler.cs` | lock evaluation callers |
| `SharpMUSH.Library/Services/PermissionService.cs` | `src/access.c` |

---

## 1. Flag Mapping Table

| PennMUSH Flag | SharpMUSH `LocateFlags` | Status | Notes |
|---|---|---|---|
| `MAT_ME` | `MatchMeForLooker` | ✅ | |
| `MAT_HERE` | `MatchHereForLookerLocation` | ✅ | |
| `MAT_ABSOLUTE` | `AbsoluteMatch` | ✅ | |
| `MAT_PLAYER` | `PlayersPreference` | ✅ | |
| `MAT_NEIGHBOR` | `MatchObjectsInLookerLocation` | ✅ | |
| `MAT_POSSESSION` | `MatchObjectsInLookerInventory` | ⚠️ | See §3.7 – requires `MatchRemoteContents` to work alone |
| `MAT_EXIT` | `ExitsPreference` | ✅ | |
| `MAT_ENGLISH` | `EnglishStyleMatching` | ✅ | |
| `MAT_TYPE` | `OnlyMatchTypePreference` | ✅ | |
| `MAT_NOISY` | `LocateAndNotifyIfInvalid` (method) | ⚠️ | Not a flag; is a separate method in SharpMUSH |
| `MAT_REMOTE_CONTENTS` | `MatchRemoteContents` | ✅ | |
| `MAT_LAST` | `UseLastIfAmbiguous` | ✅ | |
| `MAT_PMATCH` | `MatchOptionalWildCardForPlayerName` | ✅ | |
| `MAT_EVERYTHING` | `All` (composite) | ⚠️ | `All` is missing `MatchRemoteContents`; see §3.5 |
| `MAT_CONTROL` | `OnlyMatchLookerControlledObjects` | ⚠️ | Checks wrong object in `Matched()`; see §3.4 |
| `MAT_NEAR` | `OnlyMatchObjectsInLookerLocation` | ✅ | |
| `MAT_GLOBAL` | embedded in `LocateMatch` | ⚠️ | Not a standalone flag; master-room exit search is hardcoded inside `LocateMatch` |
| `MAT_REMOTES` | `MatchRemoteContents` | ✅ | |
| `MAT_NOFAIL` | `NoVisibilityCheck` | ⚠️ | Semantically different – PennMUSH suppresses failure messages; SharpMUSH skips visibility entirely |
| `MAT_INSIDE` | `ExitsInsideOfLooker` | ✅ | |

---

## 2. Behavioral Comparison: `locate_object` / `LocateMatch`

| Step | PennMUSH behavior | SharpMUSH behavior | Status |
|---|---|---|---|
| Default flag injection | Adds `MAT_EVERYTHING` when no type/location flags set | Same at top of `Locate()` (lines 91–97) | ✅ |
| Looker permission guard | Returns `#-1 NOT PERMITTED` if executor not nearby/controlling looker | Same check in `Locate()` (lines 99–108) | ✅ |
| "me" matching | `MAT_ME` + name=="me", blocked by `MAT_TYPE` | `MatchMeForLooker` + `!NoTypePreference` | ✅ |
| "here" matching | `MAT_HERE` + name=="here" | `MatchHereForLookerLocation` branch | ✅ |
| Player wildcard (`*name`) | `MAT_PMATCH` or `MAT_PLAYER` + `*` → `lookup_player` | `GetPlayerQuery` via mediator | ✅ |
| Absolute dbref (`#N`) | `MAT_ABSOLUTE` → direct dbref lookup, bypasses visibility | `AbsoluteMatch` + `ParseDbRef` | ✅ |
| Absolute dbref visibility bypass | Always bypasses even without `MAT_ABSOLUTE` for `#N` | `HelperFunctions.ParseDbRef(name).IsSome()` bypass | ✅ |
| English-style parsing | `parse_english()` strips "my/here/this/toward" prefixes, parses ordinals | `ParseEnglish()` in `LocateService` | ✅ (ordinal bug fixed) |
| Inventory match | Iterates `Contents(where)` | `GetContentsQuery(where.AsContainer)` | ⚠️ requires `MatchRemoteContents` (§3.7) |
| Location match | Iterates `Contents(Location(where))` | `GetContentsQuery(location)` | ✅ |
| Exit match (room exits) | Iterates exits of current room | Filtered `GetContentsQuery` for exits | ✅ |
| Zone Master Room exits | Checks zone of location for ZMR, iterates its exits | Under `MatchRemoteContents` | ✅ |
| Master Room exits | Iterates exits of `MASTER_ROOM` | Uses `configuration.CurrentValue.Database.MasterRoom` | ✅ |
| Exits inside looker | When looker is a room, iterates its exits | `ExitsInsideOfLooker` branch | ✅ |
| Partial name matching | `string_prefix()` – prefix match | `StartsWith()` after fix | ✅ Fixed (§1.1) |
| Exact vs. partial match tracking | `full` flag, `curr`/`exact`/`right_type` counters | Same variables in `Matched()` | ✅ |
| Ambiguity resolution | Returns `#-2 WHICH ONE` | `Errors.ErrorAmbiguous` | ✅ |
| Visibility check post-locate | `Dark`, `Light`, `CanInteract(See)`, `CanExamine` | Same in `Locate()` after `LocateMatch` | ✅ |
| `choose_thing` / `ChooseThing` | Prefers type-matching; uses `CouldDoIt` for lock | `ChooseThing` | ✅ |
| `nearby()` | Same-room or one-contains-other | `Nearby()` static method | ✅ |
| Visibility gate in `match_list` | `can_interact` → skip object if blocked | `CanInteract` check with `continue` | ✅ Fixed (§1.2) |

---

## 3. Confirmed Bugs Fixed During This Analysis

### 3.1 Partial-match (`string_match`) used `Equals` instead of `StartsWith` ✅ Fixed

**File:** `LocateService.cs`, `Match_List`.

PennMUSH's `string_match()` is a *prefix* comparison. The SharpMUSH partial-match
branch checked `cur.Object().Name.Equals(name, ...)`, which is identical to the
exact-match branch above it, making the partial-match branch **dead code**.

**Fix:** Changed to `StartsWith`. **Tests added:** `MatchList_PartialMatching_*`.

---

### 3.2 `CanInteract` visibility gate had `continue` commented out ✅ Fixed

**File:** `LocateService.cs`, `Match_List`.

The `continue` statement inside the `CanInteract` failure branch was commented out.
While functionally coincidentally equivalent (no code after the `else if` chain),
the intent was hidden and future additions could silently break visibility filtering.

**Fix:** `continue` reinstated.

---

### 3.3 `ParseEnglish` ordinal validation logic was broken ✅ Fixed

**File:** `LocateService.cs`, `ParseEnglish`.

Two bugs in the ordinal validation:

1. **`Enumerable.Range(10, 14)` generates 10–23** (not 11–13 as intended).  
   `Range(start, count)`, so `Range(10, 14)` = {10,11,12,...,23}.  
   Only 11–13 require the teen "th" exception.

2. **`|| ordinal != "th"` at the end** made ALL non-"th" ordinals invalid  
   (dead code: "1st", "2nd", "3rd" always failed the last clause).

3. **No teen exclusion on `mod10 == 1/2/3` checks**: "11th" would fail the
   `mod10 == 1 && ordinal != "st"` check without an explicit teen guard.

**Fix:** Replaced the entire condition with a clean `expectedSuffix` pattern
matching PennMUSH's `parse_english()` logic. **Tests added:**
`ParseEnglish_OrdinalValidation_MatchesPennMUSH` (15 cases).

---

### 3.4 `LocateObjectQueryHandler` had a misleading "null parser" comment ✅ Fixed

**File:** `LocateObjectQueryHandler.cs`.

The class comment said "This handler passes null for the parser parameter" and
the code had `#pragma warning disable CS8625` (null-literal suppression), but
the code was actually passing the non-null injected `parser`.  The comment was
misleading and the pragma was unnecessary.

**Fix:** Removed the misleading comment and pragma; clarified the actual behavior.

---

## 4. Remaining Differences / Issues (Not Fixed)

### 4.1 `CanInteract` is partially a stub in `PermissionService`

**File:** `PermissionService.cs`, `CanInteract`.

```csharp
/*
 // Check if objects are in the same location or if 'from' controls 'to'
 ...
 */
return await ValueTask.FromResult(true);
```

The proximity/location check is commented out. PennMUSH's `interact_check`
enforces the `Interact_Lock` and location proximity. Currently SharpMUSH allows
matching objects that PennMUSH would block via the interact lock.

---

### 4.2 `CanGoto` is a stub in `PermissionService`

**File:** `PermissionService.cs`, `CanGoto`.

PennMUSH checks `Leave_Lock` and `Enter_Lock` before allowing exit traversal.
SharpMUSH's `CanGoto` is commented out and returns `true`.  Not directly part
of locate, but affects exit traversal behavior.

---

### 4.3 `OnlyMatchLookerControlledObjects` checks `where` instead of `cur`

**Severity:** Behavioral difference from PennMUSH.

```csharp
// Matched(), lines ~536-539:
if (flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
        && !await permissionService.Controls(looker, where))
```

The check tests whether `looker` controls `where` (the reference object), not
`cur` (the current candidate being tested).  PennMUSH's `MAT_CONTROL` flag
performs a per-candidate check: does the looker control *this* object?

**Recommendation:** Change the check to `permissionService.Controls(looker, cur)`.

---

### 4.4 `MatchObjectsInLookerInventory` without `MatchRemoteContents` searches the room instead of inventory

**Severity:** Behavioral difference from PennMUSH.

PennMUSH `MAT_POSSESSION` searches objects carried by the looker.  In SharpMUSH:

- Lines 285–296: `MatchObjectsInLookerInventory | MatchRemoteContents` (both required) → iterates `Contents(where)` ✓
- Lines 378–386: `MatchObjectsInLookerInventory` alone → adds `location` (the room) as a single candidate ✗

Using `MatchObjectsInLookerInventory` without `MatchRemoteContents` does not
search the player's inventory.

**Recommendation:** Either make lines 285–296 trigger on `MatchObjectsInLookerInventory`
alone, or document that `MatchRemoteContents` is always required.

---

### 4.5 `All` flag combination omits `MatchRemoteContents`

```csharp
All = (MatchMeForLooker | MatchHereForLookerLocation | AbsoluteMatch |
       MatchOptionalWildCardForPlayerName |
       MatchObjectsInLookerLocation | MatchObjectsInLookerInventory |
       ExitsInTheRoomOfLooker | EnglishStyleMatching)
```

`MatchRemoteContents` is not included, so the default "match everything" path
never searches player inventory via the `MatchObjectsInLookerInventory | MatchRemoteContents`
code path (see §4.4).

---

### 4.6 `MatchAgainstLookerLocationName` and `MatchRemoteContents` are mutually exclusive

Lines 299–312:
```csharp
if (flags.HasFlag(LocateFlags.MatchAgainstLookerLocationName)
        && !flags.HasFlag(LocateFlags.MatchRemoteContents)
        && location.Object().DBRef != where.Object().DBRef)
```

The `!MatchRemoteContents` guard skips the location-contents search when remote
matching is active.  PennMUSH's logic is additive; both location contents and
remote contents should be searched when both flags are active.

---

### 4.7 `CreateStream` null-safety inconsistency

Lines 285–293 call `mediator.CreateStream(...).Select(...)` without null guard.
Lines 303–305 use `maybeContents?.Select(...)??...` with null fallback.

If `CreateStream` returns null (e.g., in unit tests with unmocked mediator), lines
285–293 would throw `NullReferenceException`.  The consistent pattern should be
applied throughout.

---

### 4.8 Player alias matching is exact-only; PennMUSH supports prefix aliases

```csharp
cur.IsPlayer && cur.Aliases.Contains(name)
```

PennMUSH applies prefix matching (`string_match`) to player aliases.  SharpMUSH
uses `Contains` (exact match in a collection).  A player with alias "ba" would
be found by searching "b" in PennMUSH but not in SharpMUSH.

---

### 4.9 `MAT_NOFAIL` semantics differ from `NoVisibilityCheck`

PennMUSH's `MAT_NOFAIL` suppresses the "I can't see that here" notification
without skipping the visibility check.  SharpMUSH's `NoVisibilityCheck` skips
the visibility check entirely.  There is no flag to suppress notifications
without bypassing visibility.

---

## 5. Code Quality Observations

| Area | Observation |
|---|---|
| `LocateService.ControlFlow` enum | Public nested enum inside `LocateService`. Should be `internal` or moved to a dedicated file. |
| `Match_List`, `Matched`, `ChooseThing` | Public implementation details; should be `private`/`internal`. |
| `FriendlyWhereIs`, `WhereIs` | Public static utility methods on the service; better as extension methods or in a helper class. |
| `Match_List` return tuple | 6-tuple `(BestMatch, Final, Curr, RightType, Exact, ControlFlow)` is hard to read; a `MatchState` record would improve clarity. |
| `Match_List` parameter count | 11 parameters; same `MatchState` record would reduce this. |
| `right_type` vs `rightType` | snake_case in `LocateMatch`, camelCase in `Match_List` – inconsistent naming within the same class. |
| Magic string `#-1 NOT PERMITTED TO EVALUATE ON LOOKER` | Should use `Errors.*` constants. |
| `Room()` cycle protection | `Room(AnySharpObject)` walks up location chain without a depth limit; only a comment protects against infinite loops. PennMUSH has a depth limit. |
| `LocateServiceCompatibilityTests` skipped tests | 4 tests are still marked `[Skip]` covering: inventory match, type preference, ambiguous matches, room-location match. These represent untested behaviors. |
| `ChooseThing` type preference | Uses `string` comparison for types. PennMUSH uses integer type constants. Fragile if type names change. |
| Parameter naming reversal | `Locate(looker, executor)` passes `(executor, looker)` to `LocateMatch`. The parameter meanings are swapped. Documented but confusing for future maintainers. |

---

## 6. Summary Table

| # | Area | PennMUSH behavior | SharpMUSH state | Status |
|---|---|---|---|---|
| 3.1 | Partial name matching | Prefix (`string_match`) | Was `Equals` (dead code) | ✅ Fixed |
| 3.2 | Visibility gate in `Match_List` | `continue` on `can_interact` fail | `continue` was commented out | ✅ Fixed |
| 3.3 | `ParseEnglish` ordinal validation | Correct teen handling + mod10 checks | Three bugs: wrong Range, trailing `||`, missing teen exclusion | ✅ Fixed |
| 3.4 | `LocateObjectQueryHandler` comment | N/A | Misleading "pass null" comment + unused pragma | ✅ Fixed |
| 4.1 | `CanInteract` proximity check | Enforces Interact_Lock and proximity | Always returns `true` (stub) | ⚠️ Stub |
| 4.2 | `CanGoto` lock check | Checks Leave_Lock / Enter_Lock | Always returns `true` (stub) | ⚠️ Stub |
| 4.3 | `OnlyMatchLookerControlledObjects` | Per-candidate check | Checks `where`, not `cur` | ⚠️ Bug |
| 4.4 | Inventory search without remote flag | Searches contents of looker | Fixed: iterates `where` contents directly | ✅ Fixed |
| 4.5 | `All` flag completeness | Includes remote contents | Fixed: `MatchRemoteContents` added to `All` | ✅ Fixed |
| 4.6 | `MatchAgainstLookerLocationName` exclusion | Additive search | Fixed: removed `!MatchRemoteContents` guard | ✅ Fixed |
| 4.7 | `CreateStream` null safety | N/A | Fixed: `?.` guard applied consistently | ✅ Fixed |
| 4.8 | Player alias prefix matching | Prefix search on aliases | Fixed: `Any(a => a.StartsWith(...))` | ✅ Fixed |
| 4.9 | `MAT_NOFAIL` / `NoVisibilityCheck` | Suppresses notification | Skips visibility check | ⚠️ Semantic diff |
| 5.x | Various code quality | N/A | See §5 | ⚠️ Quality |
