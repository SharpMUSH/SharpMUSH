# Leading Delimiter Analysis - `]` Character Issue

## Problem Statement

Input `]think [add(1,2)]3` fails to parse because the leading `]` character is tokenized as `CBRACK` (closing bracket) instead of plain text, and the parser doesn't allow `CBRACK` as a valid starting token for `evaluationString`.

**Expected**: `]` should be treated as plain text, output as-is
**Actual**: Parser throws "mismatched input ']' expecting..." error

## Root Cause Analysis

### Lexer Tokenization

From `SharpMUSHLexer.g4` line 14:
```antlr
CBRACK: WS ']';
```

The lexer always tokenizes `]` as a `CBRACK` token (closing bracket), regardless of context.

### Parser Grammar

From `SharpMUSHParser.g4`:

```antlr
evaluationString:
      function explicitEvaluationString?
    | explicitEvaluationString
;

explicitEvaluationString:
    (bracePattern|bracketPattern|beginGenericText|PERCENT validSubstitution) 
    (
        bracePattern
      | bracketPattern
      | PERCENT validSubstitution
      | genericText
    )*
;

beginGenericText:
      { inFunction == 0 }? CPAREN
    | { !inCommandList || inBraceDepth > 0 }? SEMICOLON
    | { (!lookingForCommandArgCommas && inFunction == 0) || inBraceDepth > 0 }? COMMAWS
    | { !lookingForCommandArgEquals }? EQUALS
    | { !lookingForRegisterCaret }? CCARET
    | (escapedText|OPAREN|OTHER|ansi) 
;

bracketPattern:
    OBRACK evaluationString CBRACK
;
```

**Issue**: `CBRACK` is NOT included in `beginGenericText`, so it cannot start an `evaluationString`.

### Why This Matters

1. `evaluationString` must start with either `function` or `explicitEvaluationString`
2. `explicitEvaluationString` must start with one of: `bracePattern`, `bracketPattern`, `beginGenericText`, or `PERCENT validSubstitution`
3. `CBRACK` is NOT in `beginGenericText`
4. Therefore, input starting with `]` cannot be parsed

### Token Flow Example

Input: `]think [add(1,2)]3`

**Lexer Output**:
1. `CBRACK` (for `]`)
2. `OTHER` (for `think `)
3. `OBRACK` (for `[`)
4. `OTHER` (for `add`)
5. `FUNCHAR` (for `add(`)
6. etc...

**Parser Expectation**:
- Tries to parse `evaluationString`
- Looks for `function` or `explicitEvaluationString`
- `explicitEvaluationString` needs to start with `beginGenericText` (or other patterns)
- `beginGenericText` does NOT include `CBRACK`
- **Parser fails immediately**

## Comparison with Other Delimiters

Let's check how other closing delimiters are handled:

| Delimiter | Token | In beginGenericText? | Can Start String? |
|-----------|-------|---------------------|-------------------|
| `)` | CPAREN | ✅ Yes (with predicate) | ✅ Yes |
| `]` | CBRACK | ❌ No | ❌ No |
| `}` | CBRACE | ❌ No | ❌ No |
| `>` | CCARET | ✅ Yes (with predicate) | ✅ Yes |

So we have precedent: `)` and `>` CAN appear as text when predicates allow it.

## Solution Options

### Option 1: Add CBRACK to beginGenericText (Simplest) ✅ RECOMMENDED

Add `CBRACK` to the `beginGenericText` rule with appropriate predicate:

```antlr
beginGenericText:
      { inFunction == 0 }? CPAREN
    | { !inCommandList || inBraceDepth > 0 }? SEMICOLON
    | { (!lookingForCommandArgCommas && inFunction == 0) || inBraceDepth > 0 }? COMMAWS
    | { !lookingForCommandArgEquals }? EQUALS
    | { !lookingForRegisterCaret }? CCARET
    | { inBraceDepth == 0 }? CBRACK          // ← ADD THIS LINE
    | (escapedText|OPAREN|OTHER|ansi) 
;
```

**Predicate**: `{ inBraceDepth == 0 }?`
- Only allow `]` as text when NOT inside braces
- Prevents `]` from closing brackets incorrectly
- Similar pattern to other delimiters

**Pros**:
- Minimal change (one line)
- Consistent with existing pattern (`CPAREN`, `CCARET`)
- Grammar-level fix (robust)
- Works in strict mode

**Cons**:
- None identified

### Option 2: Add CBRACE to beginGenericText

Similarly, add `CBRACE` for completeness:

