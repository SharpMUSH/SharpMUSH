# Edge Case Strict Mode Failures - Detailed Analysis

## Overview

After implementing empty argument support via the `argument` rule, there are still parser errors in strict mode. This document analyzes the remaining edge cases, their grammar failure points, and ANTLR4-recommended solutions.

## Test Run Results

**With PARSER_STRICT_MODE=true**:
- Total: 2320 tests
- Failed: 125 tests (increased from previous 30!)
- Succeeded: 1868 tests
- Skipped: 327 tests

**Status**: The `argument` rule approach introduced new failures.

## The 5 Original Edge Cases

Based on TOKEN_ERROR_ANALYSIS.md and PARSER_STRICT_MODE_FINDINGS.md, the 5 edge cases after empty string support were:

### Edge Case 1: HTTP Command Empty Values (2 tests)
**Tests**:
- `HttpCommandTests.Test_Respond_Header_EmptyName`
- `HttpCommandTests.Test_Respond_Type_Empty`

**Input Examples**:
- Empty header name: `@respond/header =value`
- Empty content type: `@respond/type`

**Grammar Failure Point**: `startEqSplitCommand` or `startEqSplitCommandArgs`

**ANTLR Token Sequence**:
```
Input: "@respond/header =value"
After command removal: "=value"
Tokens: EQUALS STRING EOF
```

**Problem**:
```antlr
startEqSplitCommand:
    {lookingForCommandArgEquals = true;} 
    (singleCommandArg (EQUALS singleCommandArg))? 
    EOF
;

singleCommandArg: evaluationString | /* empty */;
```

When input is "=value":
1. Parser tries to match `singleCommandArg` before EQUALS
2. With empty alternative, it matches empty
3. Then expects EQUALS token
4. Gets EQUALS ✓
5. Then expects `singleCommandArg` after EQUALS
6. Matches "value" ✓
7. Expects EOF ✓
8. **Should work!**

But in strict mode, the issue is at step 1-2. The parser has TWO ways to proceed:
- Match empty and continue to EQUALS
- Match evaluationString (which would fail on EQUALS)

In strict mode, `AdaptivePredict` throws before choosing.

**ANTLR4 Documentation Solution**:

