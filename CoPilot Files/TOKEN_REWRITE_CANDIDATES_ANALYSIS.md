# Token Stream Rewriting Candidates Analysis

## Context

`RewriteOrphanedBracketClosers()` in `MUSHCodeParser.cs` scans post-lex tokens for `ESCAPE+ANY('[')` at bracket depth 0 and converts matching orphaned `CBRACK` tokens to `OTHER`. This eliminates the 2 remaining BBS syntax errors without any grammar changes (which would cause AdaptivePredict to hang).

**Question:** Are there other tokens/rules that could benefit from the same approach?

---

## All Dual-Role Tokens in the Grammar

Seven tokens serve as both structural delimiters and potential generic text:

| Token | Structural Role | Generic Text Fallback | Orphan Pattern | Risk |
|-------|----------------|----------------------|----------------|------|
| **CBRACK** `]` | bracketPattern closer | **NONE** — fixed via token rewriting | `\[text]` | ✅ **Fixed** |
| **CBRACE** `}` | bracePattern closer | **NONE** — fixed via token rewriting | `\{text}` | ✅ **Fixed** |
| **CPAREN** `)` | function closer | `{ inFunction == 0 }? CPAREN` | `\(text)` | ✓ Low |
| **SEMICOLON** `;` | commandList separator | `{ !inCommandList \|\| inBraceDepth > 0 }?` | `\;` | ✓ None |
| **COMMAWS** `,` | argument separator | Complex predicate (line 158) | `\,` | ✓ None |
| **EQUALS** `=` | assignment separator | `{ !lookingForCommandArgEquals }?` | `\=` | ✓ None |
| **CCARET** `>` | register closer | `{ !lookingForRegisterCaret }?` | `\>` | ✓ None |

---

## Detailed Analysis Per Token

### 1. CBRACE `}` — Implemented ✅

**Why it's a candidate:** CBRACE has the **same structural pattern** as CBRACK — it closes `bracePattern` but has NO generic text fallback in `beginGenericText`. An orphaned CBRACE would be a syntax error.

**Orphan mechanism:**
```
Input:    \{text}
Lexer:    ESCAPE ANY('{') ... CBRACE
Parser:   escapedText    ... ???  ← orphaned CBRACE, no rule can consume it
```

**Would a grammar predicate work?** Almost certainly **no**. Adding `{ inBraceDepth == 0 }? CBRACE` to `beginGenericText` would cause the same AdaptivePredict hang as CBRACK, because CBRACE has the same dual-role ambiguity (brace closer vs. text) that cascades through the recursive `evaluationString` prediction paths.

**Evidence of real usage:** Found in PennMUSH JSON pretty-printer example (SharpMUSH.Documentation):
```
&pretty_json_sub me=...switch(%1,\{*,\{%r[...]%r[...]\},...)]
```
This uses `\{` and `\}` to output literal braces in formatted JSON output. Regular users will encounter this pattern when escaping JSON input or output.

**Implementation:** `RewriteOrphanedBraceClosers()` in `MUSHCodeParser.cs` — structurally identical to `RewriteOrphanedBracketClosers()`. Scans for `ESCAPE+ANY('{')` at brace depth 0, tracks real `OBRACE`/`CBRACE` depth, converts orphaned CBRACE to OTHER. Called in all 4 token stream creation points.

**Tests:** `strcat(\{,hello,\})` → `{hello}` and `strcat(a,{\{json\}},b)` → `a{json}b` (escaped braces inside real braces are evaluated by the escape handler, not rewritten).

---

### 2. CPAREN `)` — No Token Rewriting Needed ✓

**Why it doesn't need rewriting:** CPAREN already has a working generic text fallback:
```antlr
beginGenericText:
    { inFunction == 0 }? CPAREN     // ← orphaned ')' outside functions = generic text
    | ...
```

**Orphan scenarios:**

| Pattern | Inside Function? | Result | Problem? |
|---------|-----------------|--------|----------|
| `\(text)` | No | CPAREN → generic text via predicate | ✓ No |
| `\(text)` | Yes | CPAREN tries to close enclosing function | ⚠️ Maybe |
| `\(text\)` | Either | Both escaped → no orphaned tokens | ✓ No |

