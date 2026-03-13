# Investigation: Fixing the 2 Remaining CBRACK Syntax Errors

## Problem Statement

After all grammar fixes (Fix A reverted, Fix B implemented, inFunction save/restore), two syntax errors remain on BBS lines 74 and 96. Both are caused by **orphaned `CBRACK` tokens** after escaped bracket sequences.

### Error Pattern

Both lines contain the pattern `\[or(hasflag(\%0,%2),hasflag(\%0,wizard))]`:

```
&CMD_+BBLOCK mbboard=$+bblock *=*/*:...{&CANREAD %q0=\[or(hasflag(\%0,%2),hasflag(\%0,wizard))]}...
                                                   ^^                                          ^
                                                   |                                           |
                                              ESCAPE ANY                                   CBRACK (ERROR)
                                         (not OBRACK)                               (no matching OBRACK)
```

### Token-Level Trace

```
Position   Characters    Token         Grammar Effect
────────   ──────────    ─────         ──────────────
0-1        \[            ESCAPE, ANY   escapedText: literal '[' — NOT OBRACK
2-4        or(           FUNCHAR       function rule entered, inFunction=1
5-12       hasflag(      FUNCHAR       nested function, inFunction=2
13-14      \%            ESCAPE, ANY   escapedText: literal '%'
15         0             OTHER         generic text
16         ,             COMMAWS       function arg separator
17         %             PERCENT       substitution mode
18         2             ARG_NUM       %2 substitution
19         )             CPAREN        closes hasflag(), inFunction=1
20         ,             COMMAWS       arg separator in or()
21-28      hasflag(      FUNCHAR       nested function, inFunction=2
29-30      \%            ESCAPE, ANY   escapedText
31         0             OTHER         generic text
32         ,             COMMAWS       function arg separator
33-37      wizard        OTHER         generic text
38         )             CPAREN        closes hasflag(), inFunction=1
39         )             CPAREN        closes or(), inFunction=0
40         ]             CBRACK        ← ERROR: inBracketDepth=0, no matching OBRACK
```

**Root cause**: The lexer correctly escapes `\[` → `ESCAPE ANY` (not `OBRACK`), so `inBracketDepth` never increments. When `]` appears, it becomes `CBRACK` with no open `bracketPattern` to close.

### Why Fix A Was Reverted

Fix A added `{ inBracketDepth == 0 }? CBRACK` to `beginGenericText`:

```antlr
beginGenericText:
    { inFunction == 0 }? CPAREN
  | { inBracketDepth == 0 }? CBRACK        // ← Fix A (REVERTED)
  | ...
```

This caused `AdaptivePredict` to hang because:

1. **FIRST set expansion**: Adding CBRACK to `beginGenericText` expanded the FIRST set of `explicitEvaluationString`, creating ambiguity between CBRACK-as-generic-text vs CBRACK-as-bracket-closer
2. **Combined with recursive prediction**: The `evaluationString → function → evaluationString` cycle, combined with the additional predicated alternative, caused the ATN simulator to explore exponentially many paths on complex CommandList inputs

---

## Proposed Approaches

### Approach 1: Token Stream Rewriting (Post-Lex, Pre-Parse)

**Concept**: After lexing but before parsing, scan the token list to identify orphaned `CBRACK` tokens (those not matching any `OBRACK`) and change their token type to `OTHER`.

**Algorithm**:
```
1. Fill all tokens via BufferedTokenSpanStream.Fill()
2. Scan for ESCAPE + ANY('[') patterns in the token list
3. For each escaped bracket opener:
   a. Track bracket nesting depth starting at 0
   b. Walk forward through subsequent tokens:
      - OBRACK: ++depth  (entering real bracket)
      - CBRACK: if depth > 0, --depth (closing real bracket)
                if depth == 0, this is the orphaned CBRACK → change type to OTHER
   c. Stop when orphaned CBRACK found or end of tokens
4. Parse normally with the modified token stream
```

**Example trace**:
```
Tokens: ... ESCAPE  ANY('[')  FUNCHAR('or(')  ...  CPAREN(')')  CBRACK(']') ...
              ↑       ↑                                           ↑
         Step 2: detect escaped bracket         Step 3c: depth=0 → type=OTHER
```

