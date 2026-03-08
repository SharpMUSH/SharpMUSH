# Fix B: ANTLR4 Implementation Recommendations

## Based on PennMUSH Brace Behavior Research

This document translates the PennMUSH brace semantics (documented in
`FIX_B_PENNMUSH_BRACE_RESEARCH.md`) into concrete ANTLR4 grammar and visitor
recommendations. It builds on the existing Fix B proposal in `ANTLR4_FIX_PROPOSALS.md`
with refined understanding of PennMUSH's two-mode brace behavior.

---

## 1. Architectural Mismatch: PennMUSH vs ANTLR4

### 1.1 PennMUSH: Single-Pass Flag-Based Evaluation

PennMUSH uses a single `process_expression()` function that combines lexing, parsing,
and evaluation in one pass. Two sets of runtime flags control behavior:

- **eflags** (evaluation flags): What to evaluate (`PE_FUNCTION_CHECK`, `PE_EVALUATE`, etc.)
- **tflags** (termination flags): What characters terminate the current expression (`PT_COMMA`, `PT_BRACE`, etc.)

When braces are encountered, PennMUSH modifies these flags for the recursive call:

```c
// Function argument braces:
eflags & ~(PE_STRIP_BRACES | PE_FUNCTION_CHECK)  // Remove function checking
tflags = PT_BRACE                                 // Only } terminates

// Command braces:
eflags & ~PE_COMMAND_BRACES                       // Keep function checking
tflags = PT_BRACE                                 // Only } terminates
```

Key insight: In PennMUSH, `add(` is **never even recognized** as a function call inside
function-arg braces because `PE_FUNCTION_CHECK` is removed. The `(` character is just
literal text — no function lookup, no argument splitting.

### 1.2 ANTLR4: Separate Lexer → Parser → Visitor Pipeline

ANTLR4 strictly separates three phases:

1. **Lexer**: Tokenizes input into a stream of tokens. Context-free.
2. **Parser**: Builds a parse tree from tokens using grammar rules. Limited context via semantic predicates.
3. **Visitor**: Walks the parse tree and performs evaluation. Full context available.

The critical constraint: The ANTLR4 **lexer always tokenizes `add(` as a `FUNCHAR` token**
regardless of whether it's inside braces:

```antlr
FUNCHAR: [0-9a-zA-Z_~@`]+ '(' WS ;  // Always matches — no context
```

This cannot be changed without lexer modes (which would be prohibitively complex for MUSH
parsing). Therefore, the parser WILL always see function structure inside braces.

### 1.3 The Solution: Parse Maximally, Evaluate Selectively

Since the lexer cannot suppress `FUNCHAR` tokenization inside braces, the correct
architectural approach is:

> **Let the parser recognize ALL syntactic structure (including function calls inside braces),
> then let the visitor decide what to evaluate vs. what to return as literal text.**

This is the "parse maximally, evaluate selectively" principle. It maps PennMUSH's
runtime flag system to ANTLR4's phased architecture:

| PennMUSH Phase | ANTLR4 Equivalent | What Handles It |
|----------------|-------------------|-----------------|
| Lexing (character recognition) | Lexer | `FUNCHAR`, `COMMAWS`, etc. — always active |
| Parsing (structure recognition) | Parser | Grammar rules — always recognizes function structure |
| `PE_FUNCTION_CHECK` flag | Visitor | `VisitFunction()` — can suppress evaluation |
| `PE_EVALUATE` flag | Visitor | `VisitBracePattern()` — context-dependent |
| `PT_COMMA` / `PT_BRACE` tflags | Parser + Visitor | Semantic predicates + visitor context |

---

## 2. Grammar Recommendation: Remove Brace Depth Predicate

### 2.1 The Change

Remove `{inBraceDepth == 0}?` from the `function` rule's `COMMAWS`:

```antlr
// CURRENT (incorrect):
function: 
    FUNCHAR {++inFunction;} 
    (evaluationString? ({inBraceDepth == 0}? COMMAWS evaluationString?)*)?
    CPAREN {--inFunction;} 
;

// RECOMMENDED (correct):
function: 
    FUNCHAR {++inFunction;} 
    (evaluationString? (COMMAWS evaluationString?)*)?
    CPAREN {--inFunction;} 
