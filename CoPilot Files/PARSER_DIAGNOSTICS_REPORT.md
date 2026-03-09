# Parser Diagnostics Report

**Date:** March 9, 2026  
**Scope:** SharpMUSH ANTLR4 parser — Myrddin BBS v4.0.6 full script (150 lines, 102 executable)  
**Test:** `ParserPerformanceDiagnosticTests.BBSScript_ParserPerformanceDiagnostics`

---

## Executive Summary

**Parser errors: 0.** All 102 executable BBS script lines parse without syntax errors in both SLL and LL prediction modes. Both modes produce identical parse results. SLL mode is **~157–254× faster** than LL mode and can safely be used as the default.

---

## 1. Parser Error Status

### Current Status: ✅ Zero Parser Errors

All 8 original BBS parser error lines have been resolved through a series of fixes:

| Fix | Lines Resolved | Root Cause | Solution |
|-----|---------------|------------|----------|
| **Fix A** | 3 (lines 74, 83, 96) | Orphaned `]` from `\[` escapes | `{ inBracketDepth == 0 }?` predicate in `beginGenericText` |
| **Fix B** | 4 (lines 91, 109, 110, 111) | Multi-arg functions inside braces | `inFunctionInsideBrace` counter with save/restore stack |
| **inFunction save/restore** | 2 (lines 57, 101) | Function scope leaking through braces | `savedFunction` Stack in `bracePattern` |

### Documentation Used
- **PennMUSH source code** (`src/parse.c`): Brace behavior research — two brace modes (PE_COMMAND_BRACES vs function argument braces), bracket-in-brace evaluation re-enabling PE_FUNCTION_CHECK
- **PennMUSH headers** (`hdrs/parse.h`): PE_DEFAULT, PE_FUNCTION_CHECK flag definitions
- **ANTLR4 documentation**: Semantic predicates, prediction modes, DiagnosticErrorListener

### Non-Parser Errors Remaining
These are **not** ANTLR parser errors:

1. **Lock evaluator errors** (3 lines: 134, 136, 138): Attribute clear commands like `&bb_read me` — the lock evaluator expects `NAME`/`BIT_FLAG` tokens, not the bare identifier `me`.
2. **Install #-1 false positives** (3): Attribute values containing `#-1` as intentional conditional checks (e.g., `@switch [first(grep(me,*,a))]=#-1,...`).

---

## 2. SLL vs LL Performance Comparison

### Raw Performance Data

| Metric | LL Mode | SLL Mode |
|--------|---------|----------|
| Total parse time (wall clock) | ~4,550ms | ~18-29ms |
| Average per line | ~44,100µs | ~174-281µs |
| Syntax errors | 0 | 0 |
| Full context scans | 675 | 0 |
| Ambiguity reports | 5 | 670 |
| Context sensitivities | 670 | 0 |

### Speedup Factor: **~157–254× faster with SLL**

The massive performance difference is due to LL mode performing 675 full context scans to evaluate semantic predicates like `{ inFunction == 0 }?`. SLL mode avoids these full context scans entirely.

### Mode Agreement: ✅ Identical Results

Both SLL and LL modes produce identical parse results on **every** line. There are zero lines where the modes differ in syntax error output.

### Recommendation

**SLL mode can safely be used as the default prediction mode.** All BBS script lines parse identically in both modes with zero syntax errors. SLL mode provides a significant performance advantage without sacrificing correctness.

---

## 3. Full Context Scan Analysis

### What Are Full Context Scans?

Full context scans occur in LL mode when ANTLR4's prediction algorithm encounters semantic predicates (like `{ inFunction == 0 }?`) that depend on parser state at parse time. ANTLR must evaluate these predicates in the full parser context to determine which alternative to choose.

### Full Context Scan Statistics (LL Mode)

- **Total scans:** 675
- **Lines affected:** 99 / 102 (97.1%)
- **Lines without scans:** 3 (only the simplest lines)

### By Grammar Rule

| Rule | Scans | Lines | Description |
|------|-------|-------|-------------|
| `explicitEvaluationString` | 670 | 99 | Predicate-based alternatives in generic text classification |
| `function` | 5 | 5 | Function rule ambiguity with optional empty args |

### Top 10 Lines by Full Context Scan Count

| Line | Scans | Content Preview |
|------|-------|----------------|
| 95 | 39 | `&CMD_+BBMOVE` — complex @switch with multiple function calls |
| 83 | 31 | `&CMD_+BBREAD2` — nested @switch with iter() |
| 74 | 29 | `&CMD_+BBLOCK` — nested conditional with member() |
| 96 | 25 | `&CMD_+BBWRITELOCK` — nested conditional with member() |
| 57 | 22 | `&TR_POST_NOTIFY` — iter() with remove() and pemit() |
| 50 | 18 | `&TR_TIMEOUT_GROUP` — @dolist with extract() and get() |
| 75 | 17 | `&CMD_+BBCLEARGROUP` — conditional with hasflag() |
| 78 | 16 | `&CMD_+BBPOST` — member() with valid_groups |
| 97 | 16 | `&CMD_+BBPOST2` — hasattr() conditional |
| 28 | 15 | `&VALID_GROUPS` — iter() with switch() |

