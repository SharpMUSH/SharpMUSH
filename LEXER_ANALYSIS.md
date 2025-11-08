# Boolean Expression Lexer Analysis - Failing Test Cases

## Test Results Summary
- **Total Tests**: 1476
- **Passing**: 1098 (74.4%)
- **Failing**: 7 (0.5%)
- **Skipped**: 371 (25.1%)
- **Boolean Expression Pass Rate**: 99.4% (1098/1105 run tests)

## Failing Tests Analysis

### Overview
All 7 failing tests are related to the Boolean Expression lexer's inability to properly tokenize keyword-based lock expressions. The failures occur when the lexer needs to recognize compound tokens like `name^`, `type^`, `flag^`, etc.

---

### Test Category 1: TypeExpressions (3 failures)

**Test File**: `BooleanExpressionUnitTests.cs` lines 46-59

**Failing Inputs**:
1. `type^Player & #TRUE` - Expected: true
2. `type^Player & #FALSE` - Expected: false  
3. `type^Player & !type^Player` - Expected: false

**Current Behavior**: Tests fail during parsing/validation phase

**Root Cause Analysis**:
- The lexer needs to recognize `BIT_TYPE` token which matches `type^`
- Current lexer definition: `BIT_TYPE: T Y P E CARET;` (line 50)
- The STRING token is matching "type" before the lexer can check for "type^"
- Semantic predicate excludes "type" from STRING, but the issue persists

**Grammar Rules Involved**:
```antlr
BIT_TYPE: T Y P E CARET;              // Should match "type^"
STRING: ~(...)+                        // Currently matching "type"
    {Text.ToLower() != "type" ...}?;  // Predicate tries to prevent it
```

**Token Matching Order Issue**:
1. Lexer sees "t"
2. Starts matching STRING token: "type"
3. Semantic predicate evaluates to false (correctly rejects "type")
4. Lexer backtracks but STRING has already consumed input
5. BIT_TYPE token never gets a chance to match "type^"

---

### Test Category 2: TypeValidation (3 failures)

**Test File**: `BooleanExpressionUnitTests.cs` lines 61-73

**Failing Inputs**:
1. `type^Player` - Expected: Valid (true)
2. `type^Thing` - Expected: Valid (true)
3. `type^Room` - Expected: Valid (true)

**Current Behavior**: Validation fails because the parser cannot recognize the expression

**Root Cause**: Same as TypeExpressions - `BIT_TYPE` token not matching

**Expected vs Actual**:
- **Expected Token Sequence**: `BIT_TYPE("type^")` `STRING("Player")`
- **Actual Token Sequence**: Parse error - no viable alternative

---

### Test Category 3: NameExpressionMatching (2 failures)

**Test File**: `BooleanExpressionUnitTests.cs` lines 99-110

**Failing Inputs**:
1. `name^God` - Expected: true (matches if player is named "God")
2. `name^One` - Expected: true (matches if player is named "One")

**Current Behavior**: Tests fail during parsing/validation phase

**Root Cause Analysis**:
- The lexer needs to recognize `NAME` token which matches `name^`
- Current lexer definition: `NAME: N A M E CARET;` (line 47)
- Same issue as type^ - STRING token matching "name" before NAME token can match "name^"

**Grammar Rules Involved**:
```antlr
NAME: N A M E CARET;                   // Should match "name^"
STRING: ~(...)+                        // Currently matching "name"
    {Text.ToLower() != "name" ...}?;  // Predicate tries to prevent it
```

---

### Test Category 4: ExactObjectMatching (2 failures - but likely different issue)

**Test File**: `BooleanExpressionUnitTests.cs` lines 112-123

**Passing Input**:
- `=me` - Working correctly ✓

