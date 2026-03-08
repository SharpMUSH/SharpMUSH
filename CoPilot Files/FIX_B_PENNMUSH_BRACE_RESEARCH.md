# Fix B Research: PennMUSH Brace `{}` Behavior

## Executive Summary

PennMUSH braces have **two distinct modes** depending on context, controlled by the
`PE_COMMAND_BRACES` flag in `process_expression()`. The original Fix B proposal to simply
remove `{inBraceDepth == 0}?` from the function rule is **partially correct** but
**incomplete** — it handles the grammar/parser level correctly but requires corresponding
visitor-level changes to match PennMUSH's evaluation suppression in function argument braces.

---

## 1. PennMUSH Source Analysis

### 1.1 Key Flags (from `hdrs/parse.h`)

```c
// Evaluation flags (eflags)
#define PE_COMPRESS_SPACES    0x00000001
#define PE_STRIP_BRACES       0x00000002  // Strip outer braces
#define PE_COMMAND_BRACES     0x00000004  // First brace in a command
#define PE_EVALUATE           0x00000010  // Enable %, [], \ evaluation
#define PE_FUNCTION_CHECK     0x00000020  // Enable function call recognition
#define PE_FUNCTION_MANDATORY 0x00000040  // Error if function not found
#define PE_LITERAL            0x00000100  // Everything literal (used by lit())

// Default flags for top-level evaluation
#define PE_DEFAULT  (PE_COMPRESS_SPACES | PE_STRIP_BRACES | PE_DOLLAR | \
                     PE_EVALUATE | PE_FUNCTION_CHECK)

// Termination flags (tflags)
#define PT_BRACE   0x00000001  // '}' terminates
#define PT_BRACKET 0x00000002  // ']' terminates
#define PT_PAREN   0x00000004  // ')' terminates
#define PT_COMMA   0x00000008  // ',' terminates
#define PT_SEMI    0x00000010  // ';' terminates
#define PT_EQUALS  0x00000020  // '=' terminates
```

### 1.2 Brace Handling in `process_expression()` (from `src/parse.c`)

```c
case '{':
    // Output { only if NOT stripping braces
    if (!(eflags & (PE_STRIP_BRACES | PE_COMMAND_BRACES)))
        safe_chr('{', buff, bp);
    (*str)++;

    // Recurse with different eflags depending on brace type:
    process_expression(buff, bp, str, executor, caller, enactor,
        eflags & PE_COMMAND_BRACES
            ? (eflags & ~PE_COMMAND_BRACES)                      // MODE 1: Command braces
            : (eflags & ~(PE_STRIP_BRACES | PE_FUNCTION_CHECK)), // MODE 2: Function arg braces
        PT_BRACE,  // <-- ONLY '}' terminates!
        pe_info);

    if (**str == '}') {
        if (!(eflags & (PE_STRIP_BRACES | PE_COMMAND_BRACES)))
            safe_chr('}', buff, bp);
        (*str)++;
    }
    eflags &= ~PE_COMMAND_BRACES;  // Only strip one set of braces
    break;
```

---

## 2. Two Distinct Brace Modes

### Mode 1: Command Braces (`PE_COMMAND_BRACES` set)

**Context**: First `{` in a command, e.g., `@switch %0=1,{@pemit %#=hello}`

**eflags for recursive call**: `eflags & ~PE_COMMAND_BRACES`
- ALL evaluation flags **preserved** (including `PE_FUNCTION_CHECK` and `PE_EVALUATE`)
- Functions inside braces **ARE evaluated**
- Braces are **stripped** (outer `{` and `}` not output)

**Example**:
```
@switch %0=1,{@pemit %#=[ljust(name(%#),20)]}
                      ^^^^^^^^^^^^^^^^^^^^^^^^
                      Full evaluation. ljust() receives TWO arguments.
```

### Mode 2: Function Argument Braces (`PE_COMMAND_BRACES` not set)

**Context**: Braces inside function arguments, e.g., `strcat(a,{b,c},d)`

**eflags for recursive call**: `eflags & ~(PE_STRIP_BRACES | PE_FUNCTION_CHECK)`
- **`PE_FUNCTION_CHECK` removed** — function calls NOT recognized
- **`PE_EVALUATE` preserved** — `%` substitutions and `\` escaping still active
- **`PE_STRIP_BRACES` removed** — inner braces preserved literally
- Outer braces **stripped** (if `PE_STRIP_BRACES` was set in the calling context)

**Example**:
```
strcat(a,{add(1,2),b},c)
         ^^^^^^^^^^^^^^^
         PE_FUNCTION_CHECK removed. add() is NOT recognized as a function.
         ( is literal, , is literal (PT_COMMA not in tflags), ) is literal.
         Result: strcat("a", "add(1,2),b", "c") → "aadd(1,2),bc"