### Root Cause

The `explicitEvaluationString` rule has 670 context sensitivity events all predicting alternative 1. This is the `beginGenericText` rule which uses semantic predicates:

```antlr
beginGenericText:
      { inFunction == 0 }? CPAREN
    | { inBracketDepth == 0 }? CBRACK
    | { !inCommandList || inBraceDepth > 0 }? SEMICOLON
    | { (!lookingForCommandArgCommas && inFunction == 0) || (inBraceDepth > 0 && inFunctionInsideBrace == 0) }? COMMAWS
    | { !lookingForCommandArgEquals }? EQUALS
    | { !lookingForRegisterCaret }? CCARET
    | (escapedText|OPAREN|OTHER|ansi)
;
```

When ANTLR4 encounters tokens like `)`, `]`, `;`, `,`, `=`, `^` in the input, it must check whether the token should be treated as generic text or as a structural delimiter. The semantic predicates are evaluated in full context, causing the scan events.

**This is correct behavior, not a performance bug.** The predicates are necessary for context-sensitive parsing where the meaning of `)` depends on whether we're inside a function call.

---

## 4. Ambiguity Analysis

### Ambiguity Statistics (LL Mode)

- **Total ambiguity reports:** 5
- **All in rule:** `function`
- **Type:** All inexact (not exact ambiguities)

### Root Cause

The `function` rule has a known ANTLR4 warning:

```
SharpMUSHParser.g4(88,1): warning ANT01: warning(154): rule function contains 
an optional block with at least one alternative that can match an empty string
```

This stems from the grammar:

```antlr
function:
    FUNCHAR {++inFunction; ++inFunctionInsideBrace;}
    (evaluationString? (COMMAWS evaluationString?)*)?
    CPAREN {--inFunction; --inFunctionInsideBrace;}
;
```

The `evaluationString?` alternatives can match empty strings, creating ambiguity for ANTLR about whether to enter the optional block or skip it. In practice, this ambiguity is benign — the parser always produces the correct parse tree.

### Impact

These 5 ambiguity reports (across 102 lines) are:
- **Benign:** They don't cause incorrect parsing
- **Rare:** Only 5 occurrences across the entire BBS script
- **Expected:** They arise from the inherent optional-empty-match pattern in function arguments

---

## 5. Context Sensitivity Analysis

### Context Sensitivity Statistics (LL Mode)

- **Total events:** 670
- **Lines affected:** 99
- **All in rule:** `explicitEvaluationString`
- **All predicting:** alternative 1 (the `beginGenericText` path)

### Interpretation

Context sensitivity events occur when ANTLR4's LL prediction mode determines that the choice between alternatives depends on the parser context (semantic predicates). All 670 events are in `explicitEvaluationString` predicting alternative 1, which means the parser is consistently choosing `beginGenericText` after evaluating predicates.

This pattern is expected and correct — the predicates in `beginGenericText` are designed to make context-dependent decisions about token classification.

---

## 6. Grammar Architecture

### Current Parser Grammar Structure

```
startCommandString → commandList → command → evaluationString
                                                 |
                                                 +→ function explicitEvaluationString?
                                                 +→ explicitEvaluationString
                                                      |
                                                      +→ bracketPattern → OBRACK evaluationString CBRACK
                                                      +→ bracePattern → OBRACE evaluationString? CBRACE
                                                      +→ PERCENT validSubstitution
                                                      +→ beginGenericText (semantic predicates)
                                                      +→ function → FUNCHAR args CPAREN
```

### Semantic Predicates (Context-Sensitive Parsing)

| Predicate | Rule | Purpose |
|-----------|------|---------|
| `{ inFunction == 0 }?` | `beginGenericText` CPAREN | `)` is generic text only outside functions |
| `{ inBracketDepth == 0 }?` | `beginGenericText` CBRACK | `]` is generic text only outside brackets |
| `{ !inCommandList \|\| inBraceDepth > 0 }?` | `beginGenericText` SEMICOLON | `;` separates commands only at top level |
| `{ (!lookingForCommandArgCommas && inFunction == 0) \|\| (inBraceDepth > 0 && inFunctionInsideBrace == 0) }?` | `beginGenericText` COMMAWS | `,` is separator only in function args or command args |
| `{ !lookingForCommandArgEquals }?` | `beginGenericText` EQUALS | `=` is separator only in eq-split commands |
| `{ !lookingForRegisterCaret }?` | `beginGenericText` CCARET | `^` is separator only in register subs |
| `{ inBraceDepth == 0 }?` | `commandList` SEMICOLON | Semicolons don't separate commands inside braces |

### State Tracking Variables