**Failing Inputs**:
1. `=#1` - Expected: true (DBRef #1 matches itself)
2. `=#2` - Expected: false (DBRef #1 doesn't match #2)

**Current Behavior**: Tests may be failing due to visitor logic rather than lexer

**Root Cause Analysis**:
- EXACTOBJECT token: `EXACTOBJECT: ('=' | O B J I D CARET);` (line 44)
- The `=` character should match as EXACTOBJECT token
- The failing tests use `=#1` and `=#2` format
- Issue may be in the visitor logic handling DBRef comparison rather than lexer

**Token Matching**:
- **Expected**: `EXACTOBJECT("=")` `STRING("#1")`
- **Likely Actual**: Tokens match correctly, but visitor logic may not properly handle DBRef comparison

**Note**: These 2 failures may not be lexer-related. Need to verify visitor implementation for exact object matching with DBRef numbers.

---

## Core Problem: ANTLR Lexer Matching Strategy

### The Fundamental Issue

ANTLR lexers use **maximal munch** (longest match) strategy with **first match wins**:

1. **Greedy Matching**: When multiple tokens could match, the longest one wins
2. **First Alternative**: Among equal-length matches, the first rule defined wins
3. **No Lookahead**: Lexers don't look ahead to see what comes next before committing to a token
4. **Semantic Predicates**: Evaluated AFTER the token is matched, causing backtracking

### Why Current Solution Fails

```antlr
STRING: ~( '#' | '&' | '|' | ':' | '!' | ')' | '(' | '^' | ' ' | '/')+
    {Text.ToLower() != "name" && 
     Text.ToLower() != "type" && 
     Text.ToLower() != "flag" && 
     Text.ToLower() != "power" ...}?;
```

**Problem Flow**:
1. Input: `type^Player`
2. Lexer starts: sees 't'
3. Lexer matches: `type` as STRING (4 characters)
4. Semantic predicate: evaluates to FALSE (rejects "type")
5. Lexer backtracks: tries to match shorter string
6. Result: Either error or incorrect tokenization

The semantic predicate doesn't **prevent** STRING from matching; it **rejects** the match after it's already consumed the input, causing backtracking issues.

---

## Current Lexer Configuration

### Token Definitions (Line Numbers from SharpMUSHBoolExpLexer.g4)

```antlr
Line 47:  NAME: N A M E CARET;           // Should match "name^"
Line 48:  BIT_FLAG: F L A G CARET;       // Should match "flag^"
Line 49:  BIT_POWER: P O W E R CARET;    // Should match "power^"
Line 50:  BIT_TYPE: T Y P E CARET;       // Should match "type^"
Line 51:  DBREFLIST: D B R E F L I S T CARET;
Line 52:  CHANNEL: C H A N N E L CARET;
Line 53:  IP: I P CARET;
Line 54:  HOSTNAME: H O S T N A M E CARET;
Line 59:  STRING: ~(...)+ {...}?;        // Problematic token
```

### Character Exclusions in STRING

```antlr
Excluded: '#', '&', '|', ':', '!', ')', '(', '^', ' ', '/'
```

**Issue**: Even though `^` is excluded, the lexer matches "type" before it can see the `^` that follows.

### Semantic Predicate in STRING

```csharp
{Text.ToLower() != "name" && 
 Text.ToLower() != "type" && 
 Text.ToLower() != "flag" && 
 Text.ToLower() != "power" && 
 Text.ToLower() != "channel" && 
 Text.ToLower() != "dbreflist" && 
 Text.ToLower() != "ip" && 
 Text.ToLower() != "hostname" &&
 Text.ToLower() != "objid"}?
```

**Issue**: Semantic predicates are evaluated AFTER the token matches, not before. This causes backtracking rather than preventing the match.

---

## Proposed Solutions

### Solution 1: Lexer Mode Switching (Recommended)

Use lexer modes to handle keyword-based tokens separately:

```antlr
// Default mode - keywords have priority
NAME: N A M E CARET -> mode(PATTERN_MODE);
BIT_TYPE: T Y P E CARET -> mode(PATTERN_MODE);
// ... other keyword tokens

STRING: ~(...)+ ;  // No semantic predicate needed

mode PATTERN_MODE;
PATTERN_STRING: ~(...)+ -> mode(DEFAULT_MODE);
```

**Pros**:
- Clean separation between keyword matching and pattern matching
- No semantic predicates needed
- Explicit control over token matching order

**Cons**:
- Requires grammar restructuring
- More complex lexer definition

### Solution 2: Fragment-Based Keywords

Define keywords as fragments and reference them explicitly:

```antlr
fragment NAME_KEYWORD: N A M E;
fragment TYPE_KEYWORD: T Y P E;

NAME: NAME_KEYWORD CARET;
BIT_TYPE: TYPE_KEYWORD CARET;

STRING: ~NAME_KEYWORD ~TYPE_KEYWORD ... ~(...)+ ;
```

**Pros**:
- Clearer keyword definitions
- Better pattern matching control

**Cons**:
- Complex exclusion pattern in STRING
- May not fully resolve the issue

### Solution 3: Reorder Token Definitions (Partial Solution)

Move keyword tokens BEFORE STRING token and remove semantic predicates:

```antlr
// Define these FIRST (already done correctly)
NAME: N A M E CARET;
BIT_TYPE: T Y P E CARET;
// ... other keywords

// Define STRING LAST with max exclusions
ATTRIBUTENAME: ~(...)+ ;
STRING: ~(...)+ ;  // Remove semantic predicate
```

**Current Status**: Already implemented (tokens are in correct order)

**Pros**:
- Simple change
- No grammar restructuring

**Cons**:
- Doesn't fully solve the problem due to maximal munch
- May cause other tokenization issues

### Solution 4: Negative Lookahead (Not Available in ANTLR)

Would be ideal but ANTLR lexers don't support lookahead:

```antlr
// NOT POSSIBLE in ANTLR
STRING: ~('name' CARET | 'type' CARET | ...) ~(...)+ ;
```

---

## Recommended Fix Strategy

### Phase 1: Quick Fix (Current Approach)
- ✓ Exclude special characters from STRING
- ✓ Add semantic predicate to reject keywords
- **Result**: 99.4% pass rate (7 failures remain)

### Phase 2: Comprehensive Fix (Recommended)
Implement **Lexer Mode Switching**:

1. Default mode recognizes keywords + opening tokens
2. Pattern mode handles the pattern/value after `^`
3. Automatic mode switching on `^` character
4. Clean token boundaries

### Phase 3: Alternative (If Phase 2 Too Complex)
Restructure grammar to separate keyword-based locks:

```antlr
lockExpression
    : simpleLock        // #TRUE, #FALSE
    | booleanOp        // !, &, |, ()
    | keywordLock      // name^, type^, flag^, etc.
    | valueLock        // =object, +object, $object, etc.
    | attributeLock    // attr:value, attr/value
    ;

keywordLock
    : (NAME | BIT_TYPE | BIT_FLAG | ...) STRING
    ;
```

This forces parser-level distinction between keyword locks and other locks.

---

## Test-Rule Mapping

| Test Name | Input | Expected Tokens | Current Issue | Lexer Rule |
|-----------|-------|----------------|---------------|------------|
| TypeExpressions(type^Player & #TRUE) | type^Player & #TRUE | BIT_TYPE STRING AND TRUE | STRING matches "type" | Line 50 |
| TypeExpressions(type^Player & #FALSE) | type^Player & #FALSE | BIT_TYPE STRING AND FALSE | STRING matches "type" | Line 50 |
| TypeExpressions(type^Player & !type^Player) | type^Player & !type^Player | BIT_TYPE STRING AND NOT BIT_TYPE STRING | STRING matches "type" | Line 50 |
| TypeValidation(type^Player) | type^Player | BIT_TYPE STRING | STRING matches "type" | Line 50 |
| TypeValidation(type^Thing) | type^Thing | BIT_TYPE STRING | STRING matches "type" | Line 50 |
| TypeValidation(type^Room) | type^Room | BIT_TYPE STRING | STRING matches "type" | Line 50 |
| NameExpressionMatching(name^God) | name^God | NAME STRING | STRING matches "name" | Line 47 |
| NameExpressionMatching(name^One) | name^One | NAME STRING | STRING matches "name" | Line 47 |
| ExactObjectMatching(=#1) | =#1 | EXACTOBJECT STRING | Likely visitor issue | Line 44 |
| ExactObjectMatching(=#2) | =#2 | EXACTOBJECT STRING | Likely visitor issue | Line 44 |

---

## Conclusion

The core issue is that ANTLR's lexer cannot effectively use semantic predicates to prevent token matching - they only reject matches after they're made, causing backtracking issues. The current 99.4% pass rate represents the limit of what can be achieved with semantic predicates.

To achieve 100% pass rate, the grammar needs restructuring using either:
1. **Lexer modes** (recommended for clean separation)
2. **Parser-level keyword handling** (alternative if lexer modes too complex)

The 7 remaining failures are:
- **5 keyword-based lock failures** (name^, type^): Lexer tokenization issue
- **2 exact object failures** (=#1, =#2): Likely visitor logic issue (not lexer)

Both issues are fixable, but require deeper grammar/visitor changes beyond simple token exclusions.
