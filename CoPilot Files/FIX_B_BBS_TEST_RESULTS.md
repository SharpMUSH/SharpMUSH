# Fix B — BBS Test Results

## Test Run Summary

After implementing Fix B (grammar + visitor changes for PennMUSH-compatible brace semantics),
the Myrddin BBS v4.0.6 test suite was re-run to measure progress.

### Parser Error Reduction

| Metric | Before Fix B | After Fix B | Change |
|--------|-------------|-------------|--------|
| Lines with ANTLR parser errors | 8 | 1 | **87.5% reduction** |
| Total ANTLR error instances | 24+ | 6 | **75% reduction** |
| Error-producing lines by Root Cause B | 4 (lines 91, 109, 110, 111) | 0 | **100% resolved** |
| Error-producing lines by Root Cause A | 3 (lines 74, 83, 96) | 0 | **100% resolved** |
| Error-producing lines by Root Cause C | 1 (line 101) | 0 | **Resolved** |
| New error (Fix C interaction) | — | 1 (line 57) | See analysis below |

### Full Test Suite

- **Total tests:** 2566
- **Passed:** 2273
- **Failed:** 0
- **Skipped:** 293
- **Duration:** ~2 minutes

All existing tests pass, including the 3 new Fix B test cases.

---

## BBS Integration Test Results

```
[BBS INSTALL] Executed 102 commands from 150 total lines.
[BBS INSTALL] +bbread command executed successfully.
Total script lines: 150
Executable commands: 102
Successfully executed: 102
Execution exceptions: 0
Install notifications: 336
Install #-1 errors: 3
+bbread notifications: 14
+bbread #-1 errors: 2
Lines with ANTLR parser errors: 1
```

### Install #-1 Errors (Not ANTLR-related)

These are runtime evaluation errors, not parser errors:

1. `@switch [first(grep(me,*,a))]=#-1,...` — `grep()` function not yet implemented
2. `CMD_+BBCONFIG3` — Command execution issue
3. `CMD_+BBNEWGROUP` — `num()` function behavior

### +bbread Output

The BBS installation completes and `+bbread` produces output showing group
structure, though some values show `#-1` errors due to unimplemented functions
(not parser-related).

---

## Lines Previously Erroring — Now Fixed

### Root Cause B Lines (Fixed by Fix B grammar change)

These lines had multi-arg function calls inside braces that were blocked by
the `{inBraceDepth == 0}?` predicate on the function rule's COMMAWS:

| Line | Attribute | Pattern | Status |
|------|-----------|---------|--------|
| 91 | `&CMD_+BBSCAN` | `{...u(%1/canread,##)...}` with nested functions | ✅ Fixed |
| 109 | `&FN_GROUPHEADER` | `{...ljust(name(%1),20)...}` inside braces | ✅ Fixed |
| 110 | `&FN_GROUPHEADER` | Similar brace-enclosed functions | ✅ Fixed |
| 111 | `&FN_GROUPHEADER` | Similar brace-enclosed functions | ✅ Fixed |

### Root Cause A Lines (Fixed by Fix A bracket depth tracking)

These lines had orphaned `]` from escaped `\[` sequences:

| Line | Attribute | Pattern | Status |
|------|-----------|---------|--------|
| 74 | `&CMD_+BBLOCK` | `\[or(hasflag(\%0,...)...]` | ✅ Fixed |
| 83 | `&CMD_+BBREAD2` | Multiple `\(` and `\)` escape patterns | ✅ Fixed |
| 96 | `&CMD_+BBWRITELOCK` | Same `\[or(...)` pattern as line 74 | ✅ Fixed |

### Root Cause C Line (Fixed by Fix C paren depth tracking)

| Line | Attribute | Pattern | Status |
|------|-----------|---------|--------|
| 101 | `&CMD_+BBREAD` | `\(-\)` and `\,` bare paren patterns | ✅ Fixed |

---

## Remaining Error — Line 57 Analysis

### The Error

```
Script Line 57: &TR_POST_NOTIFY bbpocket=@switch hasflag(%0,DARK)=0,{@pemit/list iter(...)=(New BB message ...)}
  Column 304: Unexpected token ']' at this position (Expected: CPAREN, COMMAWS)
  Column 309: extraneous input ')' expecting CBRACE
```

### Root Cause: Fix C `inParenDepth` Scope Leakage

The error is caused by **interaction between Fix C's `inParenDepth` tracking and
function calls inside bracket patterns**. Here's the mechanism:

#### The Problematic Content (inside `{...}`)

```
@pemit/list iter(remove(lwho(),%0),switch(or(member([get(##/bb_omit)] [get(##/bb_silent)],%1),not(u(%1/canread,##))),0,##))=(New BB message ([member(v(groups),%1)]/%2) posted to '[name(%1)]' by [ifelse(hasattr(%1,anonymous),get(%1/anonymous),name(%0))]: %3)}
```

