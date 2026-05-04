# Phase 1: Fix 20 Failing PennMUSH Parity Tests

## Goal

Fix the 20 failing tests in `PennMUSHParserGapTests.cs` while adhering to ANTLR4 best practices. All behavior changes happen in the **visitor layer** and **function implementations**, not in the grammar, except where the grammar itself causes incorrect parse trees.

## Current State

- Branch: `pennmush-compatibility`
- 43 gap tests total, 23 pass, 20 fail
- Grammar: 4 files (SharpMUSHLexer.g4, SharpMUSHParser.g4, SharpMUSHBoolExpLexer.g4, SharpMUSHBoolExpParser.g4)
- Visitor: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs` (2163 lines)

## ANTLR4 Architecture Principles (from research)

These principles govern where fixes should live:

1. **Grammar stays structural, not semantic.** The .g4 files define syntax — what's valid input. Behavioral semantics (space compression, literal handling, function dispatch) belong in the visitor. This is the canonical ANTLR4 pattern per Parr's "The Definitive ANTLR4 Reference" and confirmed by Google Group consensus.

2. **Lexer modes are correct as-is.** The SUBSTITUTION mode (`%` -> pushMode) is textbook ANTLR4 for context-sensitive tokenization. The `FUNCHAR` token (`[0-9a-zA-Z_~@\`]+ '(' WS`) correctly captures function-name-then-paren as a single token — this is the recommended "island grammar" approach for DSLs.