;
```

### 2.2 Why This is Correct

1. **Command braces**: PennMUSH DOES evaluate functions inside command braces with
   multiple arguments. The predicate currently blocks this. Removing it allows proper
   parsing.

2. **Function argument braces**: PennMUSH doesn't recognize `add(` as a function call
   at all. But the ANTLR lexer ALWAYS creates `FUNCHAR` tokens. Since the parser will
   see function structure regardless, it should parse it correctly (with proper argument
   splitting via commas). The **visitor** then handles whether to evaluate or literalize.

3. **Comma protection preserved**: Commas at the brace content level (not inside a nested
   function call) are already handled by `beginGenericText`:
   ```antlr
   // In beginGenericText — this remains UNCHANGED:
   | { (!lookingForCommandArgCommas && inFunction == 0) || inBraceDepth > 0 }? COMMAWS
   ```
   This predicate ensures that commas in `{x,y}` (brace content level, `inFunction == 0`)
   are generic text. Commas inside a nested function call `{add(1,2)}` are parsed by the
   function rule (where `inFunction > 0`), which is correct.

### 2.3 What NOT to Change

These existing predicates remain **correct and unchanged**:

```antlr
// commandList — semicolons blocked inside braces ✅
commandList: command ({inBraceDepth == 0}? SEMICOLON command)*;

// commaCommandArgs — command-level commas blocked inside braces ✅
commaCommandArgs:
    {lookingForCommandArgCommas = true;} evaluationString? (
        {inBraceDepth == 0}? COMMAWS evaluationString?
    )* {lookingForCommandArgCommas = false;}
;

// beginGenericText — commas as generic text inside braces ✅
| { (!lookingForCommandArgCommas && inFunction == 0) || inBraceDepth > 0 }? COMMAWS
```

---

## 3. Visitor Recommendations

### 3.1 Current VisitBracePattern Behavior

```csharp
public override async ValueTask<CallState?> VisitBracePattern(BracePatternContext context)
{
    _braceDepthCounter++;
    var vc = await VisitChildren(context);  // Always evaluates everything

    if (_braceDepthCounter <= 1)
        result = vc ?? new CallState(...);   // Strip braces (depth 1)
    else
        result = vc with { Message = "{" + vc.Message + "}" };  // Preserve inner braces

    _braceDepthCounter--;
    return result;
}
```

**Problem**: `VisitChildren(context)` always evaluates all child nodes, including function
calls. This is correct for command braces but incorrect for function argument braces.

### 3.2 Recommended VisitBracePattern Behavior

The visitor needs to distinguish two contexts:

#### Context Detection

Determine whether the current brace is a **command brace** or **function argument brace**
by checking the parse tree ancestry:

```
Command braces: bracePattern is child of commandList/command/startCommandString
Function arg braces: bracePattern is child of a function's evaluationString
```

In practice, this can be detected by checking if the bracePattern appears inside a
`function` node's argument list. There are multiple ways to implement this:

**Option 1: Check parser context ancestry**
Walk up the parse tree from the bracePattern to see if an ancestor is a `function` context.
If so, this is a function argument brace.

**Option 2: Use visitor state**
Track whether we're currently inside a function's argument list via a boolean or counter
in the visitor state. When entering `VisitFunction`, set a flag; when entering
`VisitBracePattern`, check the flag.

**Option 3: Use the existing `_braceDepthCounter`**
The current `_braceDepthCounter <= 1` check is a rough proxy for "command brace" vs
"function argument brace." However, this doesn't handle all cases correctly (e.g., braces
in command braces inside function arguments). A proper implementation would need explicit
context tracking.

#### Recommended Visitor Behavior by Context

**For command braces** (braces in command/commandList context):
- Evaluate everything normally (current behavior at depth 1)
- Strip outer braces
- This matches PennMUSH's `PE_COMMAND_BRACES` mode

**For function argument braces** (braces inside a function's argument):
- **DO NOT evaluate function calls** — if a `function` node appears inside the brace
  content, return its literal text instead of calling `VisitFunction`
- **DO evaluate `%` substitutions** — `VisitValidSubstitution` should still be called
- **DO evaluate `[...]` brackets** — `VisitBracketPattern` should still be called,
  which re-enables full evaluation (matching PennMUSH's bracket handler that re-adds
  `PE_FUNCTION_CHECK`)
- **Strip outer braces** (matching PennMUSH's brace-stripping behavior)

### 3.3 Selective Evaluation Pattern

The key visitor pattern for function-arg braces is **selective child visitation**:

```
Instead of:
    VisitChildren(context)  // Evaluates ALL children including functions

Do:
    For each child node in bracePattern's content:
        if child is function node AND we're in function-arg-brace context:
            → Return literal text of the function node (context.GetText())
        if child is bracketPattern:
            → Visit normally (brackets re-enable function evaluation)
        if child is PERCENT validSubstitution:
            → Visit normally (% subs are always evaluated)
        if child is genericText/beginGenericText:
            → Visit normally (literal text)
        if child is bracePattern (nested):
            → Visit with same function-arg-brace suppression
```

This maps directly to PennMUSH's behavior:

| Parse Tree Node | PennMUSH Equivalent | Behavior in Function-Arg Braces |
|-----------------|---------------------|----------------------------------|
| `function` | `(` with `PE_FUNCTION_CHECK` removed | Return literal text |
| `bracketPattern` | `[` re-enables `PE_FUNCTION_CHECK` | Evaluate normally |
| `PERCENT validSubstitution` | `%` with `PE_EVALUATE` preserved | Evaluate normally |
| `bracePattern` (nested) | `{` recurses with same flags | Apply same suppression |
| `genericText` | Literal characters | Pass through |

### 3.4 Edge Case: Nested Brackets Inside Braces

PennMUSH behavior:
```
strcat(a, {[add(1,2)]}, b)  →  "a3b"
```

Inside `{...}`, the `[...]` bracket handler re-enables `PE_FUNCTION_CHECK`:
```c
case '[':
    if (eflags & PE_EVALUATE)
        temp_eflags = eflags | PE_FUNCTION_CHECK | PE_FUNCTION_MANDATORY;
```

In ANTLR4, `bracketPattern` contains an `evaluationString` which can include `function`
nodes. Since `VisitBracketPattern` already evaluates its contents fully (including function
calls), this "just works" — when the visitor encounters a `bracketPattern` inside a
function-arg brace, it evaluates the bracket normally, which evaluates the function
inside.

### 3.5 Edge Case: Double Braces

PennMUSH behavior:
```
strcat(a, {{b,c}}, d)  →  "a{b,c}d"
```

Outer braces are stripped. Inner braces are preserved literally. Content inside inner
braces also has `PE_FUNCTION_CHECK` removed (but it's already removed from the outer
brace context).

The existing `_braceDepthCounter` mechanism already handles brace stripping vs
preservation. The recommendation is to combine it with the function-evaluation
suppression logic.

---

## 4. PennMUSH Activation/Deactivation Summary for ANTLR4

### 4.1 What PennMUSH Deactivates Inside Function-Arg Braces

These are deactivated by setting `tflags = PT_BRACE` (only `}` terminates) and removing
`PE_FUNCTION_CHECK` from eflags:

| Feature | PennMUSH Mechanism | ANTLR4 Grammar Handling | ANTLR4 Visitor Handling |
|---------|-------------------|------------------------|------------------------|
| `,` as function arg separator | `PT_COMMA` not in tflags | `{inBraceDepth > 0}? COMMAWS` in `beginGenericText` makes commas generic text at brace level | N/A — parser handles this |
| `,` as command arg separator | `PT_COMMA` not in tflags | `{inBraceDepth == 0}? COMMAWS` in `commaCommandArgs` | N/A — parser handles this |
| `;` as command separator | `PT_SEMI` not in tflags | `{inBraceDepth == 0}? SEMICOLON` in `commandList` | N/A — parser handles this |
| `)` as function closer | `PT_PAREN` not in tflags | Parsed by `function` rule — `)` always closes the FUNCHAR's function | **Visitor must suppress function evaluation** |
| `=` as command split | `PT_EQUALS` not in tflags | `{!lookingForCommandArgEquals}?` in `beginGenericText` | N/A — parser handles this |
| Function recognition `func()` | `PE_FUNCTION_CHECK` removed | FUNCHAR always tokenized — parser always sees functions | **Visitor must suppress function evaluation** |

### 4.2 What PennMUSH Keeps Active Inside Function-Arg Braces

These remain active because `PE_EVALUATE` is preserved:

| Feature | PennMUSH Mechanism | ANTLR4 Grammar Handling | ANTLR4 Visitor Handling |
|---------|-------------------|------------------------|------------------------|
| `%` substitutions | `PE_EVALUATE` preserved | `PERCENT validSubstitution` in grammar | Visitor evaluates normally |
| `[...]` evaluation | `[]` re-enables `PE_FUNCTION_CHECK` | `bracketPattern` in grammar | Visitor evaluates normally (brackets fully evaluate) |
| `\` escaping | `PE_EVALUATE` preserved | `ESCAPE ANY` in lexer | Visitor handles normally |
| `{}` nesting | Brace matching | `bracePattern` recurses | Visitor applies same suppression to nested braces |

### 4.3 What PennMUSH Keeps Active Inside Command Braces

Everything is active — command braces preserve all evaluation flags:

| Feature | Active? | Reason |
|---------|:-------:|--------|
| Function calls `func()` | ✅ | `PE_FUNCTION_CHECK` preserved |
| `,` as function arg separator | ✅ | Functions work → commas in functions work |
| `%` substitutions | ✅ | `PE_EVALUATE` preserved |
| `[...]` evaluation | ✅ | Always active with `PE_EVALUATE` |
| `\` escaping | ✅ | `PE_EVALUATE` preserved |
| `,` as command arg separator | ❌ | `PT_COMMA` not in tflags (only `}` terminates) |
| `;` as command separator | ❌ | `PT_SEMI` not in tflags |

---

## 5. Implementation Summary

### 5.1 Grammar Change (Fix B)

**One line change** in `SharpMUSHParser.g4`:

```antlr
// Remove {inBraceDepth == 0}? from function rule:
function: 
    FUNCHAR {++inFunction;} 
    (evaluationString? (COMMAWS evaluationString?)*)?
    CPAREN {--inFunction;} 