**Nested bracket handling**:
```
Input:  \[foo[bar()]baz]
Tokens: ESCAPE ANY('[') OTHER('foo') OBRACK('[') FUNCHAR('bar(') CPAREN(')') CBRACK(']') OTHER('baz') CBRACK(']')
                                       ↑ depth=1                              ↑ depth=0                ↑ depth=0→OTHER
```

**Implementation location**: Add a static method to `MUSHCodeParser` called between `bufferedTokenSpanStream.Fill()` and parser creation:

```csharp
/// <summary>
/// Scans the token stream for escaped bracket openers (\[) and converts
/// their matching orphaned CBRACK closers to OTHER tokens, preventing
/// parser errors on unmatched brackets.
/// </summary>
private static void RewriteOrphanedBracketClosers(BufferedTokenSpanStream tokenStream)
{
    var tokens = tokenStream.tokens;  // Direct access to token list
    for (int i = 0; i < tokens.Count - 1; i++)
    {
        if (tokens[i].Type == SharpMUSHLexer.ESCAPE
            && tokens[i + 1].Type == SharpMUSHLexer.ANY
            && tokens[i + 1].Text == "[")
        {
            // Found \[ — walk forward to find the orphaned CBRACK
            int depth = 0;
            for (int j = i + 2; j < tokens.Count; j++)
            {
                if (tokens[j].Type == SharpMUSHLexer.OBRACK)
                    depth++;
                else if (tokens[j].Type == SharpMUSHLexer.CBRACK)
                {
                    if (depth > 0) depth--;
                    else
                    {
                        // Orphaned CBRACK — change to OTHER
                        // Need to create a new CommonToken with type OTHER
                        var oldToken = tokens[j];
                        var newToken = new CommonToken(oldToken)
                        {
                            Type = SharpMUSHLexer.OTHER
                        };
                        tokens[j] = newToken;
                        break;
                    }
                }
            }
        }
    }
}
```

**Pros**:
- ✅ No grammar changes → zero risk of AdaptivePredict hang
- ✅ No prediction ambiguity — CBRACK is simply not in the token stream
- ✅ Clean separation of concerns: escape handling at token level, grammar stays clean
- ✅ Handles nested real brackets correctly
- ✅ `BufferedTokenSpanStream` already stores all tokens in a list (`tokens` field)
- ✅ All existing tests should continue to pass (no grammar change)
- ✅ Easy to test independently

