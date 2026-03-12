# ANTLR4 AdaptivePredict Infinite Loop Analysis

## Problem

After implementing Fix B (removing `{inBraceDepth == 0}?` from the function rule and changing `bracePattern`), an infinite loop occurred in ANTLR4's `AdaptivePredict` mechanism when parsing complex `CommandList` inputs (like the BBS install script).

## Root Cause: `evaluationString` in `bracePattern`

### The Change That Caused the Hang

Fix B initially changed `bracePattern` from:
```antlr
// ORIGINAL
bracePattern:
    OBRACE { ++inBraceDepth; } explicitEvaluationString? CBRACE { --inBraceDepth; }
;
```

to:
```antlr
// FIX B (BROKEN - causes hang)
bracePattern:
    OBRACE { ... } evaluationString? CBRACE { ... }
;
```

### Why `evaluationString` Causes an Infinite Loop

`evaluationString` is defined as:
```antlr
evaluationString:
      function explicitEvaluationString?
    | explicitEvaluationString
;
```

The `function` rule contains `evaluationString?` in its arguments:
```antlr
function: 
    FUNCHAR {++inFunction; ++inFunctionInsideBrace;} 
    (evaluationString? (COMMAWS evaluationString?)*)?
    CPAREN {--inFunction; --inFunctionInsideBrace;} 
;
```

This creates a **recursive prediction cycle**:
```
bracePattern 
  → evaluationString? 
    → function 
      → evaluationString? 
        → function 
          → evaluationString? 
            → ...
```

During ANTLR4's ATN simulation (used by `AdaptivePredict`), the parser must explore all possible paths to decide which alternative to take. The recursive `evaluationString → function → evaluationString` cycle causes the ATN simulator to explore exponentially many paths without converging, hanging forever.

### Why `explicitEvaluationString` Didn't Have This Problem

`explicitEvaluationString` starts with:
```antlr
explicitEvaluationString:
    (bracePattern|bracketPattern|beginGenericText|PERCENT validSubstitution) 
    ...
;
```

It does NOT include `function` as a first alternative. `function` starts with `FUNCHAR` which is NOT in `beginGenericText`'s alternatives — it's only in `genericText` (which includes `beginGenericText | FUNCHAR`). So `explicitEvaluationString` cannot recurse through `function`.

### Why This Only Happened Inside Braces

In the original grammar, `bracePattern` used `explicitEvaluationString?`, so FUNCHAR tokens inside braces were consumed as `genericText` (via the `FUNCHAR` alternative), NOT as function calls. This was correct because:

1. **PennMUSH behavior**: Inside function-argument braces, functions are NOT recognized (`PE_FUNCTION_CHECK` is removed)
2. **No recursive prediction**: `explicitEvaluationString` → no `function` → no recursion

## The Fix: `braceExplicitEvaluationString`

### Solution

Created a new rule `braceExplicitEvaluationString` that mirrors `explicitEvaluationString` but includes `genericText` (with FUNCHAR) in the first position instead of `beginGenericText`:

```antlr
// Like explicitEvaluationString but accepts FUNCHAR as a first element.
// Used inside bracePattern where function names should be treated as generic text
// (not recognized as function calls) per PennMUSH semantics.
// Cannot use evaluationString here as it introduces recursive prediction
// paths through the function rule that cause AdaptivePredict to hang.
braceExplicitEvaluationString:
    (bracePattern|bracketPattern|genericText|PERCENT validSubstitution) 
    (
        bracePattern
      | bracketPattern
      | PERCENT validSubstitution
      | genericText
    )*
;
```

Key differences from `explicitEvaluationString`:
- First alternative uses `genericText` (includes `FUNCHAR`) instead of `beginGenericText` (excludes `FUNCHAR`)
- Does NOT include `function` directly or through `evaluationString`
- No recursive prediction paths possible

### Why This Works

1. **FUNCHAR inside braces** → consumed as `genericText`, not a function call. Matches PennMUSH behavior.
2. **No prediction recursion** → `braceExplicitEvaluationString` cannot reach `function` rule, so ATN simulation terminates.
3. **Bracket patterns inside braces** → still use `evaluationString` (via `bracketPattern`), allowing functions inside `[...]` within braces. This matches PennMUSH where `{[add(1,2)]}` evaluates `add()` but `{add(1,2)}` returns literal text.

## Related Issue: CBRACK Predicate (Fix A, Reverted)

Fix A originally added `{ inBracketDepth == 0 }? CBRACK` to `beginGenericText` to treat orphaned `]` as generic text. This ALSO caused `AdaptivePredict` to hang because:

1. Adding CBRACK to `beginGenericText` expanded the FIRST set of `explicitEvaluationString`
2. This created ambiguity between CBRACK as generic text vs. CBRACK closing a `bracketPattern`
3. The ATN simulator couldn't efficiently resolve this ambiguity on complex inputs

Fix A was reverted. Lines 74 and 96 (orphaned CBRACK after escaped brackets) still produce parser errors but these are handled by error recovery without hanging.

## Verification

After the fix (commit aa94200):
- All 2299 tests pass
- 0 ANTLR4 warnings during build
- BBS error analysis test completes in ~25 seconds (no hang)
- No infinite loops observed in any test
