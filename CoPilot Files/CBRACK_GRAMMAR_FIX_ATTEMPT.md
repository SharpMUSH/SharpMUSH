# CBRACK Grammar Fix Attempt — Investigation Results

## Request

> Try putting in the OBRACK/CBRACK rule back in, so that CBRACK can be in genericText as long as there is no open OBRACK.

## Problem

2 syntax errors remain on BBS lines 74 and 96 — orphaned `CBRACK` tokens from `\[...\]` escape patterns. The lexer correctly escapes `\[` → `ESCAPE ANY` (not `OBRACK`), but `]` still becomes `CBRACK` with no matching `OBRACK`. The `inBracketDepth` counter stays 0 because no `bracketPattern` was entered.

## Approaches Tested

### Attempt 1: `{ inBracketDepth == 0 }? CBRACK` in `beginGenericText`

```antlr
beginGenericText:
      { inFunction == 0 }? CPAREN
    | { inBracketDepth == 0 }? CBRACK        // ← Added here
    | { !inCommandList || inBraceDepth > 0 }? SEMICOLON
    | ...
```

**Result**: ❌ **AdaptivePredict hang** — BBS parser test exceeds 3-minute timeout.

---

### Attempt 2: `{ inBracketDepth == 0 }? CBRACK` in `genericText`

```antlr
// Changed from:
genericText: beginGenericText | FUNCHAR;
// To:
genericText: beginGenericText | FUNCHAR | { inBracketDepth == 0 }? CBRACK;
```

**Result**: ❌ **AdaptivePredict hang** — same timeout behavior.

---

### Attempt 3: `{ inBracketDepth == 0 }? CBRACK` in `explicitEvaluationString` continuation

```antlr
explicitEvaluationString:
    (bracePattern|bracketPattern|beginGenericText|PERCENT validSubstitution) 
    (
        bracePattern
      | bracketPattern
      | PERCENT validSubstitution
      | genericText
      | { inBracketDepth == 0 }? CBRACK     // ← Added here
    )*
;
```

Also added to `braceExplicitEvaluationString` in the same position.

**Result**: ❌ **AdaptivePredict hang** — same timeout behavior.

## Root Cause Analysis

The hang occurs regardless of WHERE the `{ inBracketDepth == 0 }? CBRACK` predicate is placed in the recursive evaluation chain. The fundamental mechanism is:

### 1. CBRACK Creates Dual-Role Ambiguity

`CBRACK` is the **closing delimiter** of `bracketPattern`:
```antlr
bracketPattern: OBRACK { ++inBracketDepth; } evaluationString CBRACK { --inBracketDepth; }
```

Adding CBRACK as generic text creates an ambiguity: "Is this CBRACK closing a bracket, or is it text?"

### 2. SLL Prediction Can't Resolve Predicates

In SLL mode (our default, 171× faster than LL), predicates are treated as `true` during prediction. So `{ inBracketDepth == 0 }?` doesn't help the predictor distinguish between the two roles of CBRACK.

### 3. Recursive Path Explosion

When ANTLR4's ATN simulator encounters the ambiguity, it must explore both paths:
- **Path A**: CBRACK ends the current `bracketPattern`
- **Path B**: CBRACK is consumed as generic text, and the parser continues looking for more content inside `evaluationString`

Path B leads back into `evaluationString → explicitEvaluationString`, which can contain more `bracketPattern` instances, which contain more `evaluationString` instances... creating exponentially many prediction paths.

### 4. Why CPAREN Doesn't Hang

The existing `{ inFunction == 0 }? CPAREN` predicate in `beginGenericText` does NOT cause a hang even though it has the same dual-role structure (CPAREN closes `function` rule AND is generic text). The difference:

- **Function entry** requires `FUNCHAR` token (e.g., `add(`) — a very specific token that the predictor can distinguish from generic text
- **Bracket entry** requires `OBRACK` (`[` with optional whitespace) — also specific, BUT `bracketPattern` contains `evaluationString` which can recursively contain `function` calls

The key difference is that the `function` rule's argument list uses `COMMAWS` as separator, which acts as a natural "stop" for prediction exploration. The `bracketPattern` rule contains a single `evaluationString` that runs until CBRACK, with no intermediate delimiters to bound the prediction search.

### 5. LL Mode Also Hangs

Even LL mode hangs with these changes. While LL evaluates predicates during full-context prediction, the initial SLL prediction phase (which LL also runs first) encounters the same path explosion before falling through to full-context analysis.

## Recommended Alternatives

Since grammar-level approaches all cause AdaptivePredict hangs, the two viable alternatives are:

### 1. Token Stream Rewriting (Recommended)

After lexing but before parsing, scan the token list for `ESCAPE ANY('[')` patterns and change their matching orphaned `CBRACK` to `OTHER`. Zero grammar changes = zero prediction risk.

**Implementation location**: `MUSHCodeParser.cs` between `bufferedTokenSpanStream.Fill()` and parser creation.

See `CoPilot Files/REMAINING_CBRACK_SYNTAX_ERRORS_INVESTIGATION.md` Approach 1 for full pseudocode and algorithm.

### 2. Lexer Action (Alternative)

Track escaped bracket openers in lexer member variables. When `\[` is lexed via the ESCAPING mode, increment a counter. When `]` is encountered and the counter > 0, emit as `OTHER` instead of `CBRACK`.

See `CoPilot Files/REMAINING_CBRACK_SYNTAX_ERRORS_INVESTIGATION.md` Approach 3 for details.

### 3. Custom Error Strategy (Alternative)

Override ANTLR4's error recovery to silently consume orphaned `CBRACK` tokens as generic text. This doesn't prevent the syntax error but suppresses it.

## Conclusion

Any placement of `{ inBracketDepth == 0 }? CBRACK` in the grammar's recursive evaluation chain causes AdaptivePredict to hang due to dual-role ambiguity between CBRACK-as-bracket-closer and CBRACK-as-text creating exponential prediction path exploration. This is a structural limitation of the grammar/ANTLR4 interaction that cannot be resolved with predicate placement alone.

The token stream rewriting approach (Approach 1) is recommended as it makes zero grammar changes and handles the problem before the parser ever sees the orphaned CBRACK tokens.
