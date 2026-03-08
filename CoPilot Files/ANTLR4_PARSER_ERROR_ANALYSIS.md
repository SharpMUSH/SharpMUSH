# ANTLR4 Parser Error Analysis — Myrddin BBS v4.0.6

## Executive Summary

This document presents a deep analysis of the ANTLR4 parser errors that occur when
processing Myrddin's BBS v4.0.6 installation script through the SharpMUSH parser.
Of the 150 script lines (102 executable), **8 lines** produce parser errors. These
errors stem from **two distinct root causes** in the ANTLR4 grammar, both related to
how the parser handles token semantics inside braces (`{}`).

**Root Cause A — Escape Sequences:** MUSH escape sequences like `\[`, `\(`, `\)` cause
bracket/paren nesting mismatches because the lexer correctly escapes the opening
delimiter but the matching closer has no corresponding opener.

**Root Cause B — Brace Depth Predicate:** The `{inBraceDepth == 0}` predicate in the
`function` rule prevents multi-argument function calls inside braces. This causes
secondary function names to be consumed as generic text, leaving their closing
parentheses orphaned.

**Root Cause C — OPAREN/CPAREN Asymmetry:** `(` is always generic text via
`beginGenericText`, but `)` is only generic text when `inFunction == 0`. Inside
function calls, bare `(text)` patterns cause `)` to prematurely close the enclosing
function.

---

## Table of Contents

