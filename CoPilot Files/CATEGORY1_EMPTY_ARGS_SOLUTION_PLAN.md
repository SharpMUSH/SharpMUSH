# Category 1: Empty Command Arguments - Solution Plan

## Problem Statement

The current grammar uses `evaluationString?` in multiple places to allow empty arguments:
- Functions: `function: FUNCHAR (evaluationString? (COMMAWS evaluationString?)*)?  CPAREN`
- Commands: Various rules use `evaluationString?`

In strict mode, this causes ambiguity because the parser cannot predict at EOF whether to:
1. Match the empty alternative (from `?`)
2. Try to match evaluationString content

## Current Failures (7 tests)

1. Flag_List_DisplaysAllFlags
2. Power_List_DisplaysAllPowers  
3. SuggestListCommand
4. Entrances_ShowsLinkedObjects
5. Search_PerformsDatabaseSearch
6. DoBreakSimpleCommandList
7. BasicLambdaTest

All fail with: `no viable alternative at input '<EOF>'` in rule `evaluationString`

## User Guidance

"Adding an empty option to evaluationString will cause too much ambiguity and full context lookaheads. We only want to allow the optional for arguments. You can see this for functions, and it works there. You should be able to use a similar approach for commands."

## Solution: Intermediate Rules with Lookahead Predicates

### Approach

Instead of using `evaluationString?` directly, create intermediate rules that:
1. Have an explicit empty alternative  
2. Use lookahead predicates to make the choice predictable
3. Follow the same pattern as existing `argument` rule for functions

### Step 1: Analyze Current Grammar

**Function arguments** (line 86):
```antlr
function: 
    FUNCHAR {++inFunction;} 
    (evaluationString? ({inBraceDepth == 0}? COMMAWS evaluationString?)*)?
    CPAREN {--inFunction;} 
;
```

**Problem**: The `?` operator on `evaluationString` causes ambiguity in strict mode.

### Step 2: Create Intermediate Rules

**For function arguments**:
```antlr
functionArgument:
    evaluationString
    | {InputStream.LA(1) == COMMAWS || InputStream.LA(1) == CPAREN}? /* empty */
;
```

**For command arguments** (different terminators):
```antlr
commandArgument:
    evaluationString
    | {InputStream.LA(1) == COMMAWS || InputStream.LA(1) == SEMICOLON || 
       InputStream.LA(1) == CBRACE || InputStream.LA(1) == Eof}? /* empty */
;
```

**For single command arguments**:
```antlr
singleCommandArgument:
    evaluationString
    | {InputStream.LA(1) == Eof}? /* empty */
;
```

### Step 3: Update Grammar Rules

**Replace in function** (line 86):
```antlr
// OLD
function: 
    FUNCHAR {++inFunction;} 
    (evaluationString? ({inBraceDepth == 0}? COMMAWS evaluationString?)*)?
    CPAREN {--inFunction;} 
;

// NEW
function: 
    FUNCHAR {++inFunction;} 
    (functionArgument ({inBraceDepth == 0}? COMMAWS functionArgument)*)?
    CPAREN {--inFunction;} 
;
```

**Replace in startPlainSingleCommandArg** (line 46):
```antlr
// OLD
startPlainSingleCommandArg: evaluationString? EOF;

// NEW  
startPlainSingleCommandArg: singleCommandArgument EOF;
```

**Replace in commaCommandArgs** (line 56-58):
```antlr
// OLD
commaCommandArgs:
    {lookingForCommandArgCommas = true;} evaluationString? (
        {inBraceDepth == 0}? COMMAWS evaluationString?
    )* {lookingForCommandArgCommas = false;}
;

// NEW
commaCommandArgs:
    {lookingForCommandArgCommas = true;} commandArgument (
        {inBraceDepth == 0}? COMMAWS commandArgument
    )* {lookingForCommandArgCommas = false;}
;
```

**Replace in startEqSplitCommandArgs** (line 33-35):
```antlr
// OLD
startEqSplitCommandArgs:
    {lookingForCommandArgEquals = true;} evaluationString? (
      EQUALS {lookingForCommandArgEquals = false;} commaCommandArgs
    )? EOF
;

// NEW
startEqSplitCommandArgs:
    {lookingForCommandArgEquals = true;} singleCommandArgument (
      EQUALS {lookingForCommandArgEquals = false;} commaCommandArgs
    )? EOF
;
```

**Replace in startEqSplitCommand** (line 40-42):
```antlr
// OLD
startEqSplitCommand:
    {lookingForCommandArgEquals = true;} evaluationString? (
        EQUALS {lookingForCommandArgEquals = false;} evaluationString?
    )? EOF
;

// NEW
startEqSplitCommand:
    {lookingForCommandArgEquals = true;} singleCommandArgument (
        EQUALS {lookingForCommandArgEquals = false;} singleCommandArgument
    )? EOF
;
```