#### Token-Level Trace

1. **Col 52:** `{` → OBRACE, enters bracePattern with `evaluationString`
2. **Col 53-68:** `@pemit/list ` → OTHER tokens (beginGenericText)
3. **Col 69:** `iter(` → FUNCHAR, consumed as **genericText** (not a function call)
   - Because evaluationString started with explicitEvaluationString (first token was `@`, not FUNCHAR)
   - All subsequent FUNCHAR tokens are also genericText
4. **Cols 69-175:** All function calls (`remove(`, `lwho(`, `switch(`, `or(`, etc.) are genericText
   - Their `(` are consumed as part of FUNCHAR tokens
   - Their `)` are CPAREN → generic text (inFunction==0)
5. **Col 177:** `(` in `(New BB message` → **OPAREN** → `inParenDepth = 1`
6. **Col 193:** `(` in `([member(` → **OPAREN** → `inParenDepth = 2`
7. **Col 194-215:** `[member(v(groups),%1)]` → bracketPattern, functions parsed correctly inside
8. **Col 219:** `)` after `/%2` → CPAREN generic text → `inParenDepth = 1`
9. **Col 232-241:** `[name(%1)]` → bracketPattern, function parsed correctly inside

**The Critical Point:**

10. **Col 247:** `[` → OBRACK, enters bracketPattern
    - **`inParenDepth` is still 1** from the `(New` at col 177
11. **Col 248:** `ifelse(` → FUNCHAR → **function rule** (inFunction=1)
12. **Cols 248-301:** Function arguments parsed normally:
    - `hasattr(%1,anonymous)` → nested function, closes properly
    - `get(%1/anonymous)` → nested function, closes properly
    - `name(%0)` → nested function starts
13. **Col 302:** `)` after `name(%0)` — **AMBIGUITY!**
    - Function rule expects CPAREN to close `name()`
    - But beginGenericText predicate `{inFunction == 0 || inParenDepth > 0}?` evaluates to:
      `{2 == 0 || 1 > 0}?` = `{false || true}?` = **TRUE**
    - ANTLR4 can consume this `)` as generic text in the evaluationString
    - If it does, `name()` doesn't close, and its argument extends: `%0)`
14. **Col 303:** `)` → Now this closes `name()` (CPAREN in function rule)
    - But `name()` consumed `)` at 302 as part of its argument
    - So `)` at 303 should close ifelse, but the paren counts are off
15. **Col 304:** `]` → CBRACK — Parser expected CPAREN or COMMAWS (still in ifelse)
    - **ERROR: "Unexpected token ']'"**
16. **Col 309:** `)` → Orphaned CPAREN where CBRACE was expected

#### Why This Happens

Fix C added `inParenDepth > 0` to the CPAREN predicate in beginGenericText to handle
bare parentheses like `(text)` in non-function contexts. This works correctly when bare
parens and function calls don't overlap.

However, when bare parentheses from an **outer context** elevate `inParenDepth`, and then
a **bracket pattern** creates a new scope with function calls, the elevated `inParenDepth`
leaks into the bracket scope. This causes the predicate to incorrectly classify function-
closing CPARENs as generic text.

#### Proposed Fix: Scope `inParenDepth` in Bracket Patterns

Save and restore `inParenDepth` when entering/exiting bracket patterns, similar to how
`inFunctionInsideBrace` is saved/restored in brace patterns:

```antlr
bracketPattern:
    OBRACK { ++inBracketDepth; savedParenDepth.Push(inParenDepth); inParenDepth = 0; }
    evaluationString
    CBRACK { --inBracketDepth; inParenDepth = savedParenDepth.Pop(); }
;
```

This would scope `inParenDepth` so that bare parens from outer contexts don't affect
function parsing inside brackets. This is analogous to PennMUSH's `[...]` handler which
creates a fresh evaluation context with `PE_FUNCTION_CHECK` re-enabled.

---

## Error Category Evolution

| Root Cause | Before Any Fixes | After Fix A+C | After Fix A+B+C |
|-----------|-----------------|---------------|-----------------|
| A: Escaped brackets `\[...\]` | 3 lines | 0 lines | 0 lines |
| B: Brace depth predicate | 4 lines | 4 lines | 0 lines |
| C: Bare paren tracking | 1 line | 0 lines | 0 lines |
| C+B interaction: ParenDepth scope | 0 lines | 0 lines | 1 line |
| **Total** | **8 lines** | **4 lines** | **1 line** |

## Conclusion

Fix B resolved its target errors (Root Cause B — 4 lines) completely. Combined with
Fix A and Fix C, only 1 of the original 8 error-producing lines remains. The remaining
error is a cross-fix interaction between Fix C's `inParenDepth` tracking and Fix B's
function-inside-brace recognition, specifically when bare parentheses from outer text
contexts leak into bracket patterns that contain function calls.