;
```

**No other grammar changes needed.** The existing predicates on `commandList`,
`commaCommandArgs`, and `beginGenericText` are already correct.

### 5.2 Visitor Change (New)

Add function-evaluation suppression to `VisitBracePattern`:

1. **Detect context**: Is this brace inside a function's argument list?
2. **If function-arg brace**: Walk children selectively — literalize `function` nodes,
   evaluate `bracketPattern` and `%` substitutions normally
3. **If command brace**: Full evaluation (existing behavior)

### 5.3 What This Achieves

After both changes:

```
// Function arg braces — functions literalized:
strcat(a,{add(1,2)},b)        → "aadd(1,2)b"     ✅ (function not evaluated)
strcat(a,{[add(1,2)]},b)      → "a3b"             ✅ (brackets re-enable evaluation)
strcat(a,{%#},b)               → "a#123b"          ✅ (% subs still active)
strcat(a,{b,c},d)              → "ab,cd"           ✅ (comma literal, braces stripped)
strcat(a,{{b,c}},d)            → "a{b,c}d"         ✅ (inner braces preserved)
cat(a,b,{c,d},e)               → "a b c,d e"       ✅ (comma protected)

// Command braces — full evaluation:
{ljust(name(%#),20)}            → ljust evaluated   ✅ (functions work in cmd braces)
{ifelse(get(o/a),div(1,2),no)} → ifelse evaluated  ✅ (multi-arg functions work)
```

---

## 6. Risk Assessment

### 6.1 Grammar Change Risk: LOW

- Removing a predicate is a simplification — fewer semantic predicates = simpler parsing
- No new tokens, rules, or alternatives added
- All existing tests that don't involve functions inside braces are unaffected
- Functions inside braces now parse with correct argument counts (strictly better)

### 6.2 Visitor Change Risk: MEDIUM

- Selective child visitation is more complex than `VisitChildren(context)`
- Need to correctly detect command vs function-arg brace context
- Need to handle edge cases (nested braces, brackets inside braces, etc.)
- Existing `_braceDepthCounter` logic needs integration with new suppression logic

### 6.3 Testing Strategy

1. **Existing tests**: All current function and brace tests should continue to pass
2. **New function-arg brace tests**: `strcat(a,{add(1,2)},b)` → `"aadd(1,2)b"`
3. **New bracket-in-brace tests**: `strcat(a,{[add(1,2)]},b)` → `"a3b"`
4. **New command brace tests**: Command context with multi-arg functions inside braces
5. **Myrddin BBS regression**: Lines 91, 109, 110, 111 should parse without errors

---

## 7. References

- `CoPilot Files/FIX_B_PENNMUSH_BRACE_RESEARCH.md` — PennMUSH source analysis
- `CoPilot Files/ANTLR4_FIX_PROPOSALS.md` — Original Fix B proposal (Section 3)
- `CoPilot Files/ANTLR4_PARSER_ERROR_ANALYSIS.md` — Root cause analysis
- `SharpMUSH.Parser.Generated/SharpMUSHParser.g4` — Current ANTLR4 grammar
- `SharpMUSH.Parser.Generated/SharpMUSHLexer.g4` — ANTLR4 lexer (FUNCHAR definition)
- `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs` — Visitor implementation
- PennMUSH `src/parse.c` — `process_expression()`, `case '{'` and `case '['` handlers
- PennMUSH `hdrs/parse.h` — PE_* and PT_* flag definitions