| Variable | Type | Purpose |
|----------|------|---------|
| `inFunction` | `int` | Tracks function nesting depth |
| `inBraceDepth` | `int` | Tracks brace nesting depth |
| `inBracketDepth` | `int` | Tracks bracket nesting depth |
| `inFunctionInsideBrace` | `int` | Tracks functions started inside braces |
| `savedFunctionInsideBrace` | `Stack<int>` | Saves/restores inFunctionInsideBrace across brace boundaries |
| `savedFunction` | `Stack<int>` | Saves/restores inFunction across brace boundaries |
| `inCommandList` | `bool` | Whether parsing a semicolon-delimited command list |
| `lookingForCommandArgCommas` | `bool` | Whether commas are command arg separators |
| `lookingForCommandArgEquals` | `bool` | Whether equals is a command eq-split separator |
| `lookingForRegisterCaret` | `bool` | Whether caret is a register substitution terminator |

---

## 7. Changes Made and Why

### Fix A: Bracket Depth Tracking
- **Files:** `SharpMUSHParser.g4` (added `inBracketDepth` counter)
- **What:** Added `{ inBracketDepth == 0 }?` predicate to prevent orphaned `]` from `\[` escape sequences from being treated as bracket-close tokens
- **Why:** When `\[` is lexed as ESCAPE+ANY, the matching `]` has no opening bracket and would cause a parse error
- **Fixed:** Lines 74, 83, 96
- **Documentation:** ANTLR4 escape mode analysis showing how `\[` is consumed by lexer ESCAPING mode

### Fix B: Brace Function Semantics
- **Files:** `SharpMUSHParser.g4` (added `inFunctionInsideBrace` counter and save/restore stack), `SharpMUSHParserVisitor.cs` (added `_suppressFunctionEval`, `IsInsideFunctionArg`, bracket save/restore)
- **What:** Tracks functions that start inside braces separately, allowing proper comma/paren handling
- **Why:** PennMUSH function argument braces suppress function evaluation but still parse function structure
- **Fixed:** Lines 91, 109, 110, 111
- **Documentation:** PennMUSH `src/parse.c` case `{` handler showing PE_COMMAND_BRACES vs function argument brace modes

### inFunction Save/Restore in bracePattern
- **Files:** `SharpMUSHParser.g4` (added `savedFunction` Stack, save/restore in `bracePattern`)
- **What:** Saves and restores `inFunction` counter when entering/exiting braces (reset to 0 on entry)
- **Why:** In PennMUSH, braces isolate function scope — `)` inside braces should not close functions from outer scope
- **Fixed:** Lines 57, 101
- **Documentation:** PennMUSH `src/parse.c` showing tflags=PT_BRACE isolating scope; removal of Fix C (inParenDepth) and Fix D (savedParenDepth) which tracked bare OPAREN tokens incorrectly

### split() Empty Input Fix
- **Files:** `SharpMUSH.MarkupString/MarkupStringModule.fs` (split function)
- **What:** Changed `MModule.split()` to return empty array `[||]` for empty input instead of `[| empty |]`
- **Why:** PennMUSH `iter(,pattern)` produces no output and `words("")` returns 0; the old behavior caused phantom iterations in `iter()` leading to the "6 U" BBS bug
- **Fixed:** BBS +bbread runtime errors (name=#-1, get=#-1 BAD ARGUMENT)
- **Documentation:** PennMUSH `iter()` and `words()` behavior testing

---

## 8. Test Infrastructure

### Diagnostic Tests Available

| Test | Purpose |
|------|---------|
| `AntlrParserErrorAnalysis.AnalyzeAllBBSLinesForParserErrors` | Validates 0 ANTLR parser errors on BBS script |
| `AntlrParseTreeDiagnosticTests.FullContextScan_Analysis` | Checks common MUSH patterns for full context scans |
| `AntlrParseTreeDiagnosticTests.ParseTree_*` | Parse tree visualization for specific patterns |
| `ParserPerformanceDiagnosticTests.BBSScript_ParserPerformanceDiagnostics` | **NEW** — Full SLL vs LL comparison across entire BBS script |

### How to Run

```bash
# Run all parser performance diagnostics
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ParserPerformanceDiagnosticTests/*" --output detailed

# Run BBS parser error analysis
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AntlrParserErrorAnalysis/*" --output detailed

# Run full context scan analysis
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AntlrParseTreeDiagnosticTests/FullContextScan_Analysis" --output detailed
```

---

## 9. Conclusions

1. **Parser errors: 0.** All BBS script lines parse without syntax errors.
2. **SLL mode is safe.** Both SLL and LL modes produce identical results on every line.
3. **SLL is ~157–254× faster** than LL mode due to avoiding full context scans.
4. **Full context scans in LL mode are expected** — 675 scans across 99/102 lines, caused by semantic predicates in `beginGenericText`. These are correct behavior, not a performance bug.
5. **5 ambiguity reports** in the `function` rule are benign (from optional empty-match pattern).
6. **670 context sensitivity events** in `explicitEvaluationString` all predict the same alternative, confirming the predicates are working correctly and consistently.
