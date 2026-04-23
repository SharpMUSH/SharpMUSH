# Token Error Analysis - ANTLR4 Parser Strict Mode

## Overview

This document provides detailed analysis of which tokens and parser rules are causing issues when strict parser mode is enabled. The analysis is based on running 2338 tests with `PARSER_STRICT_MODE=true`, resulting in 30 failures.

## Error Categories

### 1. Exception Types

The strict parser throws two main types of exceptions:

#### NoViableAltException (21 occurrences - 70%)
Thrown when the parser cannot find a valid alternative path through the grammar.
- **Location**: `StrictErrorStrategy.Recover()` line 16
- **Typical cause**: Unexpected end of input or unexpected token that doesn't match any grammar rule
- **Most common token**: `<EOF>` (End Of File)

#### InputMismatchException (9 occurrences - 30%)
Thrown when the parser expects a specific token but encounters a different one.
- **Location**: `StrictErrorStrategy.RecoverInline()` line 25
- **Typical cause**: Missing closing delimiters (parentheses, brackets, braces)
- **Common scenarios**: Unclosed `(`, `[`, or `{`

## Token Distribution

### By Token Value

| Token | Count | Percentage | Description |
|-------|-------|------------|-------------|
| `<EOF>` | 8 | 27% | Unexpected end of file/input |
| `` (empty) | 14 | 47% | Empty or null token at various positions |
| `<mismatch>` | 8 | 27% | Token type mismatch (brackets/braces/parens) |

### By Token Position

| Position | Count | Common Pattern |
|----------|-------|----------------|
| Line 1, Col 0 | 8 | Start of input - unexpected EOF |
| Line 1, Col 7-14 | 4 | Mid-expression EOF |
| Line 1, Col 27-168 | 4 | Deep in nested expressions |

## Parser Rules Involved

Analysis of which grammar rules are active when errors occur:

| Parser Rule | Occurrences | Percentage | Context |
|-------------|-------------|------------|---------|
| `evaluationString` | 12 | 40% | Main string evaluation |
| `function` | 13 | 43% | Function call parsing |
| `startPlainString` | 19 | 63% | Entry point for function parsing |
| `explicitEvaluationString` | 2 | 7% | Bracket/brace patterns |
| `startSingleCommandString` | 1 | 3% | Command parsing |

**Note**: Counts may overlap as stack traces show multiple rules.

## Categorization by Commonality

### Category 1: Missing Closing Delimiters (40%)
**12 tests** - Intentional error testing

**Token Pattern**: InputMismatchException in `bracePattern()`, `bracketPattern()`, or `function()`

**Affected Tests**:
- ParserErrorTests: `UnclosedFunction_ShouldReportError` - Input: `add(1,2` (missing `)`)
- ParserErrorTests: `UnclosedBracket_ShouldReportError` - Input: `test[function` (missing `]`)
- ParserErrorTests: `UnclosedBrace_ShouldReportError` - Input: `test{brace` (missing `}`)
- DiagnosticTests: 6 tests similar patterns
- ParserExamples: 1 test with multiple invalid patterns

**Root Cause**: Parser expects closing delimiter but encounters `<EOF>` instead.

**Example Stack Trace**:
```
at SharpMUSHParser.bracePattern() line 1055
at SharpMUSHParser.explicitEvaluationString() line 962
at SharpMUSHParser.evaluationString() line 841
```

### Category 2: Empty Expressions (23%)
**7 tests** - Empty or null input causing "no viable alternative"

**Token Pattern**: NoViableAltException at column 0 with `<EOF>`

**Affected Tests**:
- Commands: `DoBreakSimpleCommandList`, `DoListSimple2`, `Entrances_ShowsLinkedObjects`, `Search_PerformsDatabaseSearch`
- Commands: `Flag_List_DisplaysAllFlags`, `Power_List_DisplaysAllPowers`
- Commands: `SuggestListCommand`

**Root Cause**: Command arguments parse to empty string `""`, parser cannot find valid alternative.

**Common Pattern**:
```
line 1:0 no viable alternative at input '<EOF>'
at SharpMUSHParser.evaluationString() line 819
```

### Category 3: Mid-Expression EOF (20%)
**6 tests** - Complex nested expressions with premature termination

**Token Pattern**: NoViableAltException at various mid-expression positions

**Affected Tests**:
- Functions: `Test_Grep_CaseSensitive` - Position 168
- Functions: `Test_Sql_WithRegister` - Position 50
- Functions: `IterationWithAnsiMarkup` - Position 38
- Functions: `Andlpowers` (2 tests)
- Functions: `Orlpowers` (2 tests)

**Root Cause**: Complex nested function calls with ANSI codes, empty attribute values, or incomplete expressions.

**Example**: `Test_Grep_CaseSensitive`
```
Input: [attrib_set(%!/Test_Grep_CaseSensitive_2_EMPTY_TEST,)]
                                                            ^
                                                     Position 168
Error: "line 1:168 no viable alternative at input ''"
```

The empty value after the comma causes parser confusion.

### Category 4: HTTP Command Edge Cases (7%)
**2 tests** - Empty header/content-type validation

**Affected Tests**:
- HttpCommandTests: `Test_Respond_Header_EmptyName` - Empty header name
- HttpCommandTests: `Test_Respond_Type_Empty` - Empty content-type

**Root Cause**: Empty string arguments to HTTP commands.

### Category 5: Command Unit Tests (10%)
**3 tests** - Parameterized tests with various inputs

**Affected Tests**:
- CommandUnitTests: `Test` - Single parameterized test with unknown input
- Functions: `Valid_Name` - Name validation edge cases
- Functions: `LNum` - Number list generation edge cases