```

---

## 3. Comprehensive Feature Matrix Inside `{}`

### 3.1 Termination Flags

Inside brace recursion, `tflags = PT_BRACE` (only `}` terminates).

| Character | Terminates? | Flag | Explanation |
|-----------|:-----------:|------|-------------|
| `}` | ✅ YES | `PT_BRACE` set | Closes the brace group |
| `,` | ❌ NO | `PT_COMMA` not set | Comma is inert — literal text |
| `)` | ❌ NO | `PT_PAREN` not set | Close paren is inert |
| `;` | ❌ NO | `PT_SEMI` not set | Semicolon is inert |
| `=` | ❌ NO | `PT_EQUALS` not set | Equals is inert |
| `]` | ❌ NO | `PT_BRACKET` not set | Close bracket is inert |
| ` ` | ❌ NO | `PT_SPACE` not set | Space is normal |

### 3.2 Evaluation Features

| Feature | Function Arg `{}` | Command `{}` | Mechanism |
|---------|:------------------:|:------------:|-----------|
| Function calls `func()` | ❌ NOT recognized | ✅ Evaluated | `PE_FUNCTION_CHECK` removed / preserved |
| `%` substitutions (`%#`, `%0`, `%r`) | ✅ Evaluated | ✅ Evaluated | `PE_EVALUATE` preserved in both |
| `[...]` bracket evaluation | ✅ Evaluated¹ | ✅ Evaluated | `[]` re-enables `PE_FUNCTION_CHECK` |
| `\` escape sequences | ✅ Active | ✅ Active | `PE_EVALUATE` preserved |
| Space compression | ✅ Active | ✅ Active | `PE_COMPRESS_SPACES` preserved |
| `,` as function arg separator | N/A² | ✅ Active | Function calls work → commas work |
| Inner `{}` nesting | Literal³ | Literal³ | Tracked for matching, inner content also suppressed |
| Outer brace stripping | ✅ Stripped | ✅ Stripped | `PE_STRIP_BRACES` in calling context |

**Notes:**
1. `[...]` inside braces RE-ENABLES function checking. This is intentional — `[add(1,2)]` evaluates even inside braces.
2. Since function calls aren't recognized in function arg braces, there are no function argument commas to split.
3. Inner `{}` pairs are matched for nesting purposes. Their content also has `PE_FUNCTION_CHECK` removed.

### 3.3 How `[...]` Re-enables Function Checking Inside Braces

```c
case '[':
    if (!(eflags & PE_EVALUATE)) {
        safe_chr('[', buff, bp);  // Literal bracket
        temp_eflags = eflags & ~PE_STRIP_BRACES;
    } else
        temp_eflags = eflags | PE_FUNCTION_CHECK | PE_FUNCTION_MANDATORY;
        // ^^ PE_FUNCTION_CHECK added back!
