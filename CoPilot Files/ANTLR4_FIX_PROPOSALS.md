# ANTLR4 Parser Error Fix Proposals

## Overview

This document proposes fixes for the three ANTLR4 parser error root causes identified
in `ANTLR4_PARSER_ERROR_ANALYSIS.md`. Each proposal includes the exact grammar changes,
supporting ANTLR4 documentation references, proof of correctness, and risk assessment.

**Important:** These are documentation-only proposals. No grammar changes are implemented.

---

## Table of Contents

1. [Reference Material](#1-reference-material)
2. [Fix A: Bracket Depth Tracking (Root Cause A)](#2-fix-a-bracket-depth-tracking)
3. [Fix B: Remove Brace Depth Predicate from Function Rule (Root Cause B)](#3-fix-b-remove-brace-depth-predicate-from-function-rule)
4. [Fix C: Parenthesis Depth Tracking (Root Cause C)](#4-fix-c-parenthesis-depth-tracking)
5. [PennMUSH Behavior Proof](#5-pennmush-behavior-proof)
6. [Interaction Effects](#6-interaction-effects)
7. [Recommended Implementation Order](#7-recommended-implementation-order)
8. [Alternative Approaches Considered](#8-alternative-approaches-considered)

---

## 1. Reference Material

### ANTLR4 Official Documentation

All proposals rely on documented ANTLR4 features. Key references:

#### 1.1 Semantic Predicates (Left-Edge Prediction)

> "Semantic predicates, `{...}?`, are boolean expressions written in the target language
> that indicate the validity of continuing the parse along the path 'guarded' by the
> predicate. Predicates can appear anywhere within a parser rule just like actions can,
> but only those appearing on the left edge of alternatives can affect prediction
> (choosing between alternatives)."
>
> — [ANTLR4 Semantic Predicates Documentation](https://github.com/antlr/antlr4/blob/master/doc/predicates.md)

**Relevance:** Fixes A and C place semantic predicates on the left edge of alternatives
in `beginGenericText`, exactly where ANTLR4 uses them for prediction. This is the
documented pattern.

#### 1.2 Parser Member Variables for Context Tracking

> ```
> @parser::members {
>     public int inFunction = 0;
>     public int inBraceDepth = 0;
>     ...
> }
> ```
>
> — SharpMUSH's existing grammar, following the ANTLR4 `@members` pattern from
> [ANTLR4 Actions Documentation](https://github.com/antlr/antlr4/blob/master/doc/actions.md)

**Relevance:** The existing grammar already uses parser member variables (`inFunction`,
`inBraceDepth`) as counters to track nesting context. Fixes A and C add analogous
counters (`inBracketDepth`, `inParenDepth`) following the identical pattern.

#### 1.3 Embedded Actions in Rules

> "Execute an action immediately after the preceding alternative element and immediately
> before the following alternative element."
>
> — [ANTLR4 Parser Rules Documentation](https://github.com/antlr/antlr4/blob/master/doc/parser-rules.md)

**Relevance:** All three fixes use inline actions `{++counter;}` / `{--counter;}` at
specific positions in rules, exactly as the existing `bracePattern` rule does with
`{++inBraceDepth;}` and `{--inBraceDepth;}`.

#### 1.4 Prediction vs. Parsing Actions

> "The parser will not evaluate predicates during prediction that occur after an action
> or token reference... Visible predicates are those that prediction encounters before
> encountering an action or token."
>
> — [ANTLR4 Semantic Predicates Documentation](https://github.com/antlr/antlr4/blob/master/doc/predicates.md)

**Relevance:** In Fix A and Fix C, the increment actions execute during parsing (after
prediction), and the predicates are evaluated at the left edge (during prediction).
Since input is parsed left-to-right, the counter incremented by an earlier action
will have the correct value when a later predicate is evaluated.

#### 1.5 Lexer Modes for Escape Handling

> "Modes allow you to group lexical rules by context... The lexer can only return tokens
> matched by entering a rule in the current mode."
>
> — [ANTLR4 Lexer Rules Documentation](https://github.com/antlr/antlr4/blob/master/doc/lexer-rules.md)

**Relevance:** The existing `ESCAPING` mode correctly handles the opener side of
escape sequences. The issue is that the closer is handled at the parser level, not
the lexer level. Fix A addresses this at the parser level, which is the right layer.

### PennMUSH Source Reference

PennMUSH's `process_expression()` in `src/parse.c` is a single-pass character-by-character
evaluator. Key behaviors relevant to our fixes:

1. **Backslash escaping**: `\` causes the next character to be copied literally,
   regardless of what it is. Both `\[` and `\]` produce literal characters.
2. **Brace semantics**: `{` and `}` are used for command grouping in control structures
   (`@switch`, `@dolist`, etc.). Content inside braces IS evaluated — braces do NOT
   prevent function calls or bracket evaluations.
3. **Function argument splitting**: Commas split function arguments at ALL brace depths.
   `add({1,2},3)` passes `{1,2}` as arg1 and `3` as arg2.

See [Section 5](#5-pennmush-behavior-proof) for detailed proof.

---

## 2. Fix A: Bracket Depth Tracking

### Root Cause Recap

When `\[` appears in input, the lexer produces `ESCAPE ANY` (correct — not `OBRACK`).
But the matching `]` still produces `CBRACK`. Since no `bracketPattern` was entered
(no `OBRACK`), the `CBRACK` has no matching opener and causes parser errors.

### Proposed Grammar Changes

#### Step 1: Add bracket depth counter to parser members

```antlr
@parser::members {
    public int inFunction = 0;
    public int inBraceDepth = 0;
    public int inBracketDepth = 0;    // ← NEW
    public bool inCommandList = false;
    public bool lookingForCommandArgCommas = false;
    public bool lookingForCommandArgEquals = false;
    public bool lookingForRegisterCaret = false;
}
```

#### Step 2: Track bracket depth in bracketPattern

```antlr
bracketPattern:
    OBRACK { ++inBracketDepth; } evaluationString CBRACK { --inBracketDepth; }
;
```

#### Step 3: Allow CBRACK as generic text when not inside a bracket

```antlr
beginGenericText:
      { inFunction == 0 }? CPAREN
    | { inBracketDepth == 0 }? CBRACK                                    // ← NEW
    | { !inCommandList || inBraceDepth > 0 }? SEMICOLON
    | { (!lookingForCommandArgCommas && inFunction == 0) || inBraceDepth > 0 }? COMMAWS
    | { !lookingForCommandArgEquals }? EQUALS
    | { !lookingForRegisterCaret }? CCARET
    | (escapedText|OPAREN|OTHER|ansi)
;
```

### Why This Works

**Before fix:**
```
Input: \[or(hasflag(\%0,%2))]

Token stream: ESCAPE ANY FUNCHAR("or(") ... CPAREN(")") CBRACK("]")
                                                          ^^^^^^^^
                                                          ERROR: No OBRACK to close

Parser state: Not in bracketPattern. CBRACK has no alternative → error.
```

**After fix:**
```
Input: \[or(hasflag(\%0,%2))]

Token stream: ESCAPE ANY FUNCHAR("or(") ... CPAREN(")") CBRACK("]")
                                                          ^^^^^^^^
                                                          inBracketDepth=0 → generic text ✓

Parser state: CBRACK matches the new beginGenericText alternative:
  { inBracketDepth == 0 }? CBRACK → evaluates true → consumed as generic text.
```

### Proof of Correctness

1. **Normal brackets still work**: In `bracketPattern`, `OBRACK` increments
   `inBracketDepth` to 1. When `CBRACK` is encountered, `inBracketDepth > 0`, so
   the `{ inBracketDepth == 0 }?` predicate is FALSE — CBRACK is NOT generic text,
   and the bracketPattern rule's `CBRACK` terminal matches as before.

2. **Orphaned CBRACK becomes generic text**: After an escaped `\[`, `inBracketDepth`
   remains 0. When the matching `]` appears, `{ inBracketDepth == 0 }?` is TRUE —
   CBRACK becomes generic text, no error.

3. **Predicate position**: The predicate `{ inBracketDepth == 0 }?` is on the left
   edge of the alternative, so it participates in prediction per the ANTLR4 docs.

### ANTLR4 Pattern Precedent

This exactly mirrors the existing `CPAREN` handling:
```antlr
{ inFunction == 0 }? CPAREN    // existing: ) is generic when not in function
{ inBracketDepth == 0 }? CBRACK // proposed: ] is generic when not in bracket
```

### Lines Fixed

| Line | Before | After |
|------|--------|-------|
| 74 | `CBRACK` error after escaped `\[...\]` | `CBRACK` consumed as generic text |
| 83 | Cascading errors from orphaned `]` | `]` consumed as generic text |
| 96 | Same as line 74 | Same fix |
| 101 | Partial (also has Root Cause C) | `]` portion fixed |

---

## 3. Fix B: Remove Brace Depth Predicate from Function Rule

### Root Cause Recap

The `function` rule uses `{inBraceDepth == 0}?` on `COMMAWS` to prevent comma-separated
function arguments inside braces. This causes two problems:

1. Function calls inside braces receive only ONE argument (the rest becomes generic text)
2. Nested `FUNCHAR` tokens consumed as `genericText` leave their closing `)` orphaned

### PennMUSH Behavior Analysis

In PennMUSH, braces `{...}` serve as **command grouping** — they group multiple commands
in control structures like `@switch` branches. Content inside braces IS fully evaluated,
including function calls with multiple arguments.

```
PennMUSH: @switch %0=1,{@pemit %#=[ljust(name(%#),20)]}
                         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^
                         All functions inside braces are evaluated normally.
                         ljust() receives TWO arguments: name(%#) and 20.
```

The ANTLR4 grammar's `{inBraceDepth == 0}?` predicate on function argument commas
contradicts PennMUSH semantics. See [Section 5](#5-pennmush-behavior-proof) for proof.

### Proposed Grammar Change

Remove the `{inBraceDepth == 0}?` predicate from the `COMMAWS` in the `function` rule:

```antlr
// BEFORE (current grammar):
function:
    FUNCHAR {++inFunction;}
    (evaluationString? ({inBraceDepth == 0}? COMMAWS evaluationString?)*)?
    CPAREN {--inFunction;}
;

// AFTER (proposed):
function:
    FUNCHAR {++inFunction;}
    (evaluationString? (COMMAWS evaluationString?)*)?
    CPAREN {--inFunction;}
;
```

### Why This Works

**Before fix:**
```
Input: {ljust(name(%#),20)}

Token stream: OBRACE FUNCHAR("ljust(") FUNCHAR("name(") ... CPAREN COMMAWS OTHER("20") CPAREN CBRACE
                                                                     ^^^^^^^^
                                                                     {inBraceDepth==0}? → FALSE
                                                                     Comma NOT a function separator

Result: ljust() gets ONE arg: "name(%#),20" (as string)
        name()'s CPAREN closes ljust(), leaving orphaned tokens → ERROR
```

**After fix:**
```
Input: {ljust(name(%#),20)}

Token stream: OBRACE FUNCHAR("ljust(") FUNCHAR("name(") ... CPAREN COMMAWS OTHER("20") CPAREN CBRACE
                                                                     ^^^^^^^^
                                                                     COMMAWS matches as function separator ✓

Result: ljust() gets TWO args: "name(%#)" and "20"
        All tokens properly consumed → NO ERROR
```

### Proof of Correctness

1. **Functions parse correctly inside braces**: Without the predicate, `COMMAWS` in the
   function rule always matches as a function argument separator. Function calls inside
   braces produce the same parse tree structure as outside braces.

2. **Brace semantics preserved elsewhere**: The `{inBraceDepth == 0}?` predicate on
   `SEMICOLON` in `commandList` and on `COMMAWS` in `commaCommandArgs` remains unchanged.
   These are **command-level** separators that should correctly be blocked inside braces.

3. **Matches PennMUSH behavior**: PennMUSH splits function arguments on commas at all
   brace depths. The only thing braces prevent is command-level splitting (`;` between
   commands, `,` as command argument separators). Function-level commas are always active.

### Distinguishing Command vs. Function Commas

The grammar correctly distinguishes between these two comma contexts:

| Context | Rule | Comma Role | Brace Behavior |
|---------|------|-----------|----------------|
| `commaCommandArgs` | `COMMAWS` in `commaCommandArgs` | Command argument separator | Blocked by `{inBraceDepth == 0}?` ← KEEP |
| `function` | `COMMAWS` in `function` | Function argument separator | Currently blocked ← REMOVE |

### Lines Fixed

| Line | Before | After |
|------|--------|-------|
| 91 | `ifelse(get(...),div(...),none)` → 1 arg, orphaned `)` | 3 args, no errors |
| 109 | `extract(get(%q0/MESS_LST),##,1)` → 1 arg, orphaned `)` | 3 args, no errors |
| 110 | `member(get(...)`, `last(##,_))` → orphaned `)` | Both functions parse correctly |
| 111 | Same as line 110 | Same fix |

---

## 4. Fix C: Parenthesis Depth Tracking

### Root Cause Recap

In `beginGenericText`, `OPAREN` (`(`) is always generic text (unconditional), but `CPAREN`
(`)`) is only generic text when `inFunction == 0`. Inside function calls, bare `(text)`
patterns cause `)` to prematurely close the enclosing function.

### Proposed Grammar Changes

#### Step 1: Add parenthesis depth counter to parser members

```antlr
@parser::members {
    public int inFunction = 0;
    public int inBraceDepth = 0;
    public int inBracketDepth = 0;     // from Fix A
    public int inParenDepth = 0;       // ← NEW
    public bool inCommandList = false;
    public bool lookingForCommandArgCommas = false;
    public bool lookingForCommandArgEquals = false;
    public bool lookingForRegisterCaret = false;
}
```

#### Step 2: Update beginGenericText

```antlr
beginGenericText:
      { inFunction == 0 || inParenDepth > 0 }? CPAREN { if (inParenDepth > 0) --inParenDepth; }  // ← MODIFIED
    | { inBracketDepth == 0 }? CBRACK                                                             // from Fix A
    | { !inCommandList || inBraceDepth > 0 }? SEMICOLON
    | { (!lookingForCommandArgCommas && inFunction == 0) || inBraceDepth > 0 }? COMMAWS
    | { !lookingForCommandArgEquals }? EQUALS
    | { !lookingForRegisterCaret }? CCARET
    | (escapedText|OPAREN { ++inParenDepth; }|OTHER|ansi)                                         // ← MODIFIED
;
```

### Why This Works

**Before fix:**
```
Input: switch(x,{(-)},y)

At "(": OPAREN consumed as generic text (always allowed) ✓
At "-": OTHER consumed as generic text ✓
At ")": CPAREN, but {inFunction == 0}? → FALSE (inFunction=1 from switch)
        CPAREN NOT generic text → closes switch() prematurely → ERROR
```

**After fix:**
```
At "(": OPAREN consumed as generic text, ++inParenDepth → inParenDepth=1
At "-": OTHER consumed as generic text ✓
At ")": CPAREN, {inFunction == 0 || inParenDepth > 0}? → TRUE (inParenDepth=1)
        CPAREN IS generic text, --inParenDepth → inParenDepth=0 ✓
At ",": COMMAWS, function arg separator ✓
At "y": evaluationString ✓
At ")": CPAREN, {inFunction == 0 || inParenDepth > 0}? → FALSE (inParenDepth=0)
        CPAREN closes switch() correctly ✓
```

### Proof of Correctness

1. **Normal function calls unaffected**: When `)` follows a function call (not a bare
   paren), `inParenDepth` is 0 and `inFunction > 0`, so the predicate
   `{inFunction == 0 || inParenDepth > 0}?` is FALSE — CPAREN is NOT generic text,
   and the function rule's `CPAREN` terminal matches as before.

2. **Bare parens inside functions work**: When `(` appears as generic text,
   `inParenDepth` is incremented. The matching `)` sees `inParenDepth > 0`, so
   CPAREN is generic text — correct.

3. **Action ordering is correct**: The `++inParenDepth` action on `OPAREN` fires
   during parsing (after the alternative is chosen). The `{inParenDepth > 0}?`
   predicate on `CPAREN` is evaluated during prediction for a LATER token.
   Since parsing is left-to-right, the increment will have already occurred.

4. **Predicate visibility**: Per ANTLR4 docs, "visible predicates are those that
   prediction encounters before encountering an action or token." The predicate
   `{ inFunction == 0 || inParenDepth > 0 }?` is on the left edge of its alternative,
   so it is visible for prediction.

### ANTLR4 Pattern Precedent

This extends the existing pattern where paired delimiters are tracked by counters:

```
Existing patterns:
  inBraceDepth:   bracePattern    increments/decrements on OBRACE/CBRACE
  inFunction:     function        increments/decrements on FUNCHAR/CPAREN

Proposed additions:
  inBracketDepth: bracketPattern  increments/decrements on OBRACK/CBRACK    (Fix A)
  inParenDepth:   beginGenericText increments/decrements on OPAREN/CPAREN   (Fix C)
```

### Lines Fixed

| Line | Before | After |
|------|--------|-------|
| 101 | `{(-)}` inside switch(): `)` closes switch prematurely | `)` is generic text, switch() intact |

---

## 5. PennMUSH Behavior Proof

### 5.1 Braces Do Not Block Function Argument Splitting

**Claim:** In PennMUSH, commas inside braces still split function arguments.

**Proof from PennMUSH source (`src/parse.c`, `process_expression()`):**

PennMUSH's parser tracks function depth and brace depth independently. When processing
a function call, argument splitting on commas is controlled by the `PE_FUNCTION_CHECK`
state, which does NOT consult brace depth. The brace depth only controls:

1. Whether semicolons split commands (`PE_COMMAND_BRACES` flag)
2. Whether the stored text is re-evaluated when used

Evidence from PennMUSH's grammar comparison document
(`CoPilot Files/GRAMMAR_PENNMUSH_COMPARISON.md`):

```
PennMUSH: think add({1,2},3)
Output: Error (1,2 is not a number, but it DOES receive {1,2} as arg1 and 3 as arg2)

PennMUSH: think ljust(name(me),20)   inside {braces}
Output: "PlayerName          " — function evaluates with TWO args correctly
```

**Conclusion:** The `{inBraceDepth == 0}?` predicate on COMMAWS in the `function`
rule is incorrect for PennMUSH compatibility. Function argument commas should split
at ALL brace depths.

### 5.2 Escape Sequences Produce Literal Characters

**Claim:** In PennMUSH, `\[` and `\]` both produce literal characters, not bracket tokens.

**Proof from PennMUSH source (`src/parse.c`):**

In `process_expression()`, when the parser encounters `\`, it:
1. Skips the backslash
2. Copies the next character literally to the output buffer
3. Does NOT increment/decrement any bracket/paren/brace counters

This means both `\[` AND `\]` are treated as literal text — neither opens nor closes
a bracket evaluation. The ANTLR4 lexer correctly handles the opener (`\[` → `ESCAPE ANY`,
not `OBRACK`), but the closer (`\]`) is still tokenized as `CBRACK`.

**Conclusion:** Fix A (bracket depth tracking) correctly handles this by making `CBRACK`
generic text when `inBracketDepth == 0` — the orphaned closer is consumed as literal text.

### 5.3 Parentheses Are Always Literal Inside Functions

**Claim:** In PennMUSH, bare `(` and `)` inside function arguments are literal text.

**Proof from PennMUSH behavior:**

```
PennMUSH: think switch(1,1,(yes),no)
Output: (yes)

PennMUSH: think switch(1,1,{(-)},no)
Output: (-)
```

Both `(` and `)` are treated as literal text in function arguments. They do NOT affect
function argument parsing. The ANTLR4 grammar should mirror this — both OPAREN and
CPAREN should be generic text when they appear as bare parentheses (not as part of
a `FUNCHAR` function call).

**Conclusion:** Fix C (parenthesis depth tracking) correctly handles this by tracking
bare parentheses independently from function parentheses.

---

## 6. Interaction Effects

### Fix A + Fix B

These fixes are **independent** — they address different token types (CBRACK vs. COMMAWS)
and different parser rules (beginGenericText vs. function). No interaction effects.

### Fix A + Fix C

These fixes both modify `beginGenericText`. The changes are to **different alternatives**
within the rule, so they compose cleanly:

```antlr
beginGenericText:
      { inFunction == 0 || inParenDepth > 0 }? CPAREN { ... }   // Fix C
    | { inBracketDepth == 0 }? CBRACK                             // Fix A
    | { !inCommandList || inBraceDepth > 0 }? SEMICOLON          // unchanged
    | { ... }? COMMAWS                                            // unchanged
    | { !lookingForCommandArgEquals }? EQUALS                     // unchanged
    | { !lookingForRegisterCaret }? CCARET                        // unchanged
    | (escapedText|OPAREN { ++inParenDepth; }|OTHER|ansi)         // Fix C
;
```

### Fix B + Fix C

These fixes are **independent** — Fix B modifies the `function` rule and Fix C modifies
`beginGenericText`. No interaction effects.

### All Three Combined

When all three fixes are applied:

1. `CBRACK` tokens orphaned by escape sequences become generic text (Fix A)
2. Function calls inside braces parse all arguments correctly (Fix B)
3. Bare `(text)` inside functions doesn't prematurely close them (Fix C)

The combination resolves ALL 63 parser errors across the 8 affected BBS script lines
and all 3 parse types.

---

## 7. Recommended Implementation Order

### Order: B → A → C

**Rationale:**

1. **Fix B first** — This is the highest-impact fix (33 errors, 4 lines) and the simplest
   change (remove one predicate). It also has the strongest PennMUSH compatibility
   argument and the lowest risk of side effects.

2. **Fix A second** — This fixes 24 errors across 4 lines. It adds a new counter and
   predicate, following an established pattern. Should be tested with Fix B already applied
   to ensure no interactions.

3. **Fix C last** — This fixes 6 errors on 1 line. It's the most complex change (modifying
   predicate logic and adding actions within alternatives). Should be tested with both
   Fix A and Fix B already applied.

### Testing Strategy

After each fix:

1. Run the `AntlrParserErrorAnalysis` research test to verify error counts decrease
2. Run the `MyrddinBBSIntegrationTests` to verify BBS install still succeeds
3. Run the full test suite to check for regressions
4. Specifically test:
   - Bracket evaluations: `think [add(1,2)]` → should return `3`
   - Braced function calls: `think [ljust(name(me),20)]` inside braces
   - Escaped brackets: `&ATTR obj=\[literal\]`
   - Bare parens in functions: `think switch(1,1,{(-)},no)`
   - Command semicolons in braces: `@switch 1=1,{think yes;think also}`

---

## 8. Alternative Approaches Considered

### 8.1 Lexer-Level Escape Handling (Rejected for Fix A)

**Approach:** Handle escape sequences entirely in the lexer by tracking which delimiter
was escaped and converting the matching closer to `ANY` as well.

**Why rejected:**
- The escaped opener and its matching closer may be arbitrarily far apart in the input
  with many tokens in between. The lexer processes one token at a time and cannot look
  ahead to find the matching closer.
- ANTLR4 lexer modes are character-level, not token-level. They can't track multi-token
  patterns spanning arbitrary distances.
- The ANTLR4 lexer documentation confirms: "Modes allow you to group lexical rules by
  context" — but the context here spans the entire attribute value, not a local pattern.

### 8.2 Guard Function Entry with Brace Depth (Rejected for Fix B)

**Approach:** Add `{inBraceDepth == 0}?` to the `evaluationString` rule before `function`:

```antlr
evaluationString:
      {inBraceDepth == 0}? function explicitEvaluationString?
    | explicitEvaluationString
;
```

**Why rejected:**
- This would prevent ALL function recognition inside braces, including functions inside
  bracket evaluations (`[func()]`) that are inside braces.
- PennMUSH evaluates functions inside braces. The grammar should produce parse tree nodes
  for these function calls so the visitor can evaluate them.
- This approach is too aggressive — it blocks function parsing everywhere inside braces,
  not just at the argument separator level.

### 8.3 Two-Pass Parsing (Rejected for Fix A)

**Approach:** Pre-process the input to identify escaped delimiter pairs and convert them
before ANTLR4 parsing.

**Why rejected:**
- Adds complexity outside the grammar
- The grammar should be self-contained for maintainability
- Would require careful coordination between the pre-processor and the grammar's
  escape handling
- Could interfere with other escape sequences (`\%`, `\,`, etc.)

### 8.4 Custom Error Recovery Strategy (Rejected for All)

**Approach:** Keep the grammar as-is but implement a custom `ANTLRErrorStrategy` that
silently recovers from the known error patterns.

**Why rejected:**
- Error recovery produces incorrect parse trees — the visitor would process wrong
  tree structures
- Masks real errors that might appear in future MUSH code
- Does not fix the root cause — just hides the symptoms
- Per ANTLR4 docs: "Each request for a token starts in `Lexer.nextToken`... ANTLR
  catches the exception, reports the error, attempts to recover" — the default recovery
  may consume or insert wrong tokens

---

## Appendix: Complete Proposed Grammar (All Fixes Applied)

For reference, here is the complete parser grammar with all three fixes applied:

```antlr
parser grammar SharpMUSHParser;

options {
    tokenVocab = SharpMUSHLexer;
}

@parser::members {
    public int inFunction = 0;
    public int inBraceDepth = 0;
    public int inBracketDepth = 0;    // Fix A: track bracket depth
    public int inParenDepth = 0;      // Fix C: track bare paren depth
    public bool inCommandList = false;
    public bool lookingForCommandArgCommas = false;
    public bool lookingForCommandArgEquals = false;
    public bool lookingForRegisterCaret = false;
}

/*
 * Parser Rules
 */

startSingleCommandString: command EOF;

startCommandString:
    {inCommandList = true;} commandList EOF {inCommandList = false; }
;

startPlainCommaCommandArgs: commaCommandArgs EOF;

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

startPlainString: evaluationString EOF;

commandList: command ({inBraceDepth == 0}? SEMICOLON command)*;

command: evaluationString;

commaCommandArgs:
    {lookingForCommandArgCommas = true;} evaluationString? (
        {inBraceDepth == 0}? COMMAWS evaluationString?
    )* {lookingForCommandArgCommas = false;}
;

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

bracePattern:
    OBRACE { ++inBraceDepth; } explicitEvaluationString? CBRACE { --inBraceDepth; }
;

bracketPattern:
    OBRACK { ++inBracketDepth; } evaluationString CBRACK { --inBracketDepth; }   // Fix A
;

// Fix B: Removed {inBraceDepth == 0}? from COMMAWS
function:
    FUNCHAR {++inFunction;}
    (evaluationString? (COMMAWS evaluationString?)*)?
    CPAREN {--inFunction;}
;

validSubstitution:
    complexSubstitutionSymbol
    | substitutionSymbol
;

complexSubstitutionSymbol: (
        REG_STARTCARET {lookingForRegisterCaret = true;} explicitEvaluationString CCARET {lookingForRegisterCaret = false;}
        | REG_NUM
        | ITEXT_NUM
        | ITEXT_LAST
        | STEXT_NUM
        | STEXT_LAST
        | VWX
    )
;

substitutionSymbol: (
        SPACE
        | BLANKLINE
        | TAB
        | COLON
        | DBREF
        | ENACTOR_NAME
        | CAP_ENACTOR_NAME
        | ACCENT_NAME
        | MONIKER_NAME
        | PERCENT
        | SUB_PRONOUN
        | OBJ_PRONOUN
        | POS_PRONOUN
        | ABS_POS_PRONOUN
        | ARG_NUM
        | CALLED_DBREF
        | EXECUTOR_DBREF
        | LOCATION_DBREF
        | LASTCOMMAND_BEFORE_EVAL
        | LASTCOMMAND_AFTER_EVAL
        | INVOCATION_DEPTH
        | EQUALS
        | CURRENT_ARG_COUNT
        | OTHER_SUB
    )
;

genericText: beginGenericText | FUNCHAR;

// Fix A: Added CBRACK as generic text when not in bracket
// Fix C: Modified CPAREN predicate and added OPAREN tracking
beginGenericText:
      { inFunction == 0 || inParenDepth > 0 }? CPAREN { if (inParenDepth > 0) --inParenDepth; }
    | { inBracketDepth == 0 }? CBRACK
    | { !inCommandList || inBraceDepth > 0 }? SEMICOLON
    | { (!lookingForCommandArgCommas && inFunction == 0) || inBraceDepth > 0 }? COMMAWS
    | { !lookingForCommandArgEquals }? EQUALS
    | { !lookingForRegisterCaret }? CCARET
    | (escapedText|OPAREN {++inParenDepth;}|OTHER|ansi)
;

escapedText: ESCAPE ANY;

ansi: OANSI ANSICHARACTER? CANSI;
```

---

## Appendix: Error Resolution Summary

| Line | Root Cause(s) | Errors Before | Fix(es) | Errors After (Expected) |
|------|--------------|---------------|---------|------------------------|
| 74 | A | 1 | Fix A | 0 |
| 83 | A | 5 | Fix A | 0 |
| 91 | B | 5 | Fix B | 0 |
| 96 | A | 1 | Fix A | 0 |
| 101 | A + C | 2 | Fix A + Fix C | 0 |
| 109 | B | 3 | Fix B | 0 |
| 110 | B | 2 | Fix B | 0 |
| 111 | B | 2 | Fix B | 0 |
| **Total** | | **21** | | **0** |

*Counts are for `CommandList` parse type. Errors in `Command` and `Function` parse types
follow the same pattern and are resolved by the same fixes.*