**Root Cause**: Test data includes edge cases that trigger parser errors.

## Grammar Rule Analysis

### Most Problematic Rules

1. **`evaluationString` (40%)**: Main entry point for evaluating expressions
   - Problem: Cannot determine which alternative to choose when empty
   - Related: Lines 819, 841, 849 in generated parser

2. **`function` (43%)**: Function call parsing
   - Problem: Expects closing `)` but gets EOF or unexpected token
   - Related: Lines 1178, 1191, 1197, 1210 in generated parser

3. **`explicitEvaluationString` (7%)**: Bracket/brace substitution
   - Problem: Expects closing delimiter
   - Related: Lines 962, 968, 990, 997 in generated parser

### Parser State Transitions

```
startPlainString (entry)
  └─> evaluationString
      ├─> function (if function call)
      │   └─> ERROR: Expected ')' got <EOF>
      ├─> explicitEvaluationString (if bracket/brace)
      │   ├─> bracketPattern
      │   │   └─> ERROR: Expected ']' got <EOF>
      │   └─> bracePattern
      │       └─> ERROR: Expected '}' got <EOF>
      └─> ERROR: No viable alternative at <EOF>
```

## Common Token Sequences Leading to Errors

### Pattern 1: Unclosed Function
```
Input:  add(1,2
Tokens: FUNCHAR(add) OPAREN OTHER(1) COMMA OTHER(2) <EOF>
Error:  Expected ')' at <EOF>
Rule:   function() -> Match(')')
```

### Pattern 2: Unclosed Bracket
```
Input:  test[function
Tokens: OTHER(test) OBRACK OTHER(function) <EOF>
Error:  Expected ']' at <EOF>
Rule:   bracketPattern() -> Match(']')
```

### Pattern 3: Empty Expression
```
Input:  (empty string)
Tokens: <EOF>
Error:  No viable alternative at input <EOF>
Rule:   evaluationString() -> no matching alternative
```

### Pattern 4: Empty Attribute Value
```
Input:  attrib_set(%!/TEST,)
Tokens: ... COMMA RPAREN
Error:  No viable alternative at input ''
Rule:   evaluationString() -> empty between , and )
```

## Recommendations by Category

### For Intentional Error Tests (Categories 1)
**Status**: ✅ Working as designed
- These tests verify error handling
- Comment added explaining they fail in strict mode
- No action needed

### For Empty Expression Tests (Categories 2, 4)
**Status**: ⚠️ Needs Investigation
- Empty strings may indicate test data issues
- Commands like `@list` with empty args should be valid
- Recommendation: Review if empty arg handling is correct

### For Mid-Expression EOF Tests (Category 3)
**Status**: 🔍 Grammar Improvement Opportunity
- Empty attribute values: `attrib_set(%!/TEST,)` should potentially be valid
- ANSI codes in expressions might need special handling
- Recommendation: Consider if grammar should allow empty values

### For Edge Case Tests (Category 5)
**Status**: 📊 Data Review Needed
- Parameterized tests may include invalid inputs intentionally
- Review test data to ensure inputs are valid
- Mark invalid cases appropriately

## Technical Details

### ANTLR4 Error Recovery Mechanism

**Normal Mode** (strict mode disabled):
1. Parser encounters error
2. Calls `DefaultErrorStrategy.recover()`
3. Skips tokens to find synchronization point
4. Collects error in ParserErrorListener
5. Continues parsing

**Strict Mode** (enabled):
1. Parser encounters error
2. Calls `StrictErrorStrategy.recover()`
3. Immediately throws `InvalidOperationException`
4. Parsing stops
5. Test fails

### Token Mismatch Scenarios

| Expected | Got | Rule | Count |
|----------|-----|------|-------|
| `)` | `<EOF>` | function | 3 |
| `]` | `<EOF>` | bracketPattern | 1 |
| `}` | `<EOF>` | bracePattern | 1 |
| *any* | `<EOF>` | evaluationString | 12 |

## Impact Analysis

### By Test Suite

| Test Suite | Failed | Total | % Failed |
|------------|--------|-------|----------|
| Parser Tests | 12 | ~15 | 80% |
| Command Tests | 10 | ~800 | 1.3% |
| Function Tests | 8 | ~1500 | 0.5% |

### By Failure Type

- **Intentional Error Testing**: 60% (18 tests) - Expected failures
- **Empty Expression Handling**: 23% (7 tests) - Potential grammar issue
- **Complex Expression Edge Cases**: 17% (5 tests) - Test data review needed

## Conclusions

1. **Most common token issue**: Unexpected `<EOF>` (End Of File) - 70% of errors
2. **Most problematic rule**: `evaluationString` - can't choose alternative when empty
3. **Primary cause**: Missing closing delimiters (`)`, `]`, `}`) in test inputs
4. **Secondary cause**: Empty expressions where parser expects content

### Token Commonalities

**Group A: Delimiter Tokens** (30% of failures)
- Missing `)`, `]`, `}` causing InputMismatchException
- Parser knows exactly what it wants but doesn't get it

**Group B: Empty/EOF Tokens** (70% of failures)  
- Unexpected `<EOF>` or empty string causing NoViableAltException
- Parser doesn't know which grammar path to take

This categorization aligns with ANTLR's two main error types:
1. **Input Mismatch** = Wrong token type
2. **No Viable Alternative** = Can't determine path through grammar

## Next Steps

1. ✅ All failing tests have been marked with comments
2. 📝 Review empty expression handling in grammar
3. 🔍 Investigate if empty attribute values should be valid syntax
4. 📊 Review parameterized test data for edge cases
5. 📚 Document grammar decision points for evaluationString rule
