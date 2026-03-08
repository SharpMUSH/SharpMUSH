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

## 2. Grammar Changes (Implemented)

### 2.1 Change 1: Remove Brace Depth Predicate from Function Rule

Remove `{inBraceDepth == 0}?` from the `function` rule's `COMMAWS`:

```antlr
// BEFORE:
function: 
    FUNCHAR {++inFunction;} 
    (evaluationString? ({inBraceDepth == 0}? COMMAWS evaluationString?)*)?
    CPAREN {--inFunction;} 
;

// AFTER:
function: 
    FUNCHAR {++inFunction; ++inFunctionInsideBrace;} 
    (evaluationString? (COMMAWS evaluationString?)*)?
    CPAREN {--inFunction; --inFunctionInsideBrace;} 
;
```

### 2.2 Change 2: Allow Function Recognition Inside Braces

Change `bracePattern` from `explicitEvaluationString` to `evaluationString`:

```antlr
// BEFORE:
bracePattern:
    OBRACE { ++inBraceDepth; } explicitEvaluationString? CBRACE { --inBraceDepth; }
;

// AFTER:
bracePattern:
    OBRACE { ++inBraceDepth; savedFunctionInsideBrace.Push(inFunctionInsideBrace); inFunctionInsideBrace = 0; }
    evaluationString?
    CBRACE { --inBraceDepth; inFunctionInsideBrace = savedFunctionInsideBrace.Pop(); }
;
```

The key change: `explicitEvaluationString` → `evaluationString`. This allows function calls to
be recognized inside braces (the `function` alternative is only in `evaluationString`, not
`explicitEvaluationString`). The `inFunctionInsideBrace` counter is saved/restored per brace
level via a stack so that comma handling is correct at each nesting level.

### 2.3 Change 3: Fix Comma Predicate for Functions Inside Braces

Update the `beginGenericText` COMMAWS predicate to use `inFunctionInsideBrace`:

```antlr
// BEFORE:
| { (!lookingForCommandArgCommas && inFunction == 0) || inBraceDepth > 0 }? COMMAWS

// AFTER:
| { (!lookingForCommandArgCommas && inFunction == 0) || (inBraceDepth > 0 && inFunctionInsideBrace == 0) }? COMMAWS
```

The old predicate `inBraceDepth > 0` made ALL commas generic text inside braces, even
commas inside function calls within brackets inside braces. The new predicate
`inBraceDepth > 0 && inFunctionInsideBrace == 0` only makes commas generic text when at the
brace content level (no function calls started inside the brace). When a function call IS
active inside the brace (e.g., `{[add(1,2)]}`), commas serve as function arg separators.

### 2.4 New Parser Members

```antlr
@parser::members {
    // ... existing members ...
    public int inFunctionInsideBrace = 0;
    public System.Collections.Generic.Stack<int> savedFunctionInsideBrace = new();
}
```

### 2.5 Why This is Correct

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

### 2.6 What NOT to Change

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