1. [Grammar Architecture](#1-grammar-architecture)
2. [Lexer Token Definitions](#2-lexer-token-definitions)
3. [Parser Rule Graph](#3-parser-rule-graph)
4. [Root Cause A: Escape Sequence Mismatch](#4-root-cause-a-escape-sequence-mismatch)
5. [Root Cause B: Brace Depth Predicate Blocks Function Args](#5-root-cause-b-brace-depth-predicate-blocks-function-args)
6. [Root Cause C: OPAREN/CPAREN Asymmetry](#6-root-cause-c-oparencparen-asymmetry)
7. [Per-Line Error Analysis](#7-per-line-error-analysis)
8. [Error Category Statistics](#8-error-category-statistics)
9. [Grammar Rule Flow Diagrams](#9-grammar-rule-flow-diagrams)

---

## 1. Grammar Architecture

The SharpMUSH ANTLR4 parser consists of two files:

| File | Purpose |
|------|---------|
| `SharpMUSH.Parser.Generated/SharpMUSHLexer.g4` | Lexer with 4 modes: DEFAULT, SUBSTITUTION, ESCAPING, ANSI |
| `SharpMUSH.Parser.Generated/SharpMUSHParser.g4` | Parser with semantic predicates for context-sensitive parsing |

The parser uses **semantic predicates** (inline code blocks in `{...}`) to track:
- `inFunction` (int): depth of nested function calls
- `inBraceDepth` (int): depth of nested brace patterns `{...}`
- `inCommandList` (bool): whether parsing a command list
- `lookingForCommandArgCommas` (bool): whether commas are command arg separators
- `lookingForCommandArgEquals` (bool): whether `=` is a command arg split
- `lookingForRegisterCaret` (bool): whether `>` closes a register name

---

## 2. Lexer Token Definitions

### DEFAULT Mode Tokens

```antlr
ESCAPE:    '\\' -> pushMode(ESCAPING)     // Enters escape mode
OBRACK:    '[' WS                         // Opening bracket (evaluation)
CBRACK:    WS ']'                         // Closing bracket
OBRACE:    '{' WS                         // Opening brace (grouping)
CBRACE:    WS '}'                         // Closing brace
CPAREN:    WS ')'                         // Closing paren (function)
COMMAWS:   WS ',' WS                     // Comma with whitespace
FUNCHAR:   [0-9a-zA-Z_~@`]+ '(' WS      // Function name + open paren
OPAREN:    '(' WS                         // Bare open paren
OTHER:     ~special+                      // Non-special characters (greedy)
```

### ESCAPING Mode
```antlr
ANY: . -> popMode    // Matches exactly ONE character, then returns to DEFAULT
```

**Key property:** When the lexer encounters `\`, it enters ESCAPING mode and the next
character becomes `ANY` regardless of what it is. This means `\[` produces tokens
`ESCAPE ANY` (not `OBRACK`), and `\)` produces `ESCAPE ANY` (not `CPAREN`).

---

## 3. Parser Rule Graph

```
Entry Points:
  startSingleCommandString ──► command ──► evaluationString ──► EOF
  startCommandString ──► commandList ──► EOF
  startPlainString ──► evaluationString ──► EOF

commandList:
  command ( SEMICOLON command )*
  └─ guarded by: {inBraceDepth == 0}? on SEMICOLON

evaluationString:
  ├── function  explicitEvaluationString?
  └── explicitEvaluationString

function:
  FUNCHAR {++inFunction}
    ( evaluationString? ( {inBraceDepth==0}? COMMAWS evaluationString? )* )?    ◄── ROOT CAUSE B
  CPAREN {--inFunction}

explicitEvaluationString:
  ( bracePattern | bracketPattern | beginGenericText | %substitution )
  ( bracePattern | bracketPattern | %substitution | genericText )*

bracePattern:
  OBRACE {++inBraceDepth} explicitEvaluationString? CBRACE {--inBraceDepth}

bracketPattern:
  OBRACK evaluationString CBRACK

genericText:
  ├── beginGenericText
  └── FUNCHAR                  ◄── Function-like tokens consumed as literal text

beginGenericText:
  ├── {inFunction == 0}? CPAREN                                                 ◄── ROOT CAUSE C
  ├── {!inCommandList || inBraceDepth > 0}? SEMICOLON
  ├── {(!lookingForCommandArgCommas && inFunction == 0) || inBraceDepth > 0}? COMMAWS
  ├── {!lookingForCommandArgEquals}? EQUALS
  ├── {!lookingForRegisterCaret}? CCARET
  └── ( escapedText | OPAREN | OTHER | ansi )

escapedText:
  ESCAPE ANY                   ◄── ROOT CAUSE A: escape consumes one char
```

---

## 4. Root Cause A: Escape Sequence Mismatch

### Mechanism

PennMUSH uses backslash escaping to store literal special characters in attribute
values: `\[`, `\]`, `\(`, `\)`, `\,`, `\%`. When the BBS script sets attributes with
`&ATTR_NAME object=value`, these escape sequences are part of the stored value.

The ANTLR lexer handles `\` by entering ESCAPING mode, which consumes exactly one
character as the `ANY` token. This correctly prevents the escaped character from being
tokenized as its special meaning (e.g., `\[` does NOT become `OBRACK`).

**However**, the matching closing delimiter IS still tokenized as its special token:

```
Input:  \[or(hasflag(\%0,%2),hasflag(\%0,wizard))]
Tokens: ESCAPE ANY('[') FUNCHAR('or(') FUNCHAR('hasflag(') ESCAPE ANY('%') ...
        ... CPAREN(')') COMMAWS(',') ... CPAREN(')') CBRACK(']')
                                                              ^^^^^^^^^^^^
                                                              No matching OBRACK!
```

The parser enters `escapedText` for `\[` (an `evaluationString` context), but then
processes `or(hasflag(...))` as real function calls. When it reaches `]`, this becomes
`CBRACK` — but there is no open `bracketPattern` to close, causing:
- `"extraneous input ')' expecting CBRACK"` — when `)` appears where `]` was expected
- `"Unexpected token ',' at this position"` — cascading from mismatched nesting

### Affected Lines

| Line | Attribute | Escape Sequences | Error Pattern |
|------|-----------|-----------------|---------------|
| 74 | `&CMD_+BBLOCK` | `\[or(hasflag(\%0,...)]` | Orphan `]` after escaped `\[` |
| 83 | `&CMD_+BBREAD2` | `\([name(...)]\)` | Orphan `)` after escaped `\(` |
| 96 | `&CMD_+BBWRITELOCK` | `\[or(hasflag(\%0,...)]` | Same as line 74 |
| 101 | `&CMD_+BBREAD` | `\(-\)` and `\,` | Orphan `)` and orphan `,` |

### Token-Level Trace for Line 74

```
Position   Characters    Token         Grammar Effect
────────   ──────────    ─────         ──────────────
196-197    \[            ESCAPE, ANY   escapedText: literal '['
198-200    or(           FUNCHAR       function rule entered, inFunction++
201-208    hasflag(      FUNCHAR       nested function, inFunction++
209-210    \%            ESCAPE, ANY   escapedText: literal '%'
211        0             OTHER         generic text
212        ,             COMMAWS       function arg separator (inBraceDepth=0)
213        %             PERCENT       substitution mode entered
214        2             ARG_NUM       %2 substitution, popMode
215        )             CPAREN        closes hasflag(), inFunction--
216        ,             COMMAWS       arg separator in or()
...
233        )             CPAREN        closes or(), inFunction--
234        ]             CBRACK        ← ERROR: no matching OBRACK!
```

---

## 5. Root Cause B: Brace Depth Predicate Blocks Function Args

### Mechanism

The `function` parser rule uses a semantic predicate to control when commas are
function argument separators:

```antlr
function:
    FUNCHAR {++inFunction;}
    (evaluationString? ({inBraceDepth == 0}? COMMAWS evaluationString?)*)?
    CPAREN {--inFunction;}
;
```

The predicate `{inBraceDepth == 0}?` on `COMMAWS` means: **commas can only be function
argument separators when not inside braces**. When `inBraceDepth > 0`, the repeated
`COMMAWS evaluationString?` alternative never matches, so the function receives at most
ONE argument.

This is **intentional** — in MUSH, braces prevent evaluation:
```
&ATTR obj={add(1,2)}     ← stores literal "add(1,2)", not evaluated
```

The stored text `add(1,2)` should be treated as one string, not a function call.

**The problem** arises when MUSH code has nested function calls with multiple arguments
inside braces. In PennMUSH, braces in `@switch` branches are used for command grouping
(like `{ }` in C), and the code inside IS evaluated:

```
@switch %0=1,{@pemit %#=[ljust(name(%#),20)]}
```

Here `ljust(name(%#),20)` is inside braces but SHOULD be evaluated as a two-argument
function call. The ANTLR grammar's `inBraceDepth` predicate prevents this.

### What Happens

When the parser encounters a multi-arg function call inside braces:

1. The outer function (e.g., `ljust(`) enters the `function` rule, `inFunction++`
2. The first argument's `evaluationString` starts — this may contain nested function calls
3. A nested function (e.g., `get(#222/groups)`) processes correctly as a sub-function
4. After the nested function closes, the parser is back in the outer function's evaluationString
5. A comma `,` appears — but `{inBraceDepth == 0}?` FAILS (we're inside braces)
6. The comma cannot be a function arg separator
7. Instead, `COMMAWS` matches in `beginGenericText` (because `inBraceDepth > 0`)
8. Subsequent function-like text (e.g., `last(`) is consumed as `FUNCHAR` in `genericText`
9. The `FUNCHAR` does NOT create a function context — `inFunction` is NOT incremented
10. When `)` appears, it closes the **outer** function, not the genericText function
11. Extra `)` tokens from the genericText functions become orphaned

### Token-Level Trace for Line 110 (inside braces at depth 3)

```
Context: [member(get(%q0/mess_lst),last(##,_))],6)
         ↑bracket    ↑function    ↑generic!  ↑↑

Position  Characters    Token        Rule              Effect
────────  ──────────    ─────        ────              ──────
714       [             OBRACK       bracketPattern    enter bracket
715-721   member(       FUNCHAR      function          inFunction++
722-725   get(          FUNCHAR      function(nested)  inFunction++
726-728   %q0           PERCENT+REG  substitution      %q0 resolved
729-737   /mess_lst     OTHER        evaluationString  text
738       )             CPAREN       function          closes get(), inFunction--
739       ,             COMMAWS      beginGenericText  ← NOT function separator!
740-743   last(         FUNCHAR      genericText       ← NOT a function call!
744-745   ##            OTHER        genericText       text
746       ,             COMMAWS      beginGenericText  text (inBraceDepth>0)
747       _             OTHER        genericText       text
748       )             CPAREN       function          closes member(), inFunction--
749       )             CPAREN       ???               ← ERROR! Orphaned closer
750       ]             CBRACK       ???               expected by bracket, but...
```

At position 749, the parser has:
- Exited the `function` rule for `member()` (closed at 748)
- Is inside `bracketPattern`, expecting `CBRACK`
- Gets `CPAREN` instead → **"extraneous input ')' expecting CBRACK"**

### Affected Lines

| Line | Attribute | Inner Function Pattern | Error |
|------|-----------|----------------------|-------|
| 91 | `&CMD_+BBLIST` | `rjust(ifelse(get(...),...),4)` in braces | Orphaned `)` from ifelse args |
| 109 | `&CMD_+BBTIMEOUT` | `extract(get(%q0/MESS_LST),##,1)` in braces | Orphaned `)` from extract args |
| 110 | `&CMD_+BBSEARCH` | `member(get(...)`, `last(##,_))` in braces | Orphaned `)` from last() |
| 111 | `&CMD_+BBSEARCH_NOGREP` | Same pattern as line 110 | Same errors |

---

## 6. Root Cause C: OPAREN/CPAREN Asymmetry

### Mechanism

In `beginGenericText`, the treatment of parentheses is asymmetric:

```antlr
beginGenericText:
      { inFunction == 0 }? CPAREN      // ')' is generic text ONLY when not in a function
    | ...
    | (escapedText|OPAREN|OTHER|ansi)   // '(' is ALWAYS generic text
;
```

`OPAREN` (`(`) is always available as generic text — it appears in the unconditional
last alternative. But `CPAREN` (`)`) is only generic text when `inFunction == 0`.

This means that inside a function call, `(text)` patterns break:
- `(` → `OPAREN`, consumed as generic text (always available)
- `)` → `CPAREN`, **cannot** be generic text because `inFunction > 0`
- `)` therefore closes the enclosing function prematurely

### Example from Line 101

```
switch(...,1:*:1,{(-)},1:*:0,...)
                  ^^^
```

Inside `switch()` (inFunction > 0), the brace `{(-)}` creates a `bracePattern`:
1. `{` → `OBRACE`, `inBraceDepth++`
2. `(` → `OPAREN`, generic text ✓
3. `-` → `OTHER`, generic text ✓
4. `)` → `CPAREN`, but `inFunction > 0` so NOT generic text!
5. `)` closes `switch()` prematurely

### Affected Lines

| Line | Attribute | Pattern | Error |
|------|-----------|---------|-------|
| 101 | `&CMD_+BBREAD` | `{(-)}` inside `switch()` | `)` closes switch prematurely |
| 101 | `&CMD_+BBREAD` | `'\(-\)'` (escaped parens) | Orphan `)` from `\)` |

---

## 7. Per-Line Error Analysis

### Line 74: `&CMD_+BBLOCK` (458 chars)

**Root Cause:** A (Escape Sequence Mismatch)

**MUSH Code Pattern:**
```
{&CANREAD %q0=\[or(hasflag(\%0,%2),hasflag(\%0,wizard))]}
```

This stores a lock expression as an attribute value. The `\[...\]` brackets are
escaped to prevent evaluation during storage. When the attribute is later used,
the `\` escapes are stripped and `[or(hasflag(%0,%2),hasflag(%0,wizard))]` is evaluated.

**Errors:**
| Column | Error | Token |
|--------|-------|-------|
| 64 | Unexpected token ',' at this position | `,` |

The error at col 64 is from the `,` after the `@switch` expression `hasflag(%#,wizard)=1,{...}`.
This cascading error results from the escaped bracket content confusing the parser's
nesting expectations for the entire command structure.

---

### Line 83: `&CMD_+BBREAD2` (1886 chars — longest line)

**Root Cause:** A (Escape Sequence Mismatch)

**MUSH Code Pattern:**
```
[mid([index(%q3,|,3,1)][ifelse(and(hasattr(%q0,anonymous),hasflag(%#,wizard)),%b\([name(index(%q3,|,4,1))]\),)],0,21)]
```

The `\(` and `\)` escape literal parentheses to display author names like `(AuthorName)`
in anonymous board listings. The escaped `\(` is consumed as `escapedText`, but the
content between is parsed as real function calls. The matching `\)` becomes `ESCAPE ANY`,
but the tokens between create mismatches.

**Errors:**
| Column | Error | Token |
|--------|-------|-------|
| 707 | extraneous input ')' expecting CBRACK | `)` |
| 1422 | missing CBRACK at ')' | `)` |
| 1454 | Unexpected token ']' at this position | `]` |
| 1644 | extraneous input ')' expecting CBRACK | `)` |
| 1885 | Unexpected end of input | `}` |

This line has TWO instances of the `\([name(...)]\)` pattern (cols 678 and 1615),
each producing similar cascading errors.

---

### Line 91: `&CMD_+BBLIST` (499 chars)

**Root Cause:** B (Brace Depth Predicate)

**MUSH Code Pattern:**
```
iter(get(#222/groups),switch(u(##/canread,%#),1,{
  %r%b[trim(
    [ljust(member(get(#222/groups),##),5)]
    [ljust(name(##),32)]
    [ljust(switch(member(get(%#/bb_omit),##),0,Yes,No),19)]
    [rjust(ifelse(get(##/config_timeout),div(get(##/config_timeout),86400),none),4)]
  )]
}))
```

Inside the `{...}` braces (switch branch), `inBraceDepth=1`. All function calls
within (`ljust`, `rjust`, `ifelse`, `get`, `div`, `member`, `switch`, `name`) have
their commas treated as generic text. Functions like `ifelse(get(...),div(...),none)`
become `ifelse("get(...),div(...),none")` — a single-argument call. The nested `div(`
is consumed as FUNCHAR generic text, and its closing `)` closes `ifelse()` instead,
leaving orphaned tokens.

**Errors:**
| Column | Error | Token |
|--------|-------|-------|
| 401 | missing CBRACK at ')' | `)` |
| 404 | extraneous input ')' expecting CBRACK | `)` |
| 406 | missing CBRACE at ')' | `)` |
| 407 | extraneous input ']' expecting {CPAREN, COMMAWS} | `]` |
| 408 | Unexpected token '}' at this position | `}` |

---

### Line 96: `&CMD_+BBWRITELOCK` (459 chars)

**Root Cause:** A (Escape Sequence Mismatch)

**MUSH Code Pattern:**
```
{&CANWRITE %q0=\[or(hasflag(\%0,%2),hasflag(\%0,wizard))]}
```

Identical pattern to line 74. Same root cause and error mechanism.

**Errors:**
| Column | Error | Token |
|--------|-------|-------|
| 74 | Unexpected token ',' at this position | `,` |

---

### Line 101: `&CMD_+BBREAD` (698 chars)

**Root Causes:** A + C (Escape Sequences + OPAREN/CPAREN Asymmetry)

**MUSH Code Pattern (two issues):**

1. **Escaped parens/comma:**
```
'\(-\)' = read only\, but you can write
```
These display literal `(-)` and `,` characters in the BBS output formatting.

2. **Bare parentheses inside braces:**
```
switch(...,1:*:1,{(-)},1:*:0,-,*)
```
The `{(-)}` contains a bare `(-)` inside braces inside `switch()`. The `(`
is generic text but `)` closes `switch()` prematurely (Root Cause C).

**Errors:**
| Column | Error | Token |
|--------|-------|-------|
| 299 | Unexpected token ')' at this position | `)` |
| 698 | extraneous input ')' expecting CBRACK | `)` |

---

### Line 109: `&CMD_+BBTIMEOUT` (765 chars)

**Root Cause:** B (Brace Depth Predicate)

**MUSH Code Pattern:**
```
@dolist [sort(...)]={
  @switch [or(strmatch(index(get(%q0/HDR_[setq(1,extract(get(%q0/MESS_LST),##,1))]%q1),|,4,1),%#),
  hasflag(%#,wizard))]:...
}
```

Inside the `={}` braces, the nested function calls
`extract(get(%q0/MESS_LST),##,1)` have their commas consumed as generic text
(Root Cause B). The `extract(` call gets one argument and closes early,
leaving orphaned `)` tokens.

**Errors:**
| Column | Error | Token |
|--------|-------|-------|
| 189 | missing CBRACK at ')' | `)` |
| 191 | Unexpected token ']' at this position | `]` |
| 195 | missing CBRACK at ')' | `)` |

---

### Line 110: `&CMD_+BBSEARCH` (963 chars)

**Root Cause:** B (Brace Depth Predicate)

**MUSH Code Pattern:**
```
@dolist %q2={@pemit %#=
  [ljust([member(get(#222/groups),%q0)]/[member(get(%q0/mess_lst),last(##,_))],6)]
  [ljust(u(#222/fn_msg_flags,%#,%q0,last(##,_)),2)]
  [ljust(index([setr(3,get(%q0/##))],|,1,1),35)]
  [ljust(index(%q3,|,2,1),14)]
  ...
}
```

Inside the `={}` braces (`inBraceDepth=3` due to nesting), the function call
`member(get(%q0/mess_lst),last(##,_))` cannot split its arguments on commas.
`last(##,_)` is consumed as FUNCHAR generic text. The `)` from `last()` closes
`member()`, and `member()`'s own `)` becomes orphaned, causing the error
at the next `]` which was expecting to close the bracketPattern.

**Errors:**
| Column | Error | Token |
|--------|-------|-------|
| 749 | extraneous input ')' expecting CBRACK | `)` |
| 836 | extraneous input ')' expecting CBRACK | `)` |

---

### Line 111: `&CMD_+BBSEARCH_NOGREP` (917 chars)

**Root Cause:** B (Brace Depth Predicate)

Identical structure to line 110 (it's the grep-free fallback for the same command).
Same error pattern with slightly different column positions.

**Errors:**
| Column | Error | Token |
|--------|-------|-------|
| 703 | extraneous input ')' expecting CBRACK | `)` |
| 790 | extraneous input ')' expecting CBRACK | `)` |

---

## 8. Error Category Statistics

### By Root Cause

| Root Cause | Lines Affected | Total Errors (across ParseTypes) |
|-----------|---------------|----------------------------------|
| A: Escape Sequence Mismatch | 74, 83, 96, 101 | 24 |
| B: Brace Depth Predicate | 91, 109, 110, 111 | 33 |
| C: OPAREN/CPAREN Asymmetry | 101 | 6 |

*Note: Line 101 has both Root Causes A and C. Errors counted across CommandList, Command, and Function parse types.*

### By Error Pattern

| Pattern | Count | Description |
|---------|-------|-------------|
| ExtraneousInput_ExpectingCBRACK | 24 | `)` where `]` expected — orphaned function closers |
| UnexpectedToken | 18 | Various unexpected tokens from cascading errors |
| MissingCBRACK | 12 | Parser inserting synthetic `]` for recovery |
| UnexpectedEndOfInput | 3 | Parser reaches EOF with unclosed delimiters |
| MissingCBRACE | 3 | Parser inserting synthetic `}` for recovery |
| ExtraneousInput_Other | 3 | Unexpected `]` inside function context |

### Parse Type Independence

All errors are **identical** across all three parse types (CommandList, Command,
Function). This confirms the errors originate in the core `evaluationString` /
`function` / `bracePattern` rules, which are shared by all parse entry points.

---

## 9. Grammar Rule Flow Diagrams

### Normal Function Call (outside braces)

```
Input: ljust(name(%#),20)

evaluationString
  └── function
        ├── FUNCHAR: "ljust("         inFunction: 0→1
        ├── evaluationString[1]
        │     └── function
        │           ├── FUNCHAR: "name("    inFunction: 1→2
        │           ├── evaluationString
        │           │     └── explicitEvaluationString
        │           │           └── PERCENT + substitution: %#
        │           └── CPAREN: ")"         inFunction: 2→1
        ├── COMMAWS: ","               ✓ inBraceDepth==0, matches!
        ├── evaluationString[2]
        │     └── explicitEvaluationString
        │           └── OTHER: "20"
        └── CPAREN: ")"               inFunction: 1→0
```

### Function Call Inside Braces (Root Cause B)

```
Input: {ljust(name(%#),20)}

bracePattern
  ├── OBRACE: "{"                    inBraceDepth: 0→1
  ├── explicitEvaluationString
  │     └── (via evaluationString)
  │         function
  │           ├── FUNCHAR: "ljust("    inFunction: 0→1
  │           ├── evaluationString[1]
  │           │     └── function
  │           │           ├── FUNCHAR: "name("   inFunction: 1→2
  │           │           ├── evaluationString
  │           │           │     └── PERCENT + substitution: %#
  │           │           └── CPAREN: ")"        inFunction: 2→1
  │           │
  │           │   ← {inBraceDepth==0}? COMMAWS  FAILS! (inBraceDepth=1)
  │           │   ← Parser looks for CPAREN to close ljust()
  │           │
  │           ├── (evaluationString continues via explicitEvaluationString)
  │           │     ├── COMMAWS: ","       matched by beginGenericText (inBraceDepth>0)
  │           │     └── OTHER: "20"        generic text
  │           │
  │           └── CPAREN: ")"              inFunction: 1→0
  │                                        ljust() closes with ONE arg: "name(%#),20"
  └── CBRACE: "}"                    inBraceDepth: 1→0
```

In this case, `ljust` receives `"name(%#),20"` as a single argument instead of
two arguments `"name(%#)"` and `"20"`. This is actually correct for MUSH attribute
storage semantics — the issue only causes parser **errors** when the nested
functions have their own closing parentheses that get misattributed.

### Escape Sequence Flow (Root Cause A)

```
Input: \[or(hasflag(\%0,%2))]

explicitEvaluationString
  ├── escapedText                      ← ESCAPE + ANY('[')
  │     └── ESCAPE: "\"
  │     └── ANY: "["                   NOT an OBRACK — literal text
  ├── (via genericText / evaluationString)
  │   function
  │     ├── FUNCHAR: "or("             inFunction: 0→1
  │     ├── evaluationString
  │     │     └── function
  │     │           ├── FUNCHAR: "hasflag("   inFunction: 1→2
  │     │           ├── evaluationString
  │     │           │     ├── escapedText: \%
  │     │           │     └── OTHER: "0"
  │     │           ├── COMMAWS: ","          ✓ (inBraceDepth==0)
  │     │           ├── evaluationString
  │     │           │     └── PERCENT + ARG_NUM: %2
  │     │           └── CPAREN: ")"           inFunction: 2→1
  │     └── CPAREN: ")"                       inFunction: 1→0
  │
  ├── CBRACK: "]"                      ← ERROR! No matching OBRACK
  │                                       Parser is in evaluationString context,
  │                                       not bracketPattern. CBRACK is unexpected.
```

### OPAREN/CPAREN Asymmetry (Root Cause C)

```
Input: switch(x,{(-)},y)

function
  ├── FUNCHAR: "switch("              inFunction: 0→1
  ├── evaluationString: "x"
  ├── COMMAWS: ","                    ✓ (inBraceDepth==0)
  ├── evaluationString:
  │     └── bracePattern
  │           ├── OBRACE: "{"         inBraceDepth: 0→1
  │           ├── explicitEvaluationString
  │           │     ├── OPAREN: "("   ← Always generic text ✓
  │           │     ├── OTHER: "-"    ← Generic text ✓
  │           │     └── ??? CPAREN: ")"
  │           │           {inFunction == 0}? → FALSE (inFunction=1)
  │           │           CPAREN is NOT generic text!
  │           │           CPAREN closes switch()!     ← ERROR!
  │           └── ...bracePattern expects CBRACE...
  ...
```

---

## Appendix: Files Referenced

- `SharpMUSH.Parser.Generated/SharpMUSHLexer.g4` — Lexer grammar
- `SharpMUSH.Parser.Generated/SharpMUSHParser.g4` — Parser grammar
- `SharpMUSH.Tests/Integration/TestData/MyrddinBBS_v406.txt` — BBS install script
- `SharpMUSH.Tests/Integration/AntlrParserErrorAnalysis.cs` — Research test
- `SharpMUSH.Implementation/MUSHCodeParser.cs` — Parser implementation