3. **`COMMAWS` absorbs surrounding whitespace.** Lexer rule `WS ',' WS` means `cat(a,b    cd        e)` lexes as: `FUNCHAR("cat(")`, `OTHER("a")`, `COMMAWS`, `OTHER("b    cd        e")`, `CPAREN`. The extra spaces within `OTHER` are preserved by the lexer. Space compression must happen in the visitor when collecting function arguments — this is correct ANTLR4 practice (don't discard data in the grammar that you might need).

4. **Semantic predicates (`{ inFunction == 0 }?`) are used sparingly and correctly.** These gate context-sensitive tokens (e.g., `)` as text outside functions). This is a known ANTLR4 pattern for overlapping syntax. No changes needed here.

5. **Visitor pattern > listener pattern for evaluation.** SharpMUSH uses `VisitXxx()` methods returning `CallState` — this is the right pattern for expression evaluation in ANTLR4. Listeners can't return values without side-channel state.

## Failing Tests Grouped by Root Cause

### Group A: FunctionFlags.Literal not implemented in visitor (4 tests)

**Tests:**
- `Lit_EmptyArgument` — `lit()` → `""`, gets `#-1 FUNCTION (lit) EXPECTS AT LEAST 1 ARGUMENTS`
- `Lit_BackslashPassesThrough` — `lit(\)` → `\`, gets `\)` + parse error
- `Lit_CommasAreLiteral` — `lit(a,b,c)` → `a,b,c`, gets `a` (commas split args)
- `QRegister_LeakThroughLit` — `setq(0,test)[lit(%q0)]` → `%q0`, gets `test`

**Root cause:** `FunctionFlags.Literal` is defined in the enum (bit 1) and set on `lit()` at StringFunctions.cs:51, but the visitor at line 519-549 has NO code path that checks for `Literal`. The three branches are:
1. `!NoParse` → evaluate args (normal functions)
2. `NoParse && MaxArgs == 1` → raw substring, single arg
3. `NoParse` (else) → deferred evaluation per-arg

lit() has `NoParse | Literal` and `MaxArgs = int.MaxValue`, so it falls into branch 3. Branch 3 stores unevaluated text per-arg via `GetContextText()` — but the grammar has ALREADY split on `COMMAWS`, so `lit(a,b,c)` produces 3 args not 1. The Literal flag should prevent comma-splitting entirely.

Additionally, `MinArgs = 1` but PennMUSH allows `lit()` with zero args (returns empty string).

**Fix (visitor, not grammar):**

Add a new branch BEFORE the existing three, specifically for `FunctionFlags.Literal`:

```
else if (attribute.Flags.HasFlag(FunctionFlags.Literal))
{
    // Literal: take the raw text between the parens as a single argument.
    // Do NOT split on commas. Do NOT evaluate substitutions.
    // The entire content between ( and ) is one raw string.
    var rawText = MModule.substring(
        context.Start.StartIndex,
        context.Stop.StopIndex - context.Start.StartIndex + 1,
        src);
    refinedArguments = [new CallState(rawText, contextDepth)];
}
```

This uses `context.Start.StartIndex` / `context.Stop.StopIndex` from the `function` parse rule to grab the raw source text between parens. This is idiomatic ANTLR4 — the parse tree always has access to the original token stream positions.

Also change `lit()` definition: `MinArgs = 0` to allow empty args.

**ANTLR4 principle applied:** Grammar already parsed correctly (commas are COMMAWS tokens). The Literal flag is a semantic concern — the visitor should bypass the per-arg evaluation and instead grab raw source text. This keeps the grammar general-purpose.

**Files to change:**
- `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs` — add Literal branch (~lines 519-549)
- `SharpMUSH.Implementation/Functions/StringFunctions.cs` — change `MinArgs = 1` to `MinArgs = 0` on lit()

### Group B: PE_COMPRESS_SPACES — space compression in function args (1 test)

**Test:**
- `SpaceCompression_CatMultipleSpacesInArgs` — `cat(a,b    cd        e)` → `a b cd e`, gets `a b    cd        e`

**Root cause:** PennMUSH applies PE_COMPRESS_SPACES when evaluating function arguments: multiple consecutive spaces in an argument are compressed to a single space. The lexer correctly preserves spaces in `OTHER` tokens. The visitor at line 519-527 evaluates args but does NOT compress spaces.

In PennMUSH source (parse.c), PE_COMPRESS_SPACES is applied during evaluation when inside a function argument context. This is NOT general — it only applies to function args, not to top-level text.

**Fix (visitor):**

After evaluating each function argument (line 521-529, the `!NoParse` branch), apply space compression to the resulting MString. This should be a post-processing step after `VisitChildren`:

```csharp
// After getting the evaluated message, compress consecutive spaces
var msg = (await visitor.VisitChildren(x))?.Message ?? MModule.empty();
if (compressSpaces) msg = MModule.compressSpaces(msg); // new MModule method
```

If `MModule.compressSpaces` doesn't exist, implement it as a simple regex or span-based replacement of `  +` → ` `.

**ANTLR4 principle applied:** The lexer correctly tokenizes `OTHER("b    cd        e")` preserving all spaces. Space compression is evaluation-time semantics, not parsing — the visitor is the right place.

**Files to change:**
- `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs` — add space compression in arg evaluation
- Possibly `MString`/`MModule` — add `compressSpaces` utility if not present

### Group C: %? returns "N M" not just one number (5 tests)

**Tests:**
- `PercentQuestion_ReturnsTwoNumbers` — `%?` → `"N M"`, gets single number
- `PercentQuestion_InvocationsIncrementWithFunctionCalls` — `[add(1,2)]%?` → `"3N M"` with N > 0
- `PercentQuestion_NestedFunctionCalls` — `[add(1,[mul(2,3)])]%?` → `"7N M"`
- `PercentQuestion_NoFunctionCalls` — `hello %?` → `"hello N M"`
- `PercentQuestion_InsideFunctionArg` — `[cat(%?,done)]` → `"N M done"`

**Root cause:** In `Substitutions.cs:81`:
```csharp
"?" => parser.State.Count().ToString(),
```
This returns just the state stack depth as one number. PennMUSH's `%?` returns TWO space-separated numbers: `invocation_count recursion_count`.

- **Invocation count**: number of function calls evaluated so far in this expression
- **Recursion count**: current depth of user-defined function recursion (u()/ufun()/etc.)

The parser needs to track both counters.

**Fix:**

1. Add `InvocationCount` property to the parser state (or on `IMUSHCodeParser`). Increment it each time a built-in function is successfully called in the visitor.
2. Add `RecursionCount` property tracking user-function nesting depth (may already exist as `FunctionRecursionLimit` tracking in `AttributeService`).
3. Change `Substitutions.cs:81` to:
```csharp
"?" => $"{parser.InvocationCount} {parser.RecursionCount}",
```

**ANTLR4 principle applied:** Token `INVOCATION_DEPTH` (lexer) is just a token. The semantic meaning (two counters) is purely a visitor/runtime concern. The grammar doesn't need to know what %? means.

**Files to change:**
- `SharpMUSH.Implementation/Substitutions/Substitutions.cs` — change %? output format
- Parser state / `IMUSHCodeParser` interface — add `InvocationCount` and `RecursionCount` properties
- `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs` — increment `InvocationCount` on each function call

### Group D: fn() calls built-in functions, not attribute functions (8 tests)

**Tests:**
- `Fn_CallsBuiltinFunctions(fn(add,1,2), 3)` — gets `""`
- `Fn_CallsBuiltinFunctions(fn(mul,3,4), 12)` — gets `""`
- `Fn_CallsBuiltinFunctions(fn(cat,hello,world), hello world)` — gets `""`
- `Fn_CallsBuiltinFunctions(fn(mid,hello,1,3), ell)` — gets `""`
- `Fn_UnknownFunctionReturnsError(fn(notafunction), #-1)` — gets `""`
- `Fn_CaseInsensitive(fn(ADD,1,2), 3)` — gets `""`
- `Fn_NestedFnCalls` — `fn(add,fn(mul,2,3),4)` → `10`
- `Fn_BuiltinBypassesOverride` — tests override bypass

**Root cause:** `fn()` at UtilityFunctions.cs:644-658 calls `AttributeService.EvaluateAttributeFunctionAsync` — this looks up `FN_<name>` as an **attribute** on the executor object. But PennMUSH's `fn()` calls the **built-in** function directly, bypassing any `@function` override.

The current implementation returns "" because:
1. In tests, there's no database object with FN_ADD attributes
2. Even with objects, the semantics are wrong — fn() should call add() the built-in, not look up FN_ADD

**Fix:**

Rewrite `fn()` to look up the function name in the built-in function registry and call it directly:

```csharp
[SharpFunction(Name = "fn", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
public static async ValueTask<CallState> Fn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
{
    // Get the function name from first arg, evaluate it
    var functionNameState = parser.CurrentState.Arguments["0"];
    var functionName = (await functionNameState.DeferredMessage!())?.ToString()
                       ?? functionNameState.Message?.ToString() ?? "";

    // Look up the built-in function (case-insensitive)
    var builtinFunction = FunctionRegistry.LookupBuiltin(functionName);
    if (builtinFunction == null)
    {
        return new CallState($"#-1 FUNCTION ({functionName.ToUpperInvariant()}) NOT FOUND");
    }

    // Evaluate remaining args and call the built-in
    var args = new Dictionary<string, CallState>();
    for (int i = 1; i < parser.CurrentState.ArgumentsOrdered.Count; i++)
    {
        var argState = parser.CurrentState.ArgumentsOrdered[i].Value;
        // Evaluate deferred args (since fn() is NoParse, args are deferred)
        var evaluated = argState.DeferredMessage != null
            ? new CallState(await argState.DeferredMessage())
            : argState;
        args[(i - 1).ToString()] = evaluated;
    }

    // Call the built-in function directly
    return await builtinFunction.Invoke(parser, args);
}
```

The exact API for looking up and calling built-in functions needs to be determined by inspecting the function registry. The key is:
1. Look up by name, case-insensitive, in the built-in registry
2. Evaluate the remaining args (they're deferred because fn() is NoParse)
3. Call the function directly

**ANTLR4 principle applied:** This is purely a runtime/function-implementation concern. The grammar correctly parses `fn(add,1,2)` as a function call with 3 comma-separated args. The fix is in the fn() implementation, not the parser.

**Files to change:**
- `SharpMUSH.Implementation/Functions/UtilityFunctions.cs` — rewrite Fn() method
- May need to expose a built-in function lookup API on whatever registry holds SharpFunction definitions

### Group E: Q-register context in lit() (2 tests)

**Tests:**
- `QRegister_SetqInsideLitPreservesContext` — `[setq(0,test)][lit(%q0)]` → `%q0`
- `QRegister_MultipleRegistersInLit` — `[setq(0,A)][setq(1,B)][lit(%q0%q1)]` → `%q0%q1`

**Root cause:** These are a subset of the Literal flag issue (Group A). Once `FunctionFlags.Literal` is properly handled in the visitor to pass through raw unevaluated text, `lit(%q0)` will correctly return the literal string `%q0` instead of evaluating it to the Q-register value.

**Fix:** Covered by Group A fix. No additional work needed — if Literal grabs raw source text, `%q0` is just the characters `%`, `q`, `0`.

## Execution Order

Fix groups in this order based on dependencies:

1. **Group A (Literal flag)** — foundational, also fixes Group E
2. **Group B (Space compression)** — independent
3. **Group C (%? two numbers)** — independent
4. **Group D (fn() built-in dispatch)** — independent, may need function registry exploration

Groups B, C, D are independent and can be done in any order after A.

## Step-by-Step Plan

### Step 1: Fix FunctionFlags.Literal in visitor (Group A + E — 6 tests)

1. In `SharpMUSHParserVisitor.cs`, add a Literal branch before the NoParse branches (around line 519):
   - Check `attribute.Flags.HasFlag(FunctionFlags.Literal)`
   - Extract raw source text between function parens using ANTLR4 token positions
   - Return as single unevaluated argument
2. In `StringFunctions.cs`, change lit() `MinArgs = 1` to `MinArgs = 0`
3. In lit() implementation, handle the case where ArgumentsOrdered is empty (return empty string)
4. Run tests: expect 6 tests to pass (Lit_EmptyArgument, Lit_BackslashPassesThrough, Lit_CommasAreLiteral, QRegister_LeakThroughLit, QRegister_SetqInsideLitPreservesContext, QRegister_MultipleRegistersInLit)

### Step 2: Fix space compression in function args (Group B — 1 test)

1. Investigate if `MModule` already has a space compression method
2. If not, add one (regex `\s{2,}` → ` ` or span-based)
3. In the visitor's function arg evaluation (line 521-529), apply space compression to evaluated arg results
4. Ensure compression only applies to evaluated args (not NoParse/Literal args)
5. Run tests: expect SpaceCompression_CatMultipleSpacesInArgs to pass

### Step 3: Fix %? to return two numbers (Group C — 5 tests)

1. Find or add `InvocationCount` property on parser state
2. Find or add `RecursionCount` property (check if AttributeService already tracks this)
3. Increment `InvocationCount` in VisitFunction when a built-in function is called successfully
4. Change Substitutions.cs:81 to return `$"{invocations} {recursions}"`
5. Run tests: expect all 5 %? tests to pass

### Step 4: Fix fn() to call built-in functions (Group D — 8 tests)

1. Find the function registry (likely where `SharpFunctionAttribute` lookup happens)
2. Expose a method to look up a built-in function by name (case-insensitive)
3. Rewrite fn() to:
   a. Evaluate first arg to get function name
   b. Look up in built-in registry
   c. Return #-1 error if not found
   d. Evaluate remaining deferred args
   e. Call built-in with evaluated args
4. Run tests: expect all 8 fn() tests to pass

### Step 5: Full regression

1. Run entire PennMUSHParserGapTests suite — expect 43/43 pass
2. Run full test suite — ensure no regressions
3. Commit on `pennmush-compatibility` branch

## Files Likely to Change

| File | Changes |
|------|---------|
| `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs` | Literal branch, space compression, invocation counting |
| `SharpMUSH.Implementation/Functions/StringFunctions.cs` | lit() MinArgs=0, handle empty args |
| `SharpMUSH.Implementation/Functions/UtilityFunctions.cs` | Rewrite fn() |
| `SharpMUSH.Implementation/Substitutions/Substitutions.cs` | %? returns "N M" |
| Parser state interface/class (TBD) | Add InvocationCount, RecursionCount |
| MModule/MString (TBD) | Add compressSpaces if needed |

## Grammar Changes

**None.** All fixes are in the visitor and function implementations. The grammar correctly tokenizes all inputs. This follows the ANTLR4 principle: grammar = structure, visitor = semantics.

The only grammar-adjacent observation: `lit(\)` causes a parse error because `\` triggers ESCAPE mode expecting an `ANY` token, but `)` is consumed as the escape target, so the CPAREN is lost. This is actually CORRECT lexer behavior — `\)` is an escaped paren. The fix for `Lit_BackslashPassesThrough` is in the Literal handling: when Literal flag is set, grab raw source text (which includes the `\` character) rather than trying to parse/evaluate it. The test `lit(\)` should see raw text `\` since the `)` after the backslash is consumed by the escape, and the real closing paren follows. Need to verify this parse tree carefully during implementation.

## Risks and Open Questions

1. **lit(\) parse error** — The lexer sees `lit(` `\` `)` where `\)` is ESCAPE+ANY, leaving no CPAREN. The grammar may need `lit(\)` to be `lit(\\)` to work. Need to test against PennMUSH oracle: does `think lit(\)` work in PennMUSH? If PennMUSH treats `\` specially inside lit(), we may need special handling. **Test with oracle first.**

2. **fn() function registry access** — Need to determine how SharpFunctionAttribute-decorated methods are discovered at runtime. The current fn() uses AttributeService for object attributes; the new fn() needs the built-in function table. May need to inject a function registry service.

3. **Space compression scope** — Need to verify: does PennMUSH compress spaces only in function args, or also in top-level text? `think hello    world` — does PennMUSH compress that? If yes, compression may need to be broader. **Test with oracle.**

4. **InvocationCount threading** — If the parser is reused across evaluations, the counter needs proper scoping (reset per-expression). Check how parser state lifecycle works.

5. **MString immutability** — Space compression creates a new string. Need to ensure MModule's string type supports this efficiently.

## Validation

After all fixes:
```bash
# Run gap tests only
DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project SharpMUSH.Tests -- \
  --treenode-filter "/*/*/PennMUSHParserGapTests/*"

# Run full suite for regression
DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project SharpMUSH.Tests
```

Expected: 43/43 gap tests pass, no regressions in full suite.