// beginGenericText — commas as generic text inside braces (UPDATED predicate) ✅
| { (!lookingForCommandArgCommas && inFunction == 0) || (inBraceDepth > 0 && inFunctionInsideBrace == 0) }? COMMAWS
```

---

## 3. Visitor Changes (Implemented)

### 3.1 Implementation Approach: Suppression Counter

The implemented approach uses **Option 1 (parse tree ancestry check)** combined with a
suppression counter. This is cleaner than selective child visitation because it leverages
the existing `VisitChildren()` mechanism while controlling function evaluation via a flag.

#### New Visitor State

```csharp
private int _suppressFunctionEval;  // When > 0, functions return literal text
```

#### IsInsideFunctionArg Helper

A static helper walks up the parse tree to detect function-arg brace context:

```csharp
private static bool IsInsideFunctionArg(ParserRuleContext context)
{
    var parent = context.Parent;
    while (parent is not null)
    {
        if (parent is SharpMUSHParser.FunctionContext)
            return true;
        parent = parent.Parent;
    }
    return false;
}
```

### 3.2 VisitBracePattern (Implemented)

```csharp
public override async ValueTask<CallState?> VisitBracePattern(BracePatternContext context)
{
    _braceDepthCounter++;

    // Detect function-arg braces by checking parse tree ancestry
    var isFunctionArgBrace = IsInsideFunctionArg(context);
    if (isFunctionArgBrace)
        _suppressFunctionEval++;

    CallState? result;
    var vc = await VisitChildren(context);  // VisitFunction checks _suppressFunctionEval

    if (_braceDepthCounter <= 1)
        result = vc ?? new CallState(GetContextText(context), context.Depth());
    else
        result = vc with { Message = "{" + vc.Message + "}" };

    if (isFunctionArgBrace)
        _suppressFunctionEval--;
    _braceDepthCounter--;
    return result;
}
```

### 3.3 VisitFunction (Implemented)

```csharp
public override async ValueTask<CallState?> VisitFunction(FunctionContext context)
{
    if (parser.CurrentState.ParseMode is ParseMode.NoParse or ParseMode.NoEval)
        return new CallState(context.GetText());

    // PennMUSH: Inside function-arg braces, PE_FUNCTION_CHECK is removed
    if (_suppressFunctionEval > 0)
        return new CallState(context.GetText());

    // ... existing function evaluation logic ...
}
```

### 3.4 VisitBracketPattern (Implemented)

Brackets re-enable function evaluation, matching PennMUSH's `[` handler that adds
`PE_FUNCTION_CHECK | PE_FUNCTION_MANDATORY`:

```csharp
public override async ValueTask<CallState?> VisitBracketPattern(BracketPatternContext context)
{
    // Save and clear suppression — brackets re-enable function evaluation
    var savedSuppress = _suppressFunctionEval;
    _suppressFunctionEval = 0;

    // ... existing bracket evaluation logic ...

    _suppressFunctionEval = savedSuppress;
    return result;
}
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

### 3.5 Edge Case: Nested Brackets Inside Braces (Verified)

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
nodes. The `VisitBracketPattern` implementation saves `_suppressFunctionEval`, sets it
to 0, evaluates children (including function calls), then restores the saved value.
This correctly re-enables function evaluation inside brackets.

**Test**: `strcat(a,{[add(1,2)]},b)` → `"a3b"` ✅

### 3.6 Edge Case: Double Braces (Verified)

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
| `,` as function arg separator | `PT_COMMA` not in tflags | `{inBraceDepth > 0 && inFunctionInsideBrace == 0}? COMMAWS` in `beginGenericText` makes commas generic text at brace level when no function is active inside | N/A — parser handles this |
| `,` as command arg separator | `PT_COMMA` not in tflags | `{inBraceDepth == 0}? COMMAWS` in `commaCommandArgs` | N/A — parser handles this |
| `;` as command separator | `PT_SEMI` not in tflags | `{inBraceDepth == 0}? SEMICOLON` in `commandList` | N/A — parser handles this |
| `)` as function closer | `PT_PAREN` not in tflags | Parsed by `function` rule — `)` always closes the FUNCHAR's function | **Visitor suppresses function evaluation via `_suppressFunctionEval`** |
| `=` as command split | `PT_EQUALS` not in tflags | `{!lookingForCommandArgEquals}?` in `beginGenericText` | N/A — parser handles this |
| Function recognition `func()` | `PE_FUNCTION_CHECK` removed | FUNCHAR always tokenized — parser recognizes function structure via `evaluationString` in bracePattern | **Visitor returns literal text when `_suppressFunctionEval > 0`** |

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

## 5. Implementation Summary (Completed)

### 5.1 Grammar Changes

Three changes in `SharpMUSHParser.g4`:

```antlr
// 1. Function rule: removed {inBraceDepth == 0}?, added inFunctionInsideBrace tracking
function: 
    FUNCHAR {++inFunction; ++inFunctionInsideBrace;} 
    (evaluationString? (COMMAWS evaluationString?)*)?
    CPAREN {--inFunction; --inFunctionInsideBrace;} 
;

// 2. bracePattern: changed from explicitEvaluationString to evaluationString,
//    added inFunctionInsideBrace save/restore via stack
bracePattern:
    OBRACE { ++inBraceDepth; savedFunctionInsideBrace.Push(inFunctionInsideBrace); inFunctionInsideBrace = 0; }
    evaluationString?
    CBRACE { --inBraceDepth; inFunctionInsideBrace = savedFunctionInsideBrace.Pop(); }
;

// 3. beginGenericText: updated COMMAWS predicate to use inFunctionInsideBrace
| { (!lookingForCommandArgCommas && inFunction == 0) || (inBraceDepth > 0 && inFunctionInsideBrace == 0) }? COMMAWS
```

### 5.2 Visitor Changes

Three changes in `SharpMUSHParserVisitor.cs`:

1. **`VisitBracePattern`**: Detect function-arg braces via `IsInsideFunctionArg()` ancestry
   check, increment `_suppressFunctionEval` counter
2. **`VisitFunction`**: When `_suppressFunctionEval > 0`, return `context.GetText()` as
   literal text instead of evaluating the function
3. **`VisitBracketPattern`**: Save `_suppressFunctionEval`, set to 0, visit children,
   restore (brackets re-enable function evaluation)

### 5.3 Test Results

All 2273 existing tests pass. Three new test cases added:

```
// Function arg braces — functions literalized:
strcat(a,{add(1,2)},b)        → "aadd(1,2)b"     ✅ (function not evaluated)
cat(a,b,{c,d},e)               → "a b c,d e"       ✅ (comma protected)

// Brackets inside function-arg braces — re-enable function evaluation:
strcat(a,{[add(1,2)]},b)      → "a3b"             ✅ (brackets re-enable evaluation)

// Existing tests continue to pass:
strcat(a,b,{c,def})            → "abc,def"          ✅ (comma literal, braces stripped)
strcat(a,b,{{c,def}})          → "ab{c,def}"        ✅ (inner braces preserved)
add({1},{2})[add(5,6)]word()   → "311word()"        ✅ (braced simple values still work)
```

---

## 6. Risk Assessment (Post-Implementation)

### 6.1 Grammar Change Risk: LOW ✅ Verified

- Three grammar changes: predicate removal, bracePattern rule, COMMAWS predicate update
- `inFunctionInsideBrace` counter with save/restore stack adds some complexity but is
  well-contained in the parser state
- All 2273 existing tests pass with 0 failures

### 6.2 Visitor Change Risk: LOW ✅ Verified

- The `_suppressFunctionEval` counter approach is simpler than selective child visitation
- `IsInsideFunctionArg` ancestry walk is O(depth) and bounded by parse tree depth
- Existing `_braceDepthCounter` logic is unchanged — suppression is orthogonal
- `VisitBracketPattern` save/restore correctly handles bracket re-enabling

### 6.3 Testing Results

1. **Existing tests**: All 2273 tests pass ✅
2. **New function-arg brace tests**: `strcat(a,{add(1,2)},b)` → `"aadd(1,2)b"` ✅
3. **New bracket-in-brace tests**: `strcat(a,{[add(1,2)]},b)` → `"a3b"` ✅
4. **New comma-protection tests**: `cat(a,b,{c,d},e)` → `"a b c,d e"` ✅

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