### Step 4: Update Visitors

When grammar changes from `evaluationString?` to `functionArgument` or `commandArgument`, 
visitors must be updated to extract the nested `evaluationString` context.

**Pattern**:
```csharp
// OLD: Direct access
var args = context.evaluationString();

// NEW: Extract from intermediate rule
var args = context.functionArgument()
    .Select(arg => arg.evaluationString())
    .ToArray();
```

**Files to update**:
- `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs`
  - VisitFunction method
  - VisitStartPlainSingleCommandArg method
  - VisitCommaCommandArgs method
  - VisitStartEqSplitCommandArgs method
  - VisitStartEqSplitCommand method

### Step 5: Testing Strategy

**Unit Tests**:
1. Test empty function arguments: `func()`
2. Test empty command arguments: `@flag/list`
3. Test empty in eq-split: `key=` and `=value`
4. Test normal cases still work: `func(arg1,arg2)`, `@flag/list obj`

**Integration Tests** (the 7 failing tests):
- Run each with PARSER_STRICT_MODE=true
- Verify they now pass

**Full Test Suite**:
- Run without strict mode: all should pass (backward compatibility)
- Run with strict mode: 7 tests should now pass, reducing failures from 10 to 3

## Implementation Checklist

- [ ] Create `functionArgument` rule with lookahead for `,` and `)`
- [ ] Create `commandArgument` rule with lookahead for `,`, `;`, `}`, EOF
- [ ] Create `singleCommandArgument` rule with lookahead for EOF only
- [ ] Update `function` rule to use `functionArgument`
- [ ] Update `commaCommandArgs` to use `commandArgument`
- [ ] Update `startPlainSingleCommandArg` to use `singleCommandArgument`
- [ ] Update `startEqSplitCommandArgs` to use `singleCommandArgument` and `commandArgument`
- [ ] Update `startEqSplitCommand` to use `singleCommandArgument`
- [ ] Build parser project (regenerate from grammar)
- [ ] Update VisitFunction visitor
- [ ] Update VisitCommaCommandArgs visitor
- [ ] Update VisitStartPlainSingleCommandArg visitor
- [ ] Update VisitStartEqSplitCommandArgs visitor
- [ ] Update VisitStartEqSplitCommand visitor
- [ ] Test empty function: `add()`
- [ ] Test empty command: `@flag/list`
- [ ] Test the 7 failing tests with strict mode
- [ ] Run full suite without strict mode (verify backward compatibility)
- [ ] Run full suite with strict mode (verify 7 failures fixed)

## Expected Outcomes

**Before**:
- 10 failures with PARSER_STRICT_MODE=true
- 7 are empty command argument issues

**After**:
- 3 failures with PARSER_STRICT_MODE=true
- Empty command arguments work in strict mode
- All tests pass in normal mode (backward compatible)

## Why This Works

**Lookahead Predicates Make Grammar LL(1)**:

The predicate `{InputStream.LA(1) == COMMAWS || InputStream.LA(1) == CPAREN}?` checks the next token BEFORE trying the empty alternative. This makes the parser decision deterministic:

- If next token is `,` or `)`: Can safely match empty
- Otherwise: Must match evaluationString

This eliminates the ambiguity that causes strict mode to fail.

**Separate Rules for Different Contexts**:

- Functions end with `)`, check for CPAREN
- Commands end with `;`, `}`, or EOF, check for those
- Single arguments only check for EOF

This precision ensures empty is only matched in appropriate contexts.

## Risks and Mitigations

**Risk 1**: Visitor update errors
- **Mitigation**: Test each visitor method individually
- **Fallback**: Keep null-coalescing for safety

**Risk 2**: Breaking existing functionality
- **Mitigation**: Run full test suite without strict mode first
- **Fallback**: Revert grammar changes if tests fail

**Risk 3**: Performance impact
- **Mitigation**: Lookahead is O(1), no performance degradation
- **Verification**: Measure test suite duration before/after

## Success Criteria

1. ✅ All 7 empty command argument tests pass with PARSER_STRICT_MODE=true
2. ✅ Full test suite passes without strict mode (backward compatible)
3. ✅ No performance regression (< 3 minutes for full suite)
4. ✅ Grammar is cleaner and more predictable
5. ✅ Strict mode failures reduced from 10 to 3

## Timeline Estimate

- Grammar changes: 30 minutes
- Parser regeneration: 5 minutes
- Visitor updates: 45 minutes
- Testing: 30 minutes
- **Total**: ~2 hours

## References

- ANTLR4 predicates: https://github.com/antlr/antlr4/blob/master/doc/predicates.md
- Parser rules: https://github.com/antlr/antlr4/blob/master/doc/parser-rules.md
- Previous work: Commits 8620140, cd2a087 (lookahead predicates for arguments)
