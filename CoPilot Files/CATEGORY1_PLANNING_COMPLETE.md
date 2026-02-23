# Category 1 Empty Command Arguments - Planning Complete

## Executive Summary

Investigated the 7 failing tests for empty command arguments in strict mode. Identified root cause and prepared solution approach pending user guidance.

## Investigation Results

### Architecture Understanding ✓

**Two-Layer Parsing**:
1. **Layer 1**: `command` rule - Matches full command WITHOUT argument parsing
2. **Layer 2**: commandarg rules - Actually do the argument splitting

This explains why the issue is in commandarg rules, not the command rule itself.

### The 7 Failing Tests ✓

All are commands called with **empty or no arguments**:

| Test | Command | Behavior Flags | Parsing Rule Used |
|------|---------|----------------|-------------------|
| Flag_List_DisplaysAllFlags | `@FLAG/LIST` | CB.EqSplit \| CB.RSArgs | startEqSplitCommandArgs |
| Power_List_DisplaysAllPowers | `@POWER/LIST` | CB.EqSplit \| CB.RSArgs | startEqSplitCommandArgs |
| SuggestListCommand | `suggest` | ? | ? |
| Entrances_ShowsLinkedObjects | `@entrances` | ? | ? |
| Search_PerformsDatabaseSearch | `@search` | ? | ? |
| DoBreakSimpleCommandList | `@break` | ? | ? |
| BasicLambdaTest | Lambda | N/A | Function parsing? |

### Grammar Analysis ✓

**Functions work** (user confirmed):
```antlr
function: 
    FUNCHAR {++inFunction;} 
    (evaluationString? ({inBraceDepth == 0}? COMMAWS evaluationString?)*)?
    CPAREN {--inFunction;} 
;
```

**Commands fail** with these rules:
```antlr
startEqSplitCommandArgs:
    {lookingForCommandArgEquals = true;} evaluationString? (
      EQUALS {lookingForCommandArgEquals = false;} commaCommandArgs
    )? EOF
;

startEqSplitCommand:
    {lookingForCommandArgEquals = true;} evaluationString? (
        EQUALS {lookingForCommandArgEquals = false;} evaluationString?
    )? EOF
;

startPlainSingleCommandArg: evaluationString? EOF;

commaCommandArgs:
    {lookingForCommandArgCommas = true;} evaluationString? (
        {inBraceDepth == 0}? COMMAWS evaluationString?
    )* {lookingForCommandArgCommas = false;}
;
```

### Root Cause ✓

The `evaluationString?` pattern works for ANTLR's optional matching (`?` operator), but:
- When input is completely empty (EOF), ANTLR still invokes `evaluationString`
- `evaluationString` rule has NO empty alternative
- In strict mode, this throws `NoViableAltException` during prediction
- Normal mode uses backtracking and error recovery (works fine)

### Key Insight from User

**"Function empty args already work without trouble"**

This suggests functions handle `evaluationString?` correctly, but commands don't. The difference must be in the CONTEXT or SURROUNDING RULES.

## Open Questions for User

### Q1: Why Functions Work But Commands Don't?

Both use `evaluationString?`, but functions work and commands fail. Possible reasons:
- Functions have surrounding context (FUNCHAR...CPAREN) that helps prediction?
- Functions are parsed differently than commandarg rules?
- Something else?

### Q2: Which Commandarg Rules Need Fixing?

Do we need to fix:
- **ALL** commandarg rules?
- Only `startEqSplitCommand` and `startEqSplitCommandArgs`?
- Just the ones used by the failing 7 tests?
- Only rules that use `evaluationString?` at TOP LEVEL?

### Q3: Appropriate Lookahead Tokens?

For command arguments, which tokens should the lookahead predicate check?
- `Eof` - End of input ✓
- `SEMICOLON` - Next command ✓
- `CBRACE` - End of brace pattern ✓
- `COMMAWS` - Comma separator ✓
- `EQUALS` - Equals sign ✓
- Others?

Do different rules need different tokens?

### Q4: One Rule or Multiple?

**Option A - Single Rule**:
```antlr
plainCommandArg:
    evaluationString
    | {InputStream.LA(1) == Eof || InputStream.LA(1) == SEMICOLON || 
       InputStream.LA(1) == CBRACE || InputStream.LA(1) == COMMAWS || 
       InputStream.LA(1) == EQUALS}? /* empty */
;
```

**Option B - Multiple Rules**:
- `eqSplitArg` - For startEqSplitCommand (checks Eof, EQUALS)
- `commaArg` - For commaCommandArgs (checks Eof, COMMAWS, CBRACE, SEMICOLON)
- `singleArg` - For startPlainSingleCommandArg (checks Eof)

Which approach is preferred?

## Proposed Solution (Draft)

**Approach**: Create intermediate rule(s) with lookahead predicates (similar to function's `argument` rule pattern).

**Step 1**: Add intermediate rule(s) to grammar

**Step 2**: Replace `evaluationString?` with new rule(s) in commandarg rules

**Step 3**: Update visitors to extract `evaluationString` from intermediate rule context

**Step 4**: Test with failing tests

**Step 5**: Run full test suite

## Implementation Status

⏸️ **PAUSED - Awaiting User Guidance**

Need clarification on Q1-Q4 before proceeding with implementation.

## Files for Reference

- Grammar: `SharpMUSH.Parser.Generated/SharpMUSHParser.g4`
- Parser: `SharpMUSH.Implementation/MUSHCodeParser.cs`
- Commands: `SharpMUSH.Implementation/Commands/WizardCommands.cs`
- Visitor: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs`
- Test results: `CoPilot Files/STRICT_MODE_DETAILED_FAILURES.txt`

## Next Steps

1. Receive user guidance on Q1-Q4
2. Refine solution approach based on guidance
3. Implement grammar changes
4. Update visitors
5. Test and validate
6. Document changes

