# Empty String Argument Support - ANTLR4 Grammar Analysis

## Problem Statement

Analysis shows that **47% of parser errors** in strict mode are caused by empty string arguments. The current grammar does not support empty expressions, causing:
- NoViableAltException when `evaluationString` rule encounters EOF
- Empty function arguments like `attrib_set(%!/TEST,)` fail
- Empty command arguments fail

## Current Grammar Structure

### evaluationString Rule (Lines 64-67)
```antlr
evaluationString:
      function explicitEvaluationString?
    | explicitEvaluationString
;
```

**Problem**: This rule requires at least one alternative:
- Either a `function` (optionally followed by more content)
- Or an `explicitEvaluationString`

There is **no empty alternative**, so an empty string causes "no viable alternative at input EOF".

### explicitEvaluationString Rule (Lines 69-77)
```antlr
explicitEvaluationString:
    (bracePattern|bracketPattern|beginGenericText|PERCENT validSubstitution) 
    (
        bracePattern
      | bracketPattern
      | PERCENT validSubstitution
      | genericText
    )*
;
```

**Problem**: The first part is **required** (not optional), so this rule also cannot match empty input.

### function Rule (Lines 87-91)
```antlr
function: 
    FUNCHAR {++inFunction;} 
    (evaluationString ({inBraceDepth == 0}? COMMAWS evaluationString)*)?
    CPAREN {--inFunction;} 
;
```

**Current behavior**: The arguments are optional (`?`), but each individual `evaluationString` within the arguments is NOT optional. This means:
- `func()` - ✅ Works (no arguments)
- `func(arg1)` - ✅ Works (one argument)
- `func(arg1,arg2)` - ✅ Works (two arguments)
- `func(,arg2)` - ❌ Fails (empty first argument)
- `func(arg1,)` - ❌ Fails (empty second argument)

## Use Cases for Empty Strings

### 1. Empty Function Arguments
**Example**: `attrib_set(%!/TEST,)`
```
Function: attrib_set
Arguments: 
  1. %!/TEST (object reference)
  2. (empty string) ← This should set attribute to empty value
```

**Real-world use**: Setting an attribute to an empty value is a valid MUSH operation for clearing data.

### 2. Empty Command Arguments
**Example**: Commands like `@list` with no specific filter
```
Command: @list
Arguments: (empty) ← Should list everything
```

**Real-world use**: Many commands accept empty arguments to mean "use defaults" or "show all".

### 3. Empty Bracket Substitutions
**Example**: `test[func()]result`
```
Before bracket: test
Bracket content: func() ← Returns empty string
After bracket: result
Result: testresult
```

**Real-world use**: Functions that return empty strings should not cause parse errors.

## Proposed Solution

### Option 1: Make evaluationString Optional (Recommended)

**Change**: Add an empty alternative to `evaluationString`

```antlr
evaluationString:
      function explicitEvaluationString?
    | explicitEvaluationString
    | /* empty - allow zero-length expressions */
;
```

**Pros**:
- ✅ Simplest change
- ✅ Backward compatible (adds capability without breaking existing)
- ✅ Handles all three use cases
- ✅ Matches MUSH semantics where empty strings are valid

**Cons**:
- ⚠️ May allow unintended empty expressions in some contexts
- ⚠️ Requires careful testing

**Impact Analysis**:
- Function arguments: `func(arg1,)` now parses successfully
- Command arguments: Empty args now parse successfully
- Bracket substitutions: `test[]result` now parses successfully
- Single command: Empty input "" now parses (may need semantic validation)

### Option 2: Make explicitEvaluationString Optional

**Change**: Make the first part of `explicitEvaluationString` optional

```antlr
explicitEvaluationString:
    (bracePattern|bracketPattern|beginGenericText|PERCENT validSubstitution)? 
    (
        bracePattern
      | bracketPattern
      | PERCENT validSubstitution
      | genericText
    )*
;
```

**Pros**:
- ✅ More targeted - only affects explicit evaluation contexts
- ✅ Backward compatible

**Cons**:
- ❌ Doesn't help with function arguments (still fails)
- ❌ Doesn't help with top-level empty expressions
- ⚠️ May create ambiguity (empty rule can match anything)

**Impact Analysis**:
- Limited - doesn't solve the main use cases

### Option 3: Add Explicit Empty Alternative

**Change**: Create a specific empty token/rule

```antlr
evaluationString:
      function explicitEvaluationString?
    | explicitEvaluationString
    | emptyExpression
;

emptyExpression:
    /* empty but explicitly matched */
;
```

**Pros**:
- ✅ More explicit intent
- ✅ Can add semantic actions specifically for empty case
- ✅ Self-documenting

**Cons**:
- ⚠️ More verbose
- ⚠️ Functionally identical to Option 1

**Impact Analysis**:
- Same as Option 1, but with clearer intent

### Option 4: Context-Specific Empty Rules

**Change**: Add empty alternatives only where needed

```antlr
// For function arguments specifically
functionArgument:
      evaluationString
    | /* empty argument */
;

function: 
    FUNCHAR {++inFunction;} 
    (functionArgument ({inBraceDepth == 0}? COMMAWS functionArgument)*)?
    CPAREN {--inFunction;} 
;

// For command arguments specifically  
singleCommandArg: 
      evaluationString
    | /* empty argument */
;
```

**Pros**:
- ✅ Most controlled approach
- ✅ Explicit about where empty is allowed
- ✅ Easier to reason about