```antlr
beginGenericText:
      { inFunction == 0 }? CPAREN
    | { !inCommandList || inBraceDepth > 0 }? SEMICOLON
    | { (!lookingForCommandArgCommas && inFunction == 0) || inBraceDepth > 0 }? COMMAWS
    | { !lookingForCommandArgEquals }? EQUALS
    | { !lookingForRegisterCaret }? CCARET
    | { inBraceDepth == 0 }? CBRACK
    | { inBraceDepth == 0 }? CBRACE          // ← Also add this
    | (escapedText|OPAREN|OTHER|ansi) 
;
```

**Pros**:
- Handles `}` at start as well
- Comprehensive solution
- Symmetric treatment of all closing delimiters

**Cons**:
- More than minimum needed (no test case for leading `}`)

### Option 3: Lexer Mode for Context-Sensitive Tokenization

Use lexer modes to tokenize `]` differently based on context:

```antlr
// Default mode
OBRACK: '[' -> pushMode(INSIDE_BRACKET);

// Inside bracket mode
mode INSIDE_BRACKET;
CBRACK_CLOSE: ']' -> popMode;
// ... other tokens
```

**Pros**:
- Most accurate (context-aware)
- No need for parser predicates

**Cons**:
- Complex (many changes)
- Requires tracking bracket depth in lexer
- May affect performance
- Over-engineered for this issue

### Option 4: Pre-process Input to Escape Leading Delimiters

In code, detect and escape leading `]` before parsing:

```csharp
if (text.StartsWith("]"))
{
    text = "\\" + text; // Escape the ]
}
```

**Pros**:
- No grammar changes

**Cons**:
- Hack/workaround
- Doesn't solve root cause
- May miss edge cases
- Not maintainable

## Recommended Solution

**Option 1**: Add `CBRACK` (and optionally `CBRACE`) to `beginGenericText` with `inBraceDepth == 0` predicate.

This is:
- ✅ Minimal (one line change)
- ✅ Consistent with existing patterns
- ✅ Grammar-level (robust)
- ✅ Works with strict mode
- ✅ Easy to understand and maintain

## Implementation Steps

1. **Modify Grammar**:
   - Edit `SharpMUSH.Parser.Generated/SharpMUSHParser.g4`
   - Add to `beginGenericText` rule (around line 145-151)

2. **Rebuild Parser**:
   - Run `dotnet build SharpMUSH.Parser.Generated/SharpMUSH.Parser.Generated.csproj`
   - ANTLR will regenerate parser code

3. **Test**:
   - Run `CommandUnitTests.Test("]think [add(1,2)]3")` without strict mode
   - Run with `PARSER_STRICT_MODE=true`
   - Verify both pass

4. **Regression Test**:
   - Ensure bracket patterns still work: `[add(1,2)]`
   - Ensure nested brackets work: `[name([name(me)])]`
   - Run full test suite

## Expected Results

### Before Fix
- `]think [add(1,2)]3` → **FAILS** (mismatched input ']')
- `[add(1,2)]` → ✅ Works
- `think [add(1,2)]` → ✅ Works

### After Fix
- `]think [add(1,2)]3` → ✅ **PASSES** (] treated as text)
- `[add(1,2)]` → ✅ Still works
- `think [add(1,2)]` → ✅ Still works

## Test Cases to Validate

```
Test Case                     | Expected Output      | Notes
------------------------------|---------------------|----------------------
]think [add(1,2)]3           | ]3                  | Leading ]
}test                        | }test               | Leading } (if added)
think ]test                  | think ]test         | ] in middle
[add(1,]                     | Parse error         | Unclosed bracket (intentional)
test[add(1,2)]               | test3               | Bracket in middle
]                            | ]                   | Just ]
]]think                      | ]]think             | Multiple ]
```

## Risk Assessment

**Low Risk**:
- Predicate ensures `]` only treated as text when `inBraceDepth == 0`
- If inside brackets, `]` still closes bracket (existing behavior)
- Pattern already used for `CPAREN` and `CCARET`
- Single line change, easy to revert

**Testing Required**:
- Bracket patterns (existing tests should cover)
- Nested brackets
- Leading delimiter edge cases
- Strict mode compatibility

## Conclusion

The leading `]` issue is a straightforward grammar incompleteness. The lexer correctly tokenizes `]` as `CBRACK`, but the parser doesn't allow `CBRACK` to appear as text at the start of strings.

**Solution**: Add `CBRACK` to `beginGenericText` with predicate `{ inBraceDepth == 0 }?`

This follows the established pattern for `CPAREN` and `CCARET`, is minimal, and works with strict mode.
