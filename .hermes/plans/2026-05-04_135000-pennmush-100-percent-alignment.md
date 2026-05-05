# Plan: 100% PennMUSH ↔ SharpMUSH Behavioral Alignment

## Goal

Ensure SharpMUSH produces identical output to PennMUSH for all softcode (MUSHcode DSL) evaluation, with deliberate incompatibilities documented and tested.

## Background

### What PennMUSH Is
PennMUSH is a C-based MUD server (Multi-User Dungeon/Shared Hallucination) dating from ~1992. It provides:
- **Hardcode**: The C engine — parser, evaluator, database, networking, command dispatch, built-in functions
- **Softcode**: A domain-specific language (MUSHcode) that players/builders write and store on objects as attributes. The engine evaluates it on-the-fly.

### MUSHcode (Softcode) DSL Characteristics
- Expression-based: `[function(arg1,arg2)]` triggers evaluation
- Substitution-based: `%r` (newline), `%t` (tab), `%0`-`%9` (positional args), `%q<reg>` (registers)
- Functions: 400+ built-in functions (string, math, list, DB, logic, etc.)
- Side-effect functions: `set()`, `create()`, etc. mutate game state
- Commands: `@set`, `@create`, `@dig`, `@tel`, etc. — hardcode entry points
- Escaping: `\` escapes next char, `{...}` groups (suppresses comma-separation)
- Flags on functions: NoParse (don't pre-evaluate args), Literal, StripAnsi

### SharpMUSH Architecture
- C# (.NET 9), ANTLR4 parser (grammar → parse tree → visitor evaluates)
- Grammar = structure only (SharpMUSHParser.g4 / SharpMUSHLexer.g4)
- `SharpMUSHParserVisitor.cs` = semantics (~2200 lines)
- Functions in `SharpMUSH.Implementation/Functions/` (22 files)
- Commands in `SharpMUSH.Implementation/Commands/` (30+ files)
- MarkupString library (MModule) = ANSI-aware string ops
- TUnit test framework (3425 tests passing)
- PennMUSH oracle at `pennmush/` for live validation (31 .t test files)

---

## Phase 0: Immediate Cleanup (ErrorMessages Consolidation)

### 0A. Migrate Errors.cs → ErrorMessages.Returns

**Problem**: Two sources of truth — `Errors.cs` (144 constants, 696 references) and `ErrorMessages.Returns` (74 constants, 85 references). 87 constants exist only in Errors.cs.

**Steps**:
1. Add 87 missing constants to `ErrorMessages.Returns` with clean names (drop `Error` prefix)
2. Build mapping: `Errors.ErrorFoo` → `ErrorMessages.Returns.Foo`
3. Replace all 696 `Errors.X` references across 40 files with `ErrorMessages.Returns.Y`
4. Delete `Errors.cs`
5. Run full test suite — expect 3425 passing (string values identical)

**Files changed**: ~40 .cs files + Errors.cs deleted

### 0B. Remaining Magic Strings in Commands/

**Problem**: ~100+ inline `"#-1 ..."` strings in Commands/ that should use `ErrorMessages.Returns` constants.

**Steps**:
1. `grep -rn '"#-1\|"#-2' Commands/` to find all
2. Add any missing constants to `ErrorMessages.Returns`
3. Replace inline strings with constant references
4. Run tests

---

## Phase 1: Function Parity Audit

### Strategy
Use PennMUSH's own test files (`pennmush/test/*.t`) as ground truth. For each function:
1. Run test against live PennMUSH oracle
2. Run equivalent test in SharpMUSH
3. Compare outputs — any difference = bug

### 1A. Generate Comprehensive Test Coverage from PennMUSH .t Files

31 test files exist covering: alias, atree, decompose, digest, distxd, firstof, flags, gaps (3), grep, hastype, insert, json, just, letq, lnum, math, null, page, penntext, pueblo, quote, regmatch, sort, sortby, sql, switch, table, textentries, time, wenv.

**Steps**:
1. Parse each .t file extracting test cases: `test('name', $who, 'command', 'expected')`
2. Generate SharpMUSH TUnit test methods from them (already have `GeneratedFunctionTests.cs` pattern)
3. Run all — failures = parity gaps
4. Fix each gap in visitor/function code

### 1B. Systematic Function-by-Function Audit

PennMUSH has ~400+ functions. SharpMUSH implements a subset. For each:

| Category | PennMUSH Source | SharpMUSH Source |
|----------|----------------|------------------|
| String | src/funstr.c | StringFunctions.cs |
| Math | src/funmath.c | MathFunctions.cs |
| List | src/funlist.c | ListFunctions.cs |
| DB/Object | src/fundb.c | DbrefFunctions.cs, InformationFunctions.cs |
| Boolean | src/funmisc.c | BooleanFunctions.cs |
| Time | src/funtime.c | TimeFunctions.cs |
| Communication | src/speech.c | CommunicationFunctions.cs |
| Regex | src/funstr.c | RegExFunctions.cs |
| JSON | src/json.c | JSONFunctions.cs |
| Bitwise | src/funmath.c | BitwiseFunctions.cs |
| HTML/ANSI | src/funstr.c | HTMLFunctions.cs |
| Attributes | src/fundb.c | AttributeFunctions.cs |
| SQL | src/sql.c | SQLFunctions.cs |
| Channels | src/extchat.c | ChannelFunctions.cs |
| Mail | src/malias.c | MailFunctions.cs |
| Connection | src/bsd.c | ConnectionFunctions.cs |

**Steps per function**:
1. Read PennMUSH C source for exact behavior (edge cases, error conditions, arg counts)
2. Compare with SharpMUSH implementation
3. Write failing test for any discrepancy
4. Fix implementation
5. Verify test passes

### 1C. Missing Functions

Identify functions in PennMUSH's function table that have no SharpMUSH implementation at all. Prioritize by usage frequency in real MUSHcode.

---

## Phase 2: Parser/Evaluator Parity

### 2A. PE_COMPRESS_SPACES Behavior (DONE — verified)
- Literal text nodes compress spaces
- Function return values do NOT compress
- PennMUSH test harness strips trailing whitespace (not the evaluator)

### 2B. Escaping and Grouping Edge Cases
- `\` before any character
- `{}` grouping (suppresses comma-splitting in args)
- Nested `[]` evaluation
- `%` substitutions in all contexts
- Interaction of NoParse flag with evaluation

**Validation**: Write edge-case tests from PennMUSH source comments and known MUSH lore.

### 2C. Function Flag Behavior
- `NoParse`: Args passed unevaluated (iter, switch, etc.)
- `Literal`: Some special handling
- `StripAnsi`: Auto-strip ANSI from args
- Verify each flag is respected identically to PennMUSH

### 2D. Argument Counting and Error Messages
- PennMUSH format: `#-1 FUNCTION (FOO) EXPECTS AT LEAST N ARGUMENTS BUT GOT M`
- Verify all min/max arg checks match PennMUSH's function table
- Check `varargs` vs fixed arg functions

---

## Phase 3: Command Parity

### 3A. Core Commands
Verify each @-command produces identical output/behavior:
- `@set`, `@create`, `@dig`, `@open`, `@link`, `@unlink`
- `@destroy`, `@nuke`, `@undestroy`
- `@name`, `@describe`, `@lock`, `@unlock`
- `@teleport`, `@force`, `@trigger`
- `@emit`, `@pemit`, `@oemit`, `say`, `pose`
- `@switch`, `@select`, `@dolist`, `@wait`
- `@mail`, `@channel`/`+channel`

### 3B. Switch Handling
- Command switches (`@set/quiet`, `@pemit/list`, etc.)
- Invalid switch errors
- Switch combinations

---

## Phase 4: Edge Cases and Compatibility Matrix

### 4A. Known Deliberate Incompatibilities (Document & Test)
1. Mid-string function recognition (SharpMUSH added)
2. [3 others from earlier work — verify still valid]
3. SharpMUSH-only features (Markdown functions, etc.)

### 4B. Numeric Precision
- PennMUSH uses C `double` — SharpMUSH uses .NET `double`
- Verify formatting: trailing zeros, scientific notation thresholds
- `div()` vs `fdiv()` behavior

### 4C. ANSI/Markup Handling
- `ansi()` function output
- `stripansi()` behavior
- ANSI-aware string operations (mid, left, right, strlen with ANSI)
- MarkupString library correctness

### 4D. Database Object Interactions
- `loc()`, `owner()`, `zone()`, `parent()` chains
- Attribute inheritance via parent
- Permission checks (See_All, Pemit_All, etc.)

---

## Phase 5: Performance Optimization

(After parity achieved)
- Profile hot paths in visitor
- MarkupString allocation reduction
- Function dispatch optimization
- Parser cache / memoization

---

## Phase 6: New Features

(After parity + performance)
- Features SharpMUSH adds beyond PennMUSH
- Markdown rendering
- Extended JSON support
- Modern transport (WebSocket)

---

## Validation Strategy

### Automated Oracle Testing
```
For each test case:
  1. Send command to PennMUSH (via pennmush/test/runtest.pl)
  2. Send same command to SharpMUSH evaluator
  3. Compare outputs (regex match where PennMUSH uses ^...$)
  4. Any mismatch = regression
```

### Continuous Integration
- All 3425+ TUnit tests must pass on every commit
- New parity tests added per function/command fixed
- PennMUSH .t files as upstream test source

### Test File Organization
- `SharpMUSH.Tests/Functions/` — unit tests per function category
- `SharpMUSH.Tests/Commands/` — command tests
- Split large test files to avoid TUnit source generator limits

---

## Risks & Tradeoffs

1. **PennMUSH undocumented behavior**: Some edge cases only discoverable by reading C source or testing live. Mitigation: oracle testing.
2. **Version drift**: PennMUSH continues development. Pin to a specific version for parity target.
3. **Performance vs fidelity**: Some PennMUSH behaviors are C-specific (buffer sizes, integer overflow). Document where .NET differs intentionally.
4. **Test coverage gaps**: 31 .t files don't cover all 400+ functions. Need supplementary tests.

---

## Immediate Next Steps (Priority Order)

1. **Phase 0A**: Consolidate Errors.cs → ErrorMessages.Returns (mechanical, unblocks clean code)
2. **Phase 1A**: Parse all 31 .t files → generate SharpMUSH tests → identify failures
3. **Phase 1B**: Start function audit with highest-coverage categories (String, Math, List)
4. **Phase 2B-2D**: Evaluator edge cases (escaping, grouping, flags)

---

## Files Likely to Change

### Phase 0
- `SharpMUSH.Library/Definitions/Errors.cs` (DELETE)
- `SharpMUSH.Library/Definitions/ErrorMessages.cs` (add 87 constants)
- ~40 files referencing `Errors.*`

### Phase 1-2
- All `SharpMUSH.Implementation/Functions/*.cs` (fixes)
- `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs` (evaluator fixes)
- `SharpMUSH.Tests/Functions/*.cs` (new/updated tests)

### Phase 3
- All `SharpMUSH.Implementation/Commands/*.cs` (fixes)
- `SharpMUSH.Tests/Commands/*.cs` (new tests)