**Cons**:
- ⚠️ Adds a pre-processing step outside the grammar — must be called in all parse entry points
- ⚠️ Modifies tokens in-place which could affect error messages (orphaned `]` won't be reported as CBRACK error)
- ⚠️ The matching heuristic assumes the first unmatched CBRACK after `\[` is the intended closer — could mismatch in edge cases
- ⚠️ Need to verify `BufferedTokenSpanStream.tokens` is mutable after `Fill()`

**Risk assessment**: LOW. This is the safest approach because it makes no grammar changes.

---

### Approach 2: CBRACK in `genericText` Continuation (Not First Position)

**Concept**: Add `{ inBracketDepth == 0 }? CBRACK` to the `genericText` rule instead of `beginGenericText`. Since `genericText` is used in continuation positions of `explicitEvaluationString`, CBRACK won't be in the FIRST set of `evaluationString`.

**Grammar change**:
```antlr
// Current:
genericText: beginGenericText | FUNCHAR;

// Proposed:
genericText: beginGenericText | FUNCHAR | { inBracketDepth == 0 }? CBRACK;
```

**Prediction analysis**:

`explicitEvaluationString` uses `beginGenericText` in first position and `genericText` in continuation:
```antlr
explicitEvaluationString:
    (bracePattern|bracketPattern|beginGenericText|PERCENT validSubstitution)  // ← FIRST set
    (
        bracePattern
      | bracketPattern
      | PERCENT validSubstitution
      | genericText    // ← continuation: CBRACK appears HERE, not in FIRST set
    )*
;
```

CBRACK would only be considered during continuation prediction (the `(...)*` part), NOT during the initial decision of which rule to enter. This significantly reduces prediction ambiguity.

**However**, `braceExplicitEvaluationString` uses `genericText` in FIRST position:
```antlr
braceExplicitEvaluationString:
    (bracePattern|bracketPattern|genericText|PERCENT validSubstitution)  // ← genericText is FIRST!
    ...
```

So CBRACK would be in the FIRST set of `braceExplicitEvaluationString`, which is used inside `bracePattern`. This could potentially cause prediction issues when the parser decides between bracePattern alternatives.

**Pros**:
- ✅ Minimal grammar change (one rule modification)
- ✅ CBRACK not in FIRST set of `evaluationString`/`explicitEvaluationString`
- ✅ Follows the same semantic predicate pattern used for CPAREN
- ✅ Grammar is self-contained (no external processing)

**Cons**:
- ⚠️ CBRACK IS in FIRST set of `braceExplicitEvaluationString` — could cause prediction issues
- ⚠️ Need to verify experimentally that this doesn't cause AdaptivePredict to hang
- ⚠️ In continuation position, CBRACK could match prematurely if there's a real bracketPattern closing
- ⚠️ Predicate evaluation ordering: the `{ inBracketDepth == 0 }?` check happens at prediction time, which may not reflect the actual bracket state during parsing

**Risk assessment**: MEDIUM. The `braceExplicitEvaluationString` first-position issue needs experimental validation.

---

### Approach 3: Lexer Action with Escape State Tracking

**Concept**: Track escaped bracket openers in the lexer using member variables. When `\[` produces `ESCAPE ANY`, record that a bracket was escaped. When the matching `]` is encountered, emit it as `OTHER` instead of `CBRACK`.

**Lexer changes**:
```antlr
@lexer::members {
    public int escapedBracketCount = 0;
}

mode ESCAPING;
ESCAPED_BRACKET: '[' { escapedBracketCount++; } -> type(ANY), popMode;
ANY: . -> popMode;
```

In DEFAULT mode, the CBRACK rule needs to check the counter:
```antlr
// ANTLR4 lexer rules don't support semantic predicates in the same way as parser rules
// Instead, use a lexer action to conditionally change the token type:
CBRACK: WS ']' { if (escapedBracketCount > 0) { escapedBracketCount--; Type = OTHER; } };
```

**Key issue**: The matching between `\[` and `]` is not always 1:1. Consider:
```
\[foo] [bar]  → \[ should match first ], not second ]
\[foo[bar]baz] → \[ should match last ], first ] closes real [bar]
```

The simple counter approach works for the first case but NOT the second. We'd need nesting tracking in the lexer, which is complex.

**Alternative lexer approach with nesting**:
```antlr
@lexer::members {
    public int escapedBracketCount = 0;
    public int realBracketDepth = 0;
}

// Track real bracket opens
OBRACK: '[' WS { realBracketDepth++; };

// When ] and we're closing a real bracket, decrement
// When ] and no real bracket open but escaped bracket pending, emit as OTHER
CBRACK: WS ']' {
    if (realBracketDepth > 0) {
        realBracketDepth--;
    } else if (escapedBracketCount > 0) {
        escapedBracketCount--;
        Type = OTHER;
    }
};
```

**Pros**:
- ✅ No grammar changes needed
- ✅ Token types are correct before parsing
- ✅ Handles the common case cleanly

**Cons**:
- ⚠️ Lexer-level nesting tracking is fragile — lexer processes character-by-character
- ⚠️ ANTLR4 lexer actions execute AFTER token recognition, but modifying `Type` in an action should work per ANTLR4 docs
- ⚠️ The `WS` fragment in `OBRACK` and `CBRACK` means whitespace-surrounded brackets — the nesting counter must account for this
- ⚠️ Counter interaction with other escaped delimiters (`\]`, `\(`, `\)`) is not addressed
- ⚠️ Need to verify ANTLR4 lexer action semantics for `Type = OTHER` inside rule body
- ⚠️ Does not handle `\]` (escaped closing bracket) — but this pattern doesn't appear in BBS data

**Risk assessment**: MEDIUM-HIGH. Lexer-level state tracking is error-prone with complex input patterns.

---

### Approach 4: Dedicated `escapedBracketContent` Parser Rule

**Concept**: Create an explicit parser rule that matches the pattern `ESCAPE ANY (where ANY='[') ... CBRACK`, treating it as a special form of escaped text that spans until the matching `]`.

**Grammar addition**:
```antlr
// Match escaped bracket content: \[ content ]
escapedBracketContent:
    ESCAPE ANY evaluationString? CBRACK
;

explicitEvaluationString:
    (bracePattern|bracketPattern|escapedBracketContent|beginGenericText|PERCENT validSubstitution)
    (
        bracePattern
      | bracketPattern
      | escapedBracketContent
      | PERCENT validSubstitution
      | genericText
    )*
;
```

**Prediction analysis**:

The FIRST set of `escapedBracketContent` is `ESCAPE`, which is already in `beginGenericText` via `escapedText: ESCAPE ANY`. This creates ambiguity: when the parser sees `ESCAPE`, should it enter `escapedBracketContent` or `beginGenericText.escapedText`?

This requires ANTLR4 to look ahead at the second token (`ANY`) and check if it's `[` to decide. Since `ANY` in ESCAPING mode matches any character, the parser can't distinguish at prediction time.

**Semantic predicate version**:
```antlr
// Use a predicate to check if the escaped character is '['
escapedBracketContent:
    ESCAPE { _input.LT(1).Text == "[" }? ANY evaluationString? CBRACK
;
```

But this has the same problems as other predicated approaches — it adds a predicated alternative to the FIRST set of `explicitEvaluationString`.

**Deeper problem**: The rule uses `evaluationString?` in the body, which is the same recursive pattern that caused the AdaptivePredict hang when used in `bracePattern`. This approach would likely cause the same infinite loop.

**Pros**:
- ✅ Explicit grammar structure for the pattern
- ✅ Parse tree would show escaped bracket content as a distinct node

**Cons**:
- ❌ `evaluationString?` inside the rule → likely causes AdaptivePredict hang (same as bracePattern issue)
- ❌ FIRST set ambiguity with `escapedText` (both start with ESCAPE)
- ❌ Predicate needed for disambiguation → adds prediction complexity
- ❌ Would need a `braceExplicit` variant to avoid recursion (duplicating the bracePattern workaround)

**Risk assessment**: HIGH. This approach reproduces the same prediction issues that caused the original hang.

---

### Approach 5: Custom `ANTLRErrorStrategy` Subclass

**Concept**: Override ANTLR4's error recovery to silently consume orphaned `CBRACK` tokens as generic text.

```csharp
public class MUSHErrorStrategy : DefaultErrorStrategy
{
    protected override IToken RecoverInline(Parser recognizer)
    {
        if (recognizer.CurrentToken?.Type == SharpMUSHLexer.CBRACK)
        {
            // Consume orphaned CBRACK as if it were generic text
            recognizer.Consume();
            return recognizer.CurrentToken;
        }
        return base.RecoverInline(recognizer);
    }
}
```

**Pros**:
- ✅ No grammar changes
- ✅ Simple to implement
- ✅ Can be precisely targeted to CBRACK errors

**Cons**:
- ❌ Error recovery produces incorrect parse trees — the orphaned CBRACK is consumed but not placed in a grammar node
- ❌ The consumed token's text (`]`) is lost from the parse tree output, potentially affecting evaluation
- ❌ Masks real errors — if a genuine missing CBRACK occurs, the custom strategy might hide it
- ❌ Does not match PennMUSH behavior where `]` IS part of the literal text output
- ❌ ANTLR4's `DefaultErrorStrategy` has complex state management; overriding it incorrectly can cause cascading failures

**Risk assessment**: MEDIUM-HIGH. Error strategy overrides are hard to get right without side effects.

---

### Approach 6: Input Pre-processing (Text-Level)

**Concept**: Before lexing, scan the input text for `\[...\]` patterns and replace the brackets with placeholder characters that the lexer treats as OTHER text.

```csharp
// Replace \[ with \← and matching ] with → (using characters not in grammar)
// Then the lexer never sees [ or ] for escaped brackets
string preprocessed = ReplaceEscapedBrackets(input);
```

**Pros**:
- ✅ No grammar changes
- ✅ No prediction issues
- ✅ Simple conceptually

**Cons**:
- ❌ Character positions shift, breaking error messages and source mapping
- ❌ Need to un-replace in the output, adding complexity
- ❌ The matching logic is complex — need to track nesting of real brackets vs escaped ones
- ❌ Choosing placeholder characters is fragile — they must not appear in valid MUSH code
- ❌ Actually, `\]` is NOT in the BBS data — the closing `]` is not escaped (it's the natural closer to the escaped `\[`). So this approach would need to find the matching unescaped `]`, which is the same nesting problem as other approaches.

**Risk assessment**: MEDIUM. Works but adds complexity outside the grammar/parser pipeline.

---

## Comparison Matrix

| Approach | Grammar Change | Hang Risk | Complexity | Correctness | Recommended |
|----------|:---:|:---:|:---:|:---:|:---:|
| 1: Token Stream Rewriting | None | None | Low | High | ✅ **Yes** |
| 2: genericText CBRACK | Minimal | Medium | Low | High | Maybe |
| 3: Lexer Action | None (lexer only) | None | Medium | Medium | Maybe |
| 4: escapedBracketContent | Significant | High | High | Low | ❌ No |
| 5: Custom Error Strategy | None | None | Medium | Low | ❌ No |
| 6: Input Pre-processing | None | None | Medium | Medium | ❌ No |

## Recommendation

**Approach 1 (Token Stream Rewriting)** is the recommended solution because:

1. **Zero grammar changes** — eliminates all risk of AdaptivePredict hangs
2. **Precise targeting** — only affects escaped bracket patterns, not general parsing
3. **Handles nesting** — correctly distinguishes real vs orphaned CBRACKs
4. **Clean integration** — `BufferedTokenSpanStream.Fill()` already materializes all tokens; adding a post-fill scan is natural
5. **Testable** — can be unit-tested independently of the parser

**Approach 2** is a viable fallback if Approach 1 has unforeseen issues with `BufferedTokenSpanStream` mutability. It needs experimental validation to confirm it doesn't cause prediction hangs via `braceExplicitEvaluationString`.

**Approach 3** is a reasonable alternative if the team prefers keeping all token handling in the lexer layer, but the nesting tracking complexity makes it less attractive.

---

## PennMUSH Behavior Reference

In PennMUSH's `process_expression()`, the `\` escape is handled by:
```c
case '\\':
    /* Escaped character: skip it */
    safe_chr(*(*str), buff, bp);
    (*str)++;
    if (**str)
    {
        safe_chr(**str, buff, bp);
        (*str)++;
    }
    break;
```

The escape simply copies the next character literally. Both `\[` and `]` become literal text characters — `\[` is literal `[` and `]` is literal `]`. PennMUSH never enters bracket evaluation for `\[...]` because the opening `[` is consumed as literal text.

This confirms that **the orphaned `]` should be treated as literal text** — exactly what all proposed approaches achieve.

---

## Test Cases for Validation

Any fix should pass these tests:

1. **Orphaned CBRACK**: `\[or(hasflag(\%0,%2),hasflag(\%0,wizard))]` → No parser errors, `]` is literal text
2. **Real brackets**: `[add(1,2)]` → Normal bracket evaluation, no errors
3. **Mixed**: `\[literal] [add(1,2)]` → First `]` is literal, second `]` closes bracket pattern
4. **Nested**: `\[foo[bar()]baz]` → `[bar()]` is real bracket pattern, outer `]` is literal text
5. **Multiple escaped**: `\[a] \[b]` → Both `]` are literal text
6. **Escaped closer**: `\]` → Already handled correctly by lexer (ESCAPE ANY)
7. **All 2299 existing tests** — must continue to pass
8. **BBS integration** — lines 74 and 96 should no longer produce syntax errors

---

## Appendix: Token Stream Access

The `BufferedTokenSpanStream` class stores tokens in `protected internal List<IToken> tokens`:

```csharp
// From BufferedTokenSpanStream.cs
protected internal List<IToken> tokens = new(256);
```

After `Fill()`, this list contains all tokens. The list should be mutable (it's a `List<IToken>`, not a read-only collection). Token replacement can be done by creating a new `CommonToken` with the same position/channel/text but different `Type`:

```csharp
var oldToken = tokens[j];
var newToken = new CommonToken(oldToken) { Type = SharpMUSHLexer.OTHER };
tokens[j] = newToken;
```

The `BufferedTokenSpanStream` also has `TokenArray` which is set during `Fill()`. After modification, the `TokenArray` may need to be refreshed. This needs verification during implementation.