**The inside-function case:** When `\(text)` appears inside `strcat(...,\(text),...)`, the `)` after `text` would try to close `strcat` prematurely. However, in practice PennMUSH softcode always escapes BOTH parentheses (`\(text\)`) or NEITHER. The BBS script confirms this:
- Line 83: `\([name(...)]\)` — both escaped
- Line 89: `\(#[member(...)]\)` — both escaped
- Line 101: `\(-\)` — both escaped

**Recommendation:** **No action needed.** The existing predicate handles the common cases, and real MUSH code escapes both parentheses symmetrically.

---

### 3. SEMICOLON `;` — No Token Rewriting Needed ✓

**Why:** Has a working predicate (`{ !inCommandList || inBraceDepth > 0 }?`). An escaped `\;` produces `ESCAPE+ANY(';')`, which is consumed as `escapedText` — no orphaned SEMICOLON is created. The predicate only matters for command lists where `;` separates commands, and braces already protect semicolons from command-level splitting.

**Recommendation:** **No action needed.**

---

### 4. COMMAWS `,` — No Token Rewriting Needed ✓

**Why:** Has a working predicate. Escaped `\,` produces `ESCAPE+ANY(',')` consumed as `escapedText`. The 2 BBS uses of `\,` (lines 34, 101) work correctly because the escape prevents the comma from being tokenized as COMMAWS in the first place.

However, there's a subtle observation: `\,` never creates an orphaned COMMAWS in the first place. The backslash pushes the lexer into ESCAPING mode (`SharpMUSHLexer.g4:12`), where the `,` is captured as an `ANY` token — NOT as COMMAWS. So the token sequence is `ESCAPE ANY(',')`, which the parser consumes as `escapedText`. No orphaned COMMAWS exists.

**Recommendation:** **No action needed.**

---

### 5. EQUALS `=` — No Token Rewriting Needed ✓

**Why:** Has a working predicate. Escaped `\=` produces `ESCAPE+ANY('=')` consumed as `escapedText`. The predicate only activates during eq-split command parsing.

**Recommendation:** **No action needed.**

---

### 6. CCARET `>` — No Token Rewriting Needed ✓

**Why:** Has a working predicate. Only relevant inside `%q<EXPR>` register lookups. Escaped `\>` produces `ESCAPE+ANY('>')` consumed as `escapedText`.

**Recommendation:** **No action needed.**

---

## Summary

| Token | Needs Token Rewriting? | Reason |
|-------|----------------------|--------|
| **CBRACK** `]` | ✅ **Already implemented** | No generic text fallback; predicate causes AdaptivePredict hang |
| **CBRACE** `}` | ✅ **Implemented** | Same structural pattern as CBRACK; JSON makes `\{...\}` common |
| **CPAREN** `)` | ❌ No | Existing predicate works; code escapes both parens symmetrically |
| **SEMICOLON** `;` | ❌ No | Existing predicate works; escape prevents SEMICOLON tokenization |
| **COMMAWS** `,` | ❌ No | Existing predicate works; escape prevents COMMAWS tokenization |
| **EQUALS** `=` | ❌ No | Existing predicate works; escape prevents EQUALS tokenization |
| **CCARET** `>` | ❌ No | Existing predicate works; escape prevents CCARET tokenization |

### Key Insight

The token rewriting approach is specifically valuable for tokens that:
1. **Have NO generic text fallback** in `beginGenericText` (CBRACK, CBRACE)
2. **Cannot have a predicate added** without causing AdaptivePredict hang
3. **Have an asymmetric escape pattern** where the opener is escaped but the closer is not

Both **CBRACK** and **CBRACE** met all three criteria and are now handled via token stream rewriting. All other dual-role tokens already have working predicate-based fallbacks that don't cause prediction hangs, because their structural entry points have bounded prediction (function entry requires FUNCHAR, command lists use explicit flags, etc.).

### Why CPAREN's predicate works but CBRACK/CBRACE's would hang

CPAREN's predicate `{ inFunction == 0 }?` doesn't cause AdaptivePredict hangs because **function entry** is bounded by the very specific `FUNCHAR` token (which requires `[0-9a-zA-Z_~@\`]+` followed by `(`). The ATN simulator can quickly determine "this is/isn't a function" by checking whether FUNCHAR is the next token.

CBRACK and CBRACE predicates would cause hangs because **bracket/brace entry** is NOT bounded by a specific entry token — `OBRACK` and `OBRACE` are single characters that can appear anywhere in the recursive evaluation chain, creating exponential prediction path exploration.