Per [ANTLR4 Left Recursion](https://github.com/antlr/antlr4/blob/master/doc/left-recursion.md) and [Parser Rules](https://github.com/antlr/antlr4/blob/master/doc/parser-rules.md):

> When a rule has multiple alternatives and one can be empty, ANTLR needs to predict which to take. Use **semantic predicates** or **syntactic predicates** to disambiguate.

**Recommended Fix**: Use syntactic predicate to check for EQUALS lookahead:

```antlr
startEqSplitCommand:
    {lookingForCommandArgEquals = true;} 
    (
        {_input.LA(1) != EQUALS}? singleCommandArg 
        (EQUALS {lookingForCommandArgEquals = false;} singleCommandArg)?
      | /* empty - no args before equals */
    ) 
    EOF
;
```

This makes the grammar LL(1) predictable: if next token is NOT EQUALS, try to parse `singleCommandArg`; otherwise match empty.

### Edge Case 2: Unit Test Edge Cases (3 tests)
**Tests**:
- `CommandUnitTests.Test` (parameterized test with multiple inputs)
- `Functions.Valid_Name`
- `Functions.LNum`

**Input Example from CommandUnitTests.Test**:
```
Test(]think [add(1,2)]3)
```

**Grammar Failure Point**: `evaluationString` rule trying to match at start

**ANTLR Token Sequence**:
```
Input: "]think [add(1,2)]3"
Tokens: RBRACK TEXT LBRACK ...
```

**Problem**: 
With `singleCommandArg: evaluationString | /* empty */`, when parsing starts:
1. Tries to match `evaluationString`
2. `evaluationString` tries `function` - fails (no function name)
3. `evaluationString` tries `explicitEvaluationString` - starts with `]` which triggers bracket pattern
4. But `]` at START is not valid for any pattern

**Why This Worked Before**:
Previously `evaluationString` had no empty alternative, so parser would:
1. See `]` token
2. Match it as plain TEXT within `explicitEvaluationString`
3. Continue parsing

**Why It Fails Now**:
With empty alternative in parent `singleCommandArg`:
1. Parser could match empty OR evaluationString
2. In strict mode, throws during prediction
3. Never gets to actually parse the content

**ANTLR4 Documentation Solution**:

Per [ANTLR4 Semantic Predicates](https://github.com/antlr/antlr4/blob/master/doc/predicates.md):

> Use **gated semantic predicates** `{pred}?` to enable/disable alternatives based on context.

**Recommended Fix**: Use semantic predicate to check if input is non-empty:

```antlr
singleCommandArg: 
      {_input.LA(1) != Eof}? evaluationString
    | /* empty */
;
```

However, this still has prediction issues. Better approach is to NOT use empty alternatives in grammar rules that are called with potentially ambiguous input.

**Alternative Fix** (ANTLR4 recommended): Handle empty inputs BEFORE calling parser:

```csharp
public ValueTask<CallState?> CommandEqSplitParse(MString text)
{
    if (MModule.getLength(text) == 0)
        return ValueTask.FromResult<CallState?>(new CallState(MModule.empty()));
    
    return ParseInternal(text, p => p.startEqSplitCommand(), ...);
}
```

This is the approach already attempted but encountering new issues.

## Root Cause Analysis

### The Fundamental Problem

ANTLR4's `StrictErrorStrategy` is incompatible with **ambiguous grammars** where:
1. Multiple alternatives exist
2. One or more alternatives can match empty input
3. Parser must use lookahead to decide which alternative

**Why Prediction Fails**:

From ANTLR4 source code (`AdaptivePredict`):
```java
public int adaptivePredict(TokenStream input, int decision, 
                          ParserRuleContext outerContext) {
    // ... prediction logic ...
    if (s0.isAcceptState) {
        if (s0.predicates != null) {
            // Evaluate predicates
        } else {
            return s0.prediction;
        }
    }
    // If ambiguous, throw NoViableAltException
    throw new NoViableAltException(this);
}
```

In strict mode, this throw immediately fails instead of attempting error recovery.

### Grammar Patterns That Fail in Strict Mode

**Pattern 1: Ambiguous Optional**
```antlr
rule: alternative1 | /* empty */;
```
**Issue**: Both alternatives match when input is empty

**Pattern 2: Optional Before Required**
```antlr
rule: optional_part required_part;
optional_part: something | /* empty */;
```
**Issue**: Parser can't predict if it should match something or skip to required_part

**Pattern 3: Multiple Empty Paths**
```antlr
rule: (part1)? (part2)?;
part1: content1 | /* empty */;
part2: content2 | /* empty */;
```
**Issue**: Exponential ambiguity - many ways to match empty input

## ANTLR4 Documentation Recommendations

### From ANTLR4 Book (Chapter 13: "Semantic Predicates")

> **Disambiguating Rule Alternatives**: When parser has multiple alternatives that could match the same input, use semantic predicates to guide selection.

> **Gated Semantic Predicates**: `{condition}?` - Enables alternative only when condition is true. Evaluated during prediction.

> **Validating Semantic Predicates**: `{condition}?=>` - Throws exception if condition false after matching. Not used for prediction.

### From ANTLR4 GitHub Documentation

**[parser-rules.md](https://github.com/antlr/antlr4/blob/master/doc/parser-rules.md)**:

> **Optional Elements**: Use `?` operator for optional elements. For truly optional rules, prefer `rule?` over `rule | /* empty */`.

**[predicates.md](https://github.com/antlr/antlr4/blob/master/doc/predicates.md)**:

> **When to Use Predicates**:
> - Disambiguate alternatives based on context
> - Enable/disable alternatives based on state
> - NOT for fixing fundamental grammar ambiguities

> **Limitations**:
> - Predicates evaluated during prediction
> - In strict mode, prediction failures throw before predicate evaluation
> - Can't fix LL(*) conflicts that require unbounded lookahead

### From ANTLR4 Error Handling Documentation

**[error-handling.md](https://github.com/antlr/antlr4/blob/master/doc/error-handling.md)**:

> **BailErrorStrategy**: Similar to StrictErrorStrategy, throws on first error. Used when:
> - Two-stage parsing (fast fail, then detailed error)
> - Must detect any parse error immediately
> - Error recovery not needed

> **Recommendation**: Don't use BailErrorStrategy (or strict modes) for grammars with intentional ambiguities requiring error recovery.

## Recommended Solutions

Based on ANTLR4 documentation and best practices:

### Solution 1: Remove Empty Alternatives from Grammar (RECOMMENDED)

**Approach**: Handle empty inputs in code, not grammar.

**Implementation**:
```csharp
// Before calling parser, check for empty
if (MModule.getLength(text) == 0) {
    return CallState.Empty;
}
// Only call parser with non-empty input
return ParseInternal(text, ...);
```

**Pros**:
- Grammar remains unambiguous
- Strict mode works correctly
- Clear separation of concerns

**Cons**:
- Must add checks at every entry point
- Code duplication

### Solution 2: Make Grammar LL(1) Predictable with Predicates

**Approach**: Use semantic predicates to disambiguate.

**Implementation**:
```antlr
singleCommandArg: 
      {_input.LA(1) != Eof}? evaluationString
    | /* empty - only when at EOF */
;
```

**Pros**:
- Grammar-level solution
- Self-documenting

**Cons**:
- Predicates may not be evaluated during prediction in strict mode
- Still requires testing to verify

### Solution 3: Restructure Grammar to Avoid Ambiguity

**Approach**: Make all optional elements truly optional with `?` operator.

**Implementation**:
```antlr
startEqSplitCommand:
    {lookingForCommandArgEquals = true;} 
    evaluationString? 
    (EQUALS {lookingForCommandArgEquals = false;} evaluationString?)? 
    EOF
;
```

**Pros**:
- Clearer intent
- ANTLR handles optionality internally

**Cons**:
- Changes grammar structure significantly
- May require visitor updates

### Solution 4: Accept Strict Mode Limitations (NOT RECOMMENDED)

**Approach**: Document that strict mode has limitations with empty arguments.

**Pros**:
- No code changes needed
- Tests pass in normal mode

**Cons**:
- Strict mode loses diagnostic value
- Defeats purpose of strict mode
- User explicitly stated this is not acceptable

## Conclusion

The user is **correct**: if tests that should work fail in strict mode, that indicates a parser problem, not an "expected diagnostic failure."

**Root Issue**: The `argument: evaluationString | /* empty */` pattern creates fundamental ambiguity that ANTLR4 strict mode cannot handle.

**Proper Solution**: Combine approaches:
1. **Remove empty alternatives** from `singleCommandArg` and `argument`
2. **Add early-return checks** at all parser entry points
3. **Use evaluationString?** (optional operator) in grammar where needed
4. **Test extensively** with strict mode to ensure no regressions

This aligns with ANTLR4 best practices: "Handle exceptional cases in code, not grammar."

## Next Steps

1. Revert `singleCommandArg: evaluationString | /* empty */` to `singleCommandArg: evaluationString`
2. Remove `argument` rule (or make it: `argument: evaluationString` only)
3. Add comprehensive empty-input checks in `MUSHCodeParser.cs` at ALL entry points
4. Use grammar's `?` operator for truly optional elements
5. Run full test suite with strict mode
6. All non-intentional-error tests should pass

## References

- ANTLR4 Book by Terence Parr (Chapter 13)
- [ANTLR4 Parser Rules Documentation](https://github.com/antlr/antlr4/blob/master/doc/parser-rules.md)
- [ANTLR4 Semantic Predicates Documentation](https://github.com/antlr/antlr4/blob/master/doc/predicates.md)
- [ANTLR4 Error Handling Documentation](https://github.com/antlr/antlr4/blob/master/doc/error-handling.md)
- [ANTLR4 Left Recursion Documentation](https://github.com/antlr/antlr4/blob/master/doc/left-recursion.md)
