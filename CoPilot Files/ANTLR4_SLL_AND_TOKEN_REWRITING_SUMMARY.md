# ANTLR4 SLL Mode, Token Stream Rewriting & Syntax Highlighting

This document provides a consolidated summary of parser analysis, grammar fixes, and token rewriting work for the SharpMUSH ANTLR4 parser. It covers the investigation, fixes, and rationale for SLL prediction mode, token stream rewriting of orphaned delimiters, and syntax highlighting/error reporting APIs.

---

## Table of Contents

1. [Problem Overview](#1-problem-overview)
2. [Root Cause Analysis](#2-root-cause-analysis)
3. [Grammar Fixes Implemented](#3-grammar-fixes-implemented)
4. [AdaptivePredict Hang Problem](#4-adaptivepredict-hang-problem)
5. [SLL vs LL Prediction Mode](#5-sll-vs-ll-prediction-mode)
6. [Token Stream Rewriting](#6-token-stream-rewriting)
7. [Syntax Highlighting & Error Reporting](#7-syntax-highlighting--error-reporting)
8. [Test Results](#8-test-results)

---

## 1. Problem Overview

The Myrddin BBS (a complex PennMUSH softcode package, ~150 lines) exposed 8 ANTLR4 parser errors across its executable lines. These errors fell into 3 root causes involving the interaction between escape sequences, brace depth predicates, and parenthesis scoping in the MUSH grammar.

### Error Lines and Root Causes

| Line(s) | Root Cause | Pattern | Resolution |
|---------|-----------|---------|------------|
| 74, 83, 96 | A: Orphaned CBRACK | `\[` → `ESCAPE ANY` but `]` → orphaned `CBRACK` | Token stream rewriting |
| 91, 109, 110, 111 | B: Brace depth predicate | `{inBraceDepth==0}?` blocks multi-arg functions inside braces | Grammar fix (remove predicate) |
| 101 | C: Paren asymmetry | `(` always generic text, but `)` only generic when `inFunction==0` | inFunction save/restore in bracePattern |
| 57 | D: Scope leakage | Fix C's `inParenDepth` leaked into bracket patterns | Removed inParenDepth; use savedFunction Stack |

---

## 2. Root Cause Analysis

### Root Cause A — Escape Sequence Mismatch

The lexer correctly escapes openers (`\[` → `ESCAPE ANY`) but matching closers (`]`) are still tokenized as `CBRACK` with no corresponding `OBRACK`, creating orphaned delimiter tokens that the parser cannot match to any rule.

**Token trace example (`\[or(match(%0,*,|),1,0)\]`):**
```
ESCAPE → \
ANY    → [    ← Not OBRACK, so no bracket pattern opens
...
CBRACK → ]    ← Orphaned: no matching OBRACK exists
```

### Root Cause B — Brace Depth Predicate

The `function` rule's COMMAWS alternative had `{inBraceDepth == 0}?`, blocking function argument splitting inside braces. PennMUSH's `process_expression()` splits function arguments on commas at ALL brace depths — braces only prevent command-level splitting (`;`, command `,`), not function-level splitting.

**Example:** `strcat(a,{add(1,2)},b)` — PennMUSH evaluates `add(1,2)` as a function inside braces, producing `a2b`. The old grammar blocked this.

### Root Cause C — OPAREN/CPAREN Asymmetry

`(` is always consumed as generic text via `beginGenericText`, but `)` is only generic text when `{inFunction == 0}?`. Inside function calls, bare `(text)` patterns cause `)` to prematurely close the enclosing function.

**Resolution:** Rather than tracking `inParenDepth` (which doesn't match PennMUSH behavior), `inFunction` is saved/restored in `bracePattern` via a `savedFunction` Stack. This ensures braces isolate function scope, matching PennMUSH's rule that `)` always closes the innermost function outside braces.

---

## 3. Grammar Fixes Implemented

### Fix B — Remove Brace Depth Predicate from Function Rule

**Change:** Removed `{inBraceDepth == 0}?` from the COMMAWS alternative in the `function` rule. Created `braceExplicitEvaluationString` rule (genericText in first position) to avoid recursive prediction through `function`.

**PennMUSH proof:** `process_expression()` handles `PE_FUNCTION_CHECK` independently of brace depth. Function-argument braces remove `PE_FUNCTION_CHECK` but preserve `PE_EVALUATE`, and `[...]` inside braces re-enables function checking.

### inFunction Save/Restore in bracePattern

**Change:** Added `savedFunction` Stack to `@parser::members`. In `bracePattern`, save current `inFunction` value, reset to 0 on entry, restore on exit. This isolates function scope within braces — `)` always closes the innermost function outside braces, matching PennMUSH behavior.

**Grammar:**
```antlr
bracePattern
    : { savedFunction.Push(inFunction); inFunction = 0; }
      OBRACE (braceExplicitEvaluationString)? CBRACE
      { inFunction = savedFunction.Pop(); }
    ;
```

### Fix A — Bracket Depth Tracking

**Change:** Added `inBracketDepth` counter incremented in `bracketPattern`. Originally planned to add `{inBracketDepth == 0}? CBRACK` to `beginGenericText`, but this caused AdaptivePredict hangs (see §4). Resolved via token stream rewriting instead (see §6).

---

## 4. AdaptivePredict Hang Problem

### The Problem

Adding `{inBracketDepth == 0}? CBRACK` as a predicated alternative anywhere in the recursive evaluation chain (`beginGenericText`, `genericText`, or `explicitEvaluationString` continuation) causes ANTLR4's `AdaptivePredict` to hang indefinitely on complex CommandList inputs.

### Root Cause

CBRACK has a **dual role**: it's both a bracket pattern closer (in `bracketPattern`) and potential generic text. When added as a predicated alternative, SLL treats the predicate as TRUE during prediction, making both paths valid. Since `evaluationString` is recursive (`evaluationString → function → evaluationString`), the prediction explores exponentially many paths.

### Why CPAREN Doesn't Hang

CPAREN's `{inFunction == 0}?` predicate works because:
- Function entry requires the specific `FUNCHAR` token (bounded entry)
- `COMMAWS` separators limit prediction search within functions
- CBRACK/CBRACE entry uses single-character tokens (`[`, `{`) creating unbounded prediction paths

### Solution

Two viable approaches avoid grammar changes entirely:
1. **Token stream rewriting** (implemented) — post-lex, pre-parse conversion of orphaned tokens
2. **Lexer action** — alternative approach not pursued since rewriting works

---

## 5. SLL vs LL Prediction Mode

### Comparison Results (102 BBS executable lines, 2299+ tests)

| Metric | SLL | LL |
|--------|-----|-----|
| Parse time (102 lines) | **8.9ms** | 1531.6ms |
| Speedup | **171.68×** | — |
| Syntax errors | 0 | 0 |
| Full context scans | 0 | 477 (97 lines) |
| Ambiguities | 472 | 5 |
| Parse results | Identical | Identical |

### Why SLL is Correct

The semantic predicates (`{inFunction == 0}?`, etc.) guard **last-position** alternatives in `beginGenericText`. SLL can't evaluate predicates during prediction but naturally prefers earlier non-predicated alternatives — giving the same result as LL's full context evaluation.

The 472 SLL "ambiguities" are predicate decision points that resolve correctly at parse time. LL's 477 full context scans just confirm the same decisions SLL makes by alternative ordering.

### Default Mode

SLL is the default prediction mode (`ParserPredictionMode.SLL`). Configuration available via `DebugOptions.ParserPredictionMode`.

---

## 6. Token Stream Rewriting

### Overview

Token stream rewriting resolves orphaned delimiter tokens (CBRACK, CBRACE) that result from escaped opener patterns (`\[`, `\{`). The rewriting happens post-lex, pre-parse — after `BufferedTokenSpanStream.Fill()` and before parser creation. Zero grammar changes = zero AdaptivePredict hang risk.

### Algorithm

Single-pass scan through the token stream:

1. Track real delimiter depth (OBRACK/CBRACK for brackets, OBRACE/CBRACE for braces)
2. Detect escaped opener patterns: `ESCAPE` followed by `ANY` where `ANY.Text == "["` or `"{"`
3. At depth 0, record pending escaped openers
4. When encountering closer tokens (CBRACK/CBRACE) at depth 0 with pending escaped openers, convert to `OTHER` token type

### Implementation

Two methods in `MUSHCodeParser.cs`:

- **`RewriteOrphanedBracketClosers()`** — handles `\[...\]` patterns (CBRACK → OTHER)
- **`RewriteOrphanedBraceClosers()`** — handles `\{...\}` patterns (CBRACE → OTHER)

Both called at all 4 token stream creation points after `Fill()`.

### Edge Case: Nested Real Delimiters

The depth-tracking approach correctly handles escaped delimiters inside real delimiter patterns:

```
[reglattr(\[0-9\]+)]
```

Here, the outer `[...]` is a real bracket pattern (depth 1). The `\[` and `\]` inside are at depth > 0, so their closers are NOT converted — they remain `CBRACK` tokens consumed by the bracket pattern's content rules.

### Token Rewrite Candidates Analysis

| Token | Needs Rewriting | Reason |
|-------|:-:|--------|
| CBRACK `]` | ✅ | No generic text fallback; predicate causes AdaptivePredict hang |
| CBRACE `}` | ✅ | Same pattern as CBRACK; JSON `\{...\}` usage common |
| CPAREN `)` | ❌ | Has `{inFunction==0}?` fallback that works (bounded entry via FUNCHAR) |
| SEMICOLON `;` | ❌ | Has working predicate fallback |
| COMMAWS `,` | ❌ | Has working predicate fallback |
| EQUALS `=` | ❌ | Has working predicate fallback |
| CCARET `^` | ❌ | Has working predicate fallback |

---

## 7. Syntax Highlighting & Error Reporting

### Parse Error API

`ValidateAndGetErrors()` returns detailed error information:

```csharp
public record ParseError(
    int Line,
    int Column,
    string Message,
    string? OffendingToken,
    IEnumerable<string> ExpectedTokens,
    string InputText
);
```

### Tokenization API

`Tokenize()` returns token information for syntax highlighting:

```csharp
public record TokenInfo(
    string Type,      // e.g., "FUNCHAR", "OBRACK", "ESCAPE"
    int StartIndex,
    int EndIndex,
    string Text,
    int Line,
    int Column,
    int Channel,
    int Length
);
```

### Supported Token Types

FUNCHAR, OBRACK, CBRACK, OBRACE, CBRACE, OPAREN, CPAREN, COMMAWS, SEMICOLON, EQUALS, ESCAPE, PERCENT, BACKSLASH, CCARET, SPACE, OTHER/ANY, DBREFFLAG, EOF

### Impact of SLL and Token Rewriting on Highlighting

**SLL mode:** No impact on syntax highlighting. Tokenization (`Tokenize()`) operates at the lexer level, which is independent of the parser prediction mode (SLL vs LL). SLL only affects parse-time decisions, not token classification.

**Token stream rewriting:** Orphaned CBRACK/CBRACE tokens converted to OTHER will be highlighted as plain text rather than structural delimiters. This is **correct behavior** — `\]` and `\}` after escaped openers ARE plain text in MUSH semantics. The rewriting makes the token stream match the semantic intent of the code.

**Expected syntax indication:** `ValidateAndGetErrors()` reports expected tokens based on the parser's grammar rules. With token rewriting, the parser sees a consistent token stream (no orphaned delimiters), so expected-syntax suggestions are accurate. Without rewriting, the parser would report misleading errors about unexpected `]` or `}` tokens.

### Configuration

```csharp
// In DebugOptions or configuration
ParserPredictionMode = ParserPredictionMode.SLL; // Default, 171× faster
// ParserPredictionMode = ParserPredictionMode.LL; // Identical results, much slower
```

---

## 8. Test Results

### Final State

- **All 2303 tests pass** (0 failures)
- **0 BBS ANTLR parser errors** (down from 8)
- **0 BBS runtime errors** for parser-related issues
- SLL and LL produce identical results

### Test Coverage

| Area | Tests | Status |
|------|-------|--------|
| Escaped bracket rewriting (`\[...\]`) | 2 unit tests | ✅ Pass |
| Escaped brace rewriting (`\{...\}`) | 2 unit tests | ✅ Pass |
| Brace function semantics (Fix B) | 3 unit tests | ✅ Pass |
| BBS integration (102 lines) | Parser error analysis | ✅ 0 errors |
| SLL vs LL equivalence | Performance diagnostics | ✅ Identical |
| Full test suite | 2303 tests | ✅ All pass |

### Remaining Non-Parser Issues (BBS)

These are **not** ANTLR parser errors — they're runtime/semantic issues:
- Lock evaluator errors on lines 134/136/138 (bare `me` not valid as lock)
- Runtime `#-1` errors from object visibility/timing in `@wait` callbacks