**Cons**:
- ❌ More complex grammar changes
- ❌ More code duplication
- ⚠️ Doesn't handle all use cases (bracket substitutions still fail)

**Impact Analysis**:
- Targeted - only affects specific contexts
- Still leaves some edge cases unhandled

## Recommendation: Option 1 with Safeguards

### Proposed Grammar Change

```antlr
evaluationString:
      function explicitEvaluationString?
    | explicitEvaluationString
    | /* empty - allow zero-length expressions */
;
```

### Implementation Strategy

1. **Make the grammar change** - Add empty alternative to `evaluationString`

2. **Add semantic validation** - In the visitor, validate that empty strings are appropriate in context:
   ```csharp
   public override CallState? VisitEvaluationString(EvaluationStringContext context)
   {
       // Check if this is an empty expression
       if (context.ChildCount == 0)
       {
           // Return empty CallState with empty message
           return CallState.Empty;
       }
       
       // Normal processing
       return base.VisitEvaluationString(context);
   }
   ```

3. **Update function argument handling** - Ensure functions handle empty arguments correctly:
   ```csharp
   public override CallState? VisitFunction(FunctionContext context)
   {
       var args = new List<string>();
       foreach (var argContext in context.evaluationString())
       {
           var result = Visit(argContext);
           // Empty argument becomes empty string
           args.Add(result?.Message?.ToString() ?? "");
       }
       
       // Pass to function implementation
       return ExecuteFunction(functionName, args);
   }
   ```

4. **Test thoroughly** - Verify:
   - ✅ Empty function arguments work
   - ✅ Empty command arguments work
   - ✅ Empty bracket substitutions work
   - ✅ Existing tests still pass
   - ✅ No unintended side effects

### Safety Considerations

**Potential Issues**:
1. **Ambiguous parses**: Empty alternative could match too eagerly
   - **Mitigation**: ANTLR will try other alternatives first (longest match)
   - **Test**: Ensure `func(arg1,arg2)` still parses as 2 args, not 3

2. **Unintended empty matches**: Empty strings in unexpected contexts
   - **Mitigation**: Add semantic validation in visitor
   - **Test**: Verify error messages still work correctly

3. **Performance**: More backtracking due to empty alternative
   - **Mitigation**: Empty alternative is last, so tried last
   - **Test**: Run performance benchmarks

### Testing Plan

#### Unit Tests to Add

1. **Empty function arguments**:
   ```csharp
   [Test]
   public async Task Function_EmptyFirstArgument()
   {
       var result = await Parser.FunctionParse(MModule.single("func(,arg2)"));
       // Should parse successfully with first arg empty
   }
   
   [Test]
   public async Task Function_EmptyMiddleArgument()
   {
       var result = await Parser.FunctionParse(MModule.single("func(arg1,,arg3)"));
       // Should parse successfully with middle arg empty
   }
   
   [Test]
   public async Task Function_EmptyLastArgument()
   {
       var result = await Parser.FunctionParse(MModule.single("func(arg1,)"));
       // Should parse successfully with last arg empty
   }
   ```

2. **Empty command arguments**:
   ```csharp
   [Test]
   public async Task Command_EmptyArgument()
   {
       var result = await Parser.CommandParse(MModule.single("@list"));
       // Should parse successfully with no args
   }
   ```

3. **Empty bracket substitutions**:
   ```csharp
   [Test]
   public async Task Bracket_EmptyContent()
   {
       var result = await Parser.FunctionParse(MModule.single("test[]result"));
       // Should parse successfully
   }
   ```

4. **Regression tests**:
   - Run all 2308 passing tests to ensure no breaks
   - Run the 30 strict mode failures to see how many now pass

### Expected Outcomes

After implementing this change:

1. **Category 2 failures (Empty Expressions - 23%)** should pass:
   - `DoBreakSimpleCommandList`
   - `Flag_List_DisplaysAllFlags`
   - `Power_List_DisplaysAllPowers`
   - etc.

2. **Category 3 failures (Mid-Expression EOF - 20%)** should pass:
   - `Test_Grep_CaseSensitive` 
   - `Test_Sql_WithRegister`
   - `IterationWithAnsiMarkup`

3. **Total expected improvements**: ~13 tests (43% of strict mode failures)

4. **Tests that will still fail**:
   - Category 1 (40%) - Intentional error tests (unclosed delimiters)
   - These are expected to fail in strict mode

## Alternative: PennMUSH Compatibility Check

Before implementing, verify PennMUSH behavior:

1. **Test in PennMUSH**:
   ```
   > think attrib_set(me/TEST,)
   > think get(me/TEST)
   ```
   
2. **Expected**: Empty string should be valid and clear the attribute

3. **If not compatible**: May need to handle empty strings as errors semantically

## Implementation Checklist

- [ ] Review PennMUSH behavior for empty arguments
- [ ] Make grammar change (add empty alternative)
- [ ] Regenerate parser from grammar
- [ ] Update visitor to handle empty expressions
- [ ] Add unit tests for empty arguments
- [ ] Run all existing tests
- [ ] Run strict mode tests
- [ ] Document behavior in grammar comments
- [ ] Update user documentation if needed

## Conclusion

**Recommendation**: Implement Option 1 (Make evaluationString optional) with proper safeguards.

This approach:
- ✅ Solves 43% of strict mode failures
- ✅ Aligns with MUSH semantics (empty strings are valid)
- ✅ Minimal grammar changes
- ✅ Backward compatible
- ✅ Can be validated semantically if needed

The key is to ensure proper testing and semantic validation to prevent unintended side effects.
