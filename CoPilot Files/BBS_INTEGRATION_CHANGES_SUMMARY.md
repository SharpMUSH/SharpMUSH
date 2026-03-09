# BBS Integration — Complete Changes Summary

## Overview

This document describes all changes made during the Myrddin BBS v4.0.6 integration
test effort. The goal was to load and run a real-world MUSHCode package (Myrddin's
Bulletin Board System) through the SharpMUSH parser, identify all failures, diagnose
their root causes, and fix them with surgical, proven changes.

**Result:** All 2287 tests pass, 0 failures. BBS installation runs cleanly with 0
ANTLR parser errors and 0 runtime `#-1` errors from `+bbread`.

---

## Table of Contents

1. [Production Code Changes](#1-production-code-changes)
   - 1.1 [Fix A: Bracket Depth Tracking](#11-fix-a-bracket-depth-tracking)
   - 1.2 [Fix B: Brace Function Semantics](#12-fix-b-brace-function-semantics)
   - 1.3 [Fix C → Removed: Paren Depth Tracking](#13-fix-c--removed-paren-depth-tracking)
   - 1.4 [inFunction Save/Restore in bracePattern](#14-infunction-saverestore-in-bracepattern)
   - 1.5 [Fix: @tel Command ToString](#15-fix-tel-command-tostring)
   - 1.6 [Fix: get() Function Locate Flags](#16-fix-get-function-locate-flags)
   - 1.7 [Fix: ArangoDB SetObjectName](#17-fix-arangodb-setobjectname)
   - 1.8 [Fix: split() Empty Input (Root Cause of "6 U")](#18-fix-split-empty-input-root-cause-of-6-u)
2. [Test Files Added](#2-test-files-added)
3. [Documentation Files Added](#3-documentation-files-added)
4. [Evidence and Verification](#4-evidence-and-verification)
5. [The "6 U" Mystery — Fully Explained](#5-the-6-u-mystery--fully-explained)

---

## 1. Production Code Changes

### 1.1 Fix A: Bracket Depth Tracking

**File:** `SharpMUSH.Parser.Generated/SharpMUSHParser.g4`

**Problem:** MUSH escape sequences like `\[` produce an escaped opening bracket, but
the matching `]` (CBRACK) has no corresponding opener. The parser's `bracketPattern`
rule matched these orphaned `]` tokens, producing incorrect parse trees.

**Change:** Added `inBracketDepth` counter to `@parser::members`. The `bracketPattern`
rule increments on entry (`++inBracketDepth`) and decrements on exit
(`--inBracketDepth`). A new predicate `{ inBracketDepth == 0 }? CBRACK` in
`beginGenericText` ensures orphaned `]` tokens outside brackets become generic text
instead of causing parse errors.

**Lines fixed:** BBS lines 74, 83, 96 (3 of 8 original error lines).

**Evidence:** These lines contain patterns like `\[header\]` where `\[` is escaped but
`]` is a bare CBRACK token. Without bracket depth tracking, the CBRACK was consumed
by the grammar as a bracket-pattern closer, but there was no opener to match.

**PennMUSH reference:** PennMUSH's `parse.c` uses `PT_BRACKET` terminator flag — only
`]` terminates inside `[...]`. Outside brackets, `]` is literal text.

---

### 1.2 Fix B: Brace Function Semantics

**Files:** `SharpMUSH.Parser.Generated/SharpMUSHParser.g4`,
`SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs`

**Problem:** The `function` rule had a `{inBraceDepth == 0}?` predicate on COMMAWS,
which prevented multi-argument function calls from being recognized inside braces.
This caused BBS patterns like `{setq(0,get(%0/LAST_MOD))}` to fail — `get(%0/LAST_MOD)`
was not recognized as a function call because commas were treated as generic text.

**Grammar changes (3):**
1. Removed `{inBraceDepth == 0}?` from the function rule's COMMAWS alternative.
   Added `++inFunctionInsideBrace` / `--inFunctionInsideBrace` tracking.
2. Changed `bracePattern` from `explicitEvaluationString` to `evaluationString` with
   `inFunctionInsideBrace` save/restore stack (reset to 0 on entry, restore on exit).
3. Updated `beginGenericText` COMMAWS predicate from
   `(!lookingForCommandArgCommas && inFunction == 0) || inBraceDepth > 0`
   to `(!lookingForCommandArgCommas && inFunction == 0) || (inBraceDepth > 0 && inFunctionInsideBrace == 0)`.

**Visitor changes (3):**
1. Added `_suppressFunctionEval` counter. In `VisitFunction`, when
   `_suppressFunctionEval > 0`, return literal text instead of evaluating.
2. Added `IsInsideFunctionArg(context)` ancestry check in `VisitBracePattern`.
   When a brace is inside a function argument, increment `_suppressFunctionEval`.
3. In `VisitBracketPattern`, save and clear `_suppressFunctionEval` (set to 0) so
   functions inside `[...]` brackets work even within suppressed braces.

**Lines fixed:** BBS lines 91, 109, 110, 111 (4 of 8 original error lines).

**PennMUSH reference:** PennMUSH `src/parse.c` has two brace modes:
- **Command braces** (`PE_COMMAND_BRACES`): preserve `PE_FUNCTION_CHECK`, functions
  work normally inside.
- **Function-argument braces**: remove `PE_FUNCTION_CHECK`, functions NOT recognized,
  but `%` substitutions and `[...]` evaluation still work.
- `[...]` RE-ENABLES `PE_FUNCTION_CHECK` via
  `eflags | PE_FUNCTION_CHECK | PE_FUNCTION_MANDATORY`.
- Both modes use `tflags=PT_BRACE` so only `}` terminates.

**Documentation:** See `CoPilot Files/FIX_B_PENNMUSH_BRACE_RESEARCH.md` and
`CoPilot Files/FIX_B_ANTLR4_RECOMMENDATIONS.md` for full analysis.

---

### 1.3 Fix C → Removed: Paren Depth Tracking

**File:** `SharpMUSH.Parser.Generated/SharpMUSHParser.g4`

**History:** Fix C added `inParenDepth` to track bare `(` tokens (OPAREN) in generic
text, with a modified CPAREN predicate `{ inFunction == 0 || inParenDepth > 0 }?`.
Fix D added `savedParenDepth` stack to isolate `inParenDepth` in `bracketPattern`.

**Why removed:** `inParenDepth` doesn't match PennMUSH behavior. In PennMUSH, `)` 
**always** closes the innermost function call outside braces. Bare parentheses in text
like `(New BB message)` don't affect function closure. The `inParenDepth` counter
caused scope leakage where bare `(` in outer text elevated the counter, and then `)`
inside bracket-pattern function calls was incorrectly consumed as generic text.

**Replacement:** Instead of tracking bare parens, `inFunction` is now saved/restored
in `bracePattern` (reset to 0 on entry, restore on exit) via a `savedFunction` Stack.
This matches PennMUSH where braces isolate function scope — `)` inside braces cannot
close a function that started outside braces.

**CPAREN predicate reverted to:** `{ inFunction == 0 }?` (the original, simpler form).

**Lines fixed:** BBS lines 57 and 101 (resolved by inFunction save/restore, not
by inParenDepth tracking).

**PennMUSH reference:** PennMUSH `src/parse.c` — the `PT_BRACE` terminator flag means
only `}` terminates inside braces. Function nesting (`inFunction`) is isolated per
brace scope because each brace starts a fresh `process_expression()` call.

---

### 1.4 inFunction Save/Restore in bracePattern

**File:** `SharpMUSH.Parser.Generated/SharpMUSHParser.g4`

**Change:** The `bracePattern` rule now saves `inFunction` to a `savedFunction` Stack
on entry (reset to 0) and restores it on exit. This prevents function nesting from
leaking across brace boundaries.

```
bracePattern:
    OBRACE {
        ++inBraceDepth;
        savedFunctionInsideBrace.Push(inFunctionInsideBrace);
        inFunctionInsideBrace = 0;
        savedFunction.Push(inFunction);
        inFunction = 0;
    }
    evaluationString?
    CBRACE {
        --inBraceDepth;
        inFunctionInsideBrace = savedFunctionInsideBrace.Pop();
        inFunction = savedFunction.Pop();
    }
;
```

**Why:** Without this, if a function call like `setq(0,...)` starts before a brace
pattern, the `inFunction` counter remains elevated inside the brace. Any `)` inside
the brace that closes a function within the brace would also decrement `inFunction`,
potentially going below the level expected by the outer function.

---

### 1.5 Fix: @tel Command ToString

**File:** `SharpMUSH.Implementation/Commands/GeneralCommands.cs`

**Problem:** The `@tel` command used `executor.ToString()` which calls `ToString()` on
a `OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>` discriminated union. This
returns a type description string like `"OneOf { ... }"` rather than a dbref like
`"#1"`. The locate service could not find this string, causing `@tel` to silently fail.

**Change 1:** `executor.ToString()` → `executor.Object().DBRef.ToString()` to get the
actual dbref string like `"#1"`.

**Change 2:** `toTeleportList.Select(x => x.ToString())` → proper `OneOf` matching:
```csharp
toTeleportList.Select(x => x.Match(
    dbref => dbref.ToString(),
    str => str))
```
This handles both cases: when the teleport target is already a parsed DBRef, extract
its string; when it's a name string, use it directly.

**Evidence:** The `TelDiagnosticTests.TelByName` and `TelByDbRef` tests prove @tel
works correctly after this fix. Before the fix, the locate service received
`"OneOf { ... }"` which is not a valid object name or dbref.

---

### 1.6 Fix: get() Function Locate Flags

**File:** `SharpMUSH.Implementation/Functions/AttributeFunctions.cs`

**Problem:** `get()` used `LocatePlayerAndNotifyIfInvalidWithCallStateFunction` which
only locates player objects. When BBS code does `get(#123/ATTR)` where `#123` is a
Thing (not a Player), the locate fails with `#-1 NO SUCH OBJECT VISIBLE`.

**Change:** Changed to `LocateAndNotifyIfInvalidWithCallStateFunction` with
`LocateFlags.All`, which searches all object types (players, rooms, exits, things).

**Evidence:** BBS group objects are Things, not Players. The old code couldn't find
them, causing `get(groupobj/LAST_MOD)` to fail with `#-1`.

---

### 1.7 Fix: ArangoDB SetObjectName

**File:** `SharpMUSH.Database.ArangoDB/ArangoDatabase.Objects.cs`

**Problem:** `SetObjectName` used `Id = obj.Object().Id` for the ArangoDB document key,
but ArangoDB expects the `_key` field. Also, `Name = value` passed a `MarkupString`
where ArangoDB needs a plain string.

**Change:**
- `Id = obj.Object().Id` → `_key = obj.Object().Key.ToString()`
- `Name = value` → `Name = MModule.plainText(value)`

**Evidence:** Without this fix, `@name` and related object renaming operations would
fail with ArangoDB serialization errors.

---

### 1.8 Fix: split() Empty Input (Root Cause of "6 U")

**File:** `SharpMUSH.MarkupString/MarkupStringModule.fs`

**Problem:** `MModule.split()` returned `[| ams |]` (a single-element array containing
the empty string) when given empty input. This caused `iter()` to produce **one
phantom iteration** with an empty value, even when the input list was empty.

**The failure chain:**
1. BBS `+bbread` calls `lattr(groupobj/HDR_LINE_*)` on a group with no messages
2. `lattr()` correctly returns empty string (no matching attributes)
3. `iter(emptyString, pattern)` calls `split(" ", "")` to tokenize the list
4. **Bug:** `split()` returns `[| "" |]` instead of `[||]`
5. `iter()` sees one element (empty string), executes one phantom iteration
6. Pattern contains `name("")` → `#-1 CAN'T SEE THAT HERE`
7. Pattern contains `get(/LAST_MOD)` → `#-1 BAD ARGUMENT FORMAT TO GET`
8. Error string `#-1 CAN'T SEE THAT HERE` is displayed as message count

**Change:** Single line: `[| ams |]` → `[||]` (return empty array for empty input).

**PennMUSH reference:** In PennMUSH, `iter(,pattern)` produces no output and
`words("")` returns `0`. An empty input list means zero iterations, not one.

**This is the root cause of the "6 U" mystery:** `words(#-1 CAN'T SEE THAT HERE)`
returns `6` because the error string has 6 space-separated words. The `U` comes from
a subsequent `switch()` falling through to the "unread" marker. The fix eliminates the
phantom iteration entirely, so no error strings are ever generated.

---

## 2. Test Files Added

| File | Tests | Purpose |
|------|-------|---------|
| `SharpMUSH.Tests/Integration/MyrddinBBSIntegrationTests.cs` | 1 | End-to-end BBS install + `+bbread` verification |
| `SharpMUSH.Tests/Integration/AntlrParserErrorAnalysis.cs` | 1 | Counts ANTLR parser errors per BBS line (asserts 0) |
| `SharpMUSH.Tests/Integration/AntlrParseTreeDiagnosticTests.cs` | ~10 | Diagnostic tests for specific parser edge cases |
| `SharpMUSH.Tests/Commands/MovementCommandTests.cs` | 4 | @tel command unit tests |
| `SharpMUSH.Tests/Commands/TelDiagnosticTests.cs` | 13 | @tel diagnostics + BBS failure chain reproduction |
| `SharpMUSH.Tests/Parser/FunctionUnitTests.cs` | 5 new | Fix B test cases for brace function semantics |
| `SharpMUSH.Tests/Functions/LambdaUnitTests.cs` | 2 updated | Extra trailing paren behavior |
| `SharpMUSH.Tests/Integration/TestData/MyrddinBBS_v406.txt` | — | BBS installer script (150 lines) |

---

## 3. Documentation Files Added

| File | Lines | Purpose |
|------|-------|---------|
| `CoPilot Files/ANTLR4_PARSER_ERROR_ANALYSIS.md` | 688 | Deep analysis of all 8 parser error lines |
| `CoPilot Files/ANTLR4_FIX_PROPOSALS.md` | 813 | Grammar fix proposals with proven test cases |
| `CoPilot Files/FIX_B_PENNMUSH_BRACE_RESEARCH.md` | 416 | PennMUSH source analysis of brace behavior |
| `CoPilot Files/FIX_B_ANTLR4_RECOMMENDATIONS.md` | 471 | ANTLR4 implementation plan for Fix B |
| `CoPilot Files/FIX_B_BBS_TEST_RESULTS.md` | 200 | Test results after Fix B implementation |
| `CoPilot Files/BBS_INTEGRATION_CHANGES_SUMMARY.md` | this file | Complete changes summary |

---

## 4. Evidence and Verification

### Test Results
- **Total tests:** 2580 (2287 passed, 293 skipped, 0 failed)
- **BBS ANTLR parser errors:** 0 (was 8)
- **BBS +bbread #-1 errors:** 0 (was 1)
- **Lock evaluator errors:** 3 (not parser errors — `me` is not valid lock syntax)

### Proving Each Assumption

| Assumption | How Proven | Test/Evidence |
|-----------|-----------|---------------|
| @tel works by name | `TelDiagnosticTests.TelByName` | Creates room, @tel by name, verify location |
| @tel works by dbref | `TelDiagnosticTests.TelByDbRef` | @tel by #dbref, verify location |
| iter() on empty list = no output | `TelDiagnosticTests.IterOnEmptyString` | `iter(,##)` returns empty |
| words("") = 0 | `TelDiagnosticTests.WordsOnEmptyString` | `words()` returns "0" |
| words(error_string) = 6 | `TelDiagnosticTests.WordsOnErrorString` | `words(#-1 CAN'T SEE THAT HERE)` = "6" |
| name("") = error | `TelDiagnosticTests.NameOnEmptyString` | Returns `#-1` error |
| get(/ATTR) = error | `TelDiagnosticTests.GetWithNoObject` | Returns `#-1` error |
| Phantom iter causes errors | `TelDiagnosticTests.BBSReadFailureChain` | Full chain reproduction |
| split("") = empty array | `TelDiagnosticTests.SplitOnEmptyString` | Verified via iter behavior |
| Fix A: orphaned ] = text | `AntlrParseTreeDiagnosticTests` | Parse tree analysis |
| Fix B: functions in braces | `FunctionUnitTests` lines 24-29 | `add(1,2)` in braces |
| PennMUSH brace semantics | `FIX_B_PENNMUSH_BRACE_RESEARCH.md` | Source code analysis |
| 0 ANTLR parser errors | `AntlrParserErrorAnalysis` | Counts errors per BBS line |

---

## 5. The "6 U" Mystery — Fully Explained

The BBS `+bbread` output showed messages like `6 U` where `6` was the "message count"
and `U` was the "unread marker". This was **not** a real message count.

**Root cause chain:**
1. `lattr(groupobj/HDR_LINE_*)` returns empty (no messages in group)
2. `iter("", get(##/LAST_MOD))` — should produce no output, but...
3. `split(" ", "")` returned `[| "" |]` instead of `[||]` (the bug)
4. `iter` executed ONE phantom iteration with value `""`
5. `name("")` evaluated to `#-1 CAN'T SEE THAT HERE` (6 words)
6. `get(/LAST_MOD)` evaluated to `#-1 BAD ARGUMENT FORMAT TO GET`
7. `words(#-1 CAN'T SEE THAT HERE)` = `6` (counting words in the error string!)
8. The `6` was displayed where the message count should be
9. The `U` came from a `switch()` that marked unread status

**Fix:** `split()` returns `[||]` for empty input → `iter()` produces no output →
no error strings generated → no phantom messages displayed.

**This was proven by:**
- `TelDiagnosticTests.WordsOnErrorString`: `words(#-1 CAN'T SEE THAT HERE)` = `"6"`
- `TelDiagnosticTests.BBSReadFailureChain`: full chain reproduction
- `TelDiagnosticTests.IterOnEmptyString`: `iter(,##)` = `""` (no output)

---

## Summary of Changed Files

### Production Code (6 files, minimal changes)
| File | Lines Changed | Description |
|------|--------------|-------------|
| `SharpMUSHParser.g4` | +13, -4 | Fix A + Fix B grammar + inFunction save/restore |
| `SharpMUSHParserVisitor.cs` | +42, -3 | Fix B visitor (suppress + bracket restore) |
| `GeneralCommands.cs` | +4, -2 | @tel ToString fix |
| `AttributeFunctions.cs` | +2, -1 | get() locate flags |
| `ArangoDatabase.Objects.cs` | +2, -2 | SetObjectName key + plainText |
| `MarkupStringModule.fs` | +1, -1 | split() empty input → empty array |

### Test Code (7 files, 1600+ lines)
All test files follow existing patterns using `ServerWebAppFactory`, `TUnit`,
`NSubstitute`, and `INotifyService` capture for command output verification.

### Documentation (6 files, 2700+ lines)
Research documents with PennMUSH source references, ANTLR4 analysis, fix proposals,
and test results.