```

Since `PE_EVALUATE` is still set inside braces, `[...]` adds `PE_FUNCTION_CHECK` back.
This means `{[add(1,2)]}` evaluates the `add()` even though bare `{add(1,2)}` does not.

### 3.4 How `(` Becomes Literal Inside Braces

```c
case '(':
    (*str)++;
    if (!(eflags & PE_EVALUATE) || !(eflags & PE_FUNCTION_CHECK)) {
        safe_chr('(', buff, bp);  // Literal paren - NOT a function call
        // ... recurse with PT_PAREN to find matching )
    } else {
        // Function call processing...
    }
```

Since `PE_FUNCTION_CHECK` is removed inside function arg braces, `(` after text is
just a literal character. `add(1,2)` → literal text `add(1,2)`, not a function call.

---

## 4. Concrete PennMUSH Examples

### 4.1 Function Argument Braces

```
think strcat(a,{b,c},d)
→ Output: ab,cd
  Explanation: {b,c} is one argument, braces stripped, comma is literal.

think strcat(a,{add(1,2)},b)
→ Output: aadd(1,2)b
  Explanation: add() NOT evaluated. ( is literal, , is literal, ) is literal.
               Braces stripped. Content is literal "add(1,2)".

think strcat(a,{[add(1,2)]},b)
→ Output: a3b
  Explanation: [...] re-enables PE_FUNCTION_CHECK. add(1,2) IS evaluated to 3.

think strcat(a,{%#},b)
→ Output: a#123b  (where 123 is enactor's dbref)
  Explanation: % substitutions still active (PE_EVALUATE preserved).

think strcat(a,{b;c},d)
→ Output: ab;cd
  Explanation: Semicolons are inert inside braces (PT_SEMI not in tflags).

think strcat(a,{{b,c}},d)
→ Output: a{b,c}d
  Explanation: Outer braces stripped. Inner braces preserved literally.
               Content "b,c" is literal (comma inert).

think cat(a,b,{c,d},e)
→ Output: a b c,d e
  Explanation: cat() gets 4 args: "a", "b", "c,d", "e" (comma inside braces
               doesn't split). Braces stripped from 3rd argument.
```

### 4.2 Command Braces

```
@switch %0=1,{@pemit %#=[ljust(name(%#),20)]}
→ Full evaluation. ljust() recognized and receives 2 arguments.
   PE_FUNCTION_CHECK preserved. PE_COMMAND_BRACES stripped.

&ATTR me={say hello;say world}
→ Content stored as: say hello;say world
   Braces are grouping delimiters for command storage.
   When attribute is evaluated, the content is processed normally.
```

---

## 5. Implications for SharpMUSH Fix B

### 5.1 Current Grammar Issue

The current grammar has `{inBraceDepth == 0}?` on the function rule:

```antlr
function:
    FUNCHAR {++inFunction;}
    (evaluationString? ({inBraceDepth == 0}? COMMAWS evaluationString?)*)?
    CPAREN {--inFunction;}
;
```

This blocks ALL commas from being function argument separators when inside ANY brace depth.
This is incorrect because:
1. In **command braces**, PennMUSH DOES allow function calls with multiple arguments
2. In **function argument braces**, PennMUSH doesn't recognize function calls at all
   (so the comma issue is moot — there are no function calls to split arguments for)

### 5.2 Why the Simple Fix B (Remove Predicate) Works at the Parser Level

Removing `{inBraceDepth == 0}?` from the function rule would allow the ANTLR parser to
recognize function call structure inside braces. This is **correct at the parser level**
because:

1. **For command braces**: Functions SHOULD be recognized and receive comma-separated
   arguments. Removing the predicate allows this.

2. **For function argument braces**: Even though PennMUSH doesn't recognize `add(` as a
   function call, the ANTLR parser always recognizes `FUNCHAR` tokens at the lexer level.
   The token `add(` is ALWAYS a `FUNCHAR` regardless of context — the lexer doesn't know
   about brace depth. So the parser WILL create a function node for `add(1,2)` inside braces.
   But the **visitor** can then choose not to evaluate it.

3. **Comma protection still works**: Inside a `bracePattern`, the `explicitEvaluationString`
   rule's `beginGenericText` already has `{ inBraceDepth > 0 }? COMMAWS` which makes commas
   generic text. So commas at the brace level (not inside a nested function) are already
   protected:
   ```
   strcat(a,{x,y},b)
             ^^^
             This comma is in bracePattern's explicitEvaluationString.
             beginGenericText's COMMAWS predicate: {inBraceDepth > 0} → TRUE.
             So this comma is generic text, NOT a function arg separator. ✅
   ```

### 5.3 What the Visitor Must Handle

After removing the grammar predicate, the **visitor** must distinguish between:

1. **Command braces** (depth 1, command list context):
   - Evaluate all function calls inside braces normally
   - Strip outer braces
   - This is what the existing `VisitBracePattern` already does at `_braceDepthCounter <= 1`

2. **Function argument braces** (braces inside a function's argument):
   - **Do NOT evaluate function calls** — return literal text
   - **DO evaluate `%` substitutions** — PennMUSH keeps PE_EVALUATE active
   - **DO evaluate `[...]` brackets** — PennMUSH re-enables PE_FUNCTION_CHECK inside brackets
   - Strip outer braces

The current `VisitBracePattern` visitor code does NOT make this distinction properly.
It always visits children (which evaluates functions), and only handles brace stripping
vs literal brace preservation based on `_braceDepthCounter`.

### 5.4 Current Visitor Behavior

```csharp
public override async ValueTask<CallState?> VisitBracePattern(BracePatternContext context)
{
    _braceDepthCounter++;
    CallState? result;
    var vc = await VisitChildren(context);  // ← Always evaluates!

    if (_braceDepthCounter <= 1)
    {
        result = vc ?? new CallState(GetContextText(context), context.Depth());
    }
    else
    {
        result = vc is not null
            ? vc with { Message = MModule.multiple(["{", vc.Message, "}"]) }
            : new CallState(GetContextText(context), context.Depth());
    }

    _braceDepthCounter--;
    return result;
}
```

**Current issues with the visitor**:
- `VisitChildren(context)` always evaluates function calls, even in function argument braces
- The `_braceDepthCounter` check only handles brace stripping, not evaluation suppression
- No distinction between command braces and function argument braces

### 5.5 Required Visitor Changes for Full PennMUSH Compatibility

To match PennMUSH, `VisitBracePattern` would need to:

1. **Detect context**: Is this a command brace or function argument brace?
   - Command brace: bracePattern at top level or in command list
   - Function argument brace: bracePattern inside a function's evaluationString

2. **For function argument braces**:
   - Suppress function evaluation (don't visit function nodes)
   - Still evaluate `%` substitutions
   - Still evaluate `[...]` brackets
   - Strip outer braces

3. **For command braces**:
   - Full evaluation (current behavior at depth 1)
   - Strip outer braces

### 5.6 Parser-Level vs Visitor-Level Solution

| Concern | Parser Level | Visitor Level |
|---------|:---:|:---:|
| FUNCHAR lexer recognition | Cannot suppress¹ | N/A |
| Function argument splitting | Fixed by removing predicate | N/A |
| Function evaluation suppression | N/A | Must suppress in function arg braces |
| `%` substitution inside braces | Already works | Already works |
| `[...]` evaluation inside braces | Already works | Already works |
| `,` as generic text in brace content | Already works² | N/A |

**Notes:**
1. The ANTLR lexer always tokenizes `add(` as FUNCHAR. This cannot be context-dependent.
2. The `beginGenericText` rule's `{ inBraceDepth > 0 }? COMMAWS` already handles this.

---

## 6. Existing Test Cases

### 6.1 Tests That Currently Pass

From `SharpMUSH.Tests/Parser/FunctionUnitTests.cs`:

| Input | Expected | Tests |
|-------|----------|-------|
| `strcat(a,b,{c,def})` | `abc,def` | Comma inside braces is literal |
| `strcat(a,b,{{c,def}})` | `ab{c,def}` | Double braces → literal `{c,def}` |
| `add({1},{2})[add(5,6)]word()` | `311word()` | Braced args as literals + bracket eval |

### 6.2 Test Cases That Would Validate Fix B

These are the cases that currently FAIL or produce errors due to `{inBraceDepth == 0}?`:

```csharp
// Command brace context - functions inside braces should work:
// {ljust(name(%#),20)}  → ljust receives 2 args
// {ifelse(get(obj/attr),div(1,2),none)}  → ifelse receives 3 args
// {extract(get(%q0/MESS_LST),##,1)}  → extract receives 3 args

// Function argument braces - functions should NOT evaluate:
// strcat(a,{add(1,2)},b)  → "aadd(1,2)b" (not "a3b")
// strcat(a,{[add(1,2)]},b)  → "a3b" (brackets re-enable function check)
```

---

## 7. Summary of PennMUSH Brace Semantics

### What `{}` ALWAYS does (both modes):
- `,` is inert (not an argument or command separator)
- `;` is inert (not a command separator)
- `)` is inert (not a function closer)
- `=` is inert (not a command split)
- Inner `{...}` pairs are matched for nesting

### What `{}` does in **function argument context**:
- Function calls `func()` are **NOT recognized** (PE_FUNCTION_CHECK removed)
- `%` substitutions **ARE evaluated** (PE_EVALUATE preserved)
- `[...]` bracket evaluation **IS active** (re-enables PE_FUNCTION_CHECK)
- `\` escaping **IS active** (PE_EVALUATE preserved)
- Outer braces are **stripped**

### What `{}` does in **command brace context**:
- Function calls `func()` **ARE recognized** (PE_FUNCTION_CHECK preserved)
- Everything is fully evaluated
- Outer braces are **stripped**
- Subsequent braces switch to function argument mode

---

## 8. References

- PennMUSH source: `src/parse.c`, function `process_expression()`, `case '{'` handler
- PennMUSH headers: `hdrs/parse.h`, PE_* and PT_* flag definitions
- SharpMUSH grammar: `SharpMUSH.Parser.Generated/SharpMUSHParser.g4`
- SharpMUSH visitor: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs`, `VisitBracePattern()`
- Existing Fix B proposal: `CoPilot Files/ANTLR4_FIX_PROPOSALS.md`, Section 3
