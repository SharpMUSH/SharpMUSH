# PennMUSH 100% Behavioral Alignment Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Achieve 100% behavioral alignment between SharpMUSH and PennMUSH — every softcode expression, command, and function should produce identical output.

**Architecture:** SharpMUSH is a C#/.NET 10 reimplementation of PennMUSH's C codebase. It uses ANTLR4 for softcode parsing (vs PennMUSH's hand-rolled recursive descent parser), a graph database for object storage (vs PennMUSH's flat array), and NATS messaging for connection handling (vs PennMUSH's select() loop). Alignment work focuses on semantic behavior, not internal architecture.

**Tech Stack:** C# / .NET 10 / ANTLR4 / xUnit / PennMUSH C source as reference

**Current State Summary:**
- Commands: 100% coverage (126/126 PennMUSH @-commands implemented)
- Functions: ~75-85% coverage (546 registrations vs PennMUSH's 332, but some may be stubs)
- Parser: Substantial but has 10 identified gaps in evaluation semantics
- Tests: Existing unit + integration test suites

---

## Phase 1: Parser & Evaluation Engine Alignment

These are the highest-priority items because parser bugs affect ALL softcode.

---

### Task 1.1: Add PE_LITERAL Evaluation Mode

**Objective:** Implement a true literal mode where ALL parsing (including %-substitutions) is suppressed, matching PennMUSH's `PE_LITERAL` flag used by `lit()`.

**Files:**
- Modify: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs`
- Modify: `SharpMUSH.Library/ParseMode.cs` (or equivalent enum/flags)
- Test: `SharpMUSH.Tests/` (new test file or extend existing parser tests)

**Steps:**
1. In PennMUSH, read `src/parse.c` search for `PE_LITERAL` to understand exact behavior
2. Add a `Literal` flag to SharpMUSH's parse mode enum
3. In the visitor, when Literal mode is active, return all text as-is without visiting substitution children
4. Ensure `lit()` function sets this mode
5. Write tests: `lit(%#)` should return literal `%#`, `lit([add(1,2)])` should return literal `[add(1,2)]`
6. Commit: `feat(parser): add PE_LITERAL evaluation mode`

---

### Task 1.2: Add PE_COMPRESS_SPACES Support

**Objective:** Support PennMUSH's space compression during evaluation (multiple spaces collapsed to one).

**Files:**
- Modify: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs`
- Modify: Parse mode flags
- Test: Parser tests

**Steps:**
1. Study PennMUSH `PE_COMPRESS_SPACES` usage in `src/parse.c`
2. Add `CompressSpaces` flag to parse mode
3. Post-process evaluation results to collapse runs of spaces when flag is set
4. Write tests comparing output with and without compression
5. Commit: `feat(parser): add PE_COMPRESS_SPACES support`

---

### Task 1.3: Fix Command-Level Brace Semantics (PE_COMMAND_BRACES)

**Objective:** Ensure the first `{...}` on a command line is stripped and fully evaluated, matching PennMUSH's `PE_COMMAND_BRACES` vs `PE_STRIP_BRACES` distinction.

**Files:**
- Modify: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs`
- Modify: Grammar if needed (`SharpMUSHParser.g4`)
- Test: Parser tests

**Steps:**
1. Study PennMUSH's brace handling in `src/parse.c` — search for `PE_COMMAND_BRACES`
2. Trace how SharpMUSH currently handles `{...}` at command level vs function arg level
3. Identify and fix any divergence
4. Test: `think {[add(1,2)]}` should evaluate to `3`, `u(obj/attr,{[add(1,2)]})` should pass `{[add(1,2)]}` literally (or per PennMUSH behavior)
5. Commit: `fix(parser): correct command-level brace semantics`

---

### Task 1.4: Fix Bracket-Expression Error Behavior (PE_FUNCTION_MANDATORY)

**Objective:** When `[notafunction(args)]` is evaluated, produce `#-1 FUNCTION (NOTAFUNCTION) NOT FOUND` matching PennMUSH.

**Files:**
- Modify: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs`
- Test: Parser/function tests

**Steps:**
1. Check PennMUSH behavior: `[nonexistent()]` → error message
2. Check SharpMUSH current behavior for unknown functions in brackets
3. Fix to match PennMUSH error output exactly
4. Test with various malformed/unknown function calls in brackets
5. Commit: `fix(parser): match PennMUSH error for unknown functions in brackets`

---

### Task 1.5: Add PE_BUILTINONLY Flag

**Objective:** Support restricting evaluation to only built-in functions (not user-defined @functions).

**Files:**
- Modify: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs`
- Modify: Parse mode flags
- Test: Parser tests

**Steps:**
1. Study where PennMUSH uses `PE_BUILTINONLY`
2. Add flag and check during function dispatch
3. When set, skip user-defined function lookup
4. Write tests
5. Commit: `feat(parser): add PE_BUILTINONLY evaluation mode`

---

### Task 1.6: Add PE_USERFN Tracking Flag

**Objective:** Track when evaluation is inside a user-defined function, matching PennMUSH's `PE_USERFN` behavior.

**Files:**
- Modify: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs`
- Modify: Parse mode flags / evaluation context
- Test: Function tests

**Steps:**
1. Study PennMUSH's `PE_USERFN` — what behaviors change when inside a ufun?
2. Add flag to SharpMUSH evaluation context
3. Set when entering u()/ufun() calls, clear on exit
4. Apply any behavioral differences
5. Commit: `feat(parser): add PE_USERFN tracking`

---

### Task 1.7: Fix %? Substitution to Return Both Values

**Objective:** `%?` should return `fun_invocations fun_recursions` (two space-separated numbers) matching PennMUSH.

**Files:**
- Modify: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs` (substitution handling)
- Test: Substitution tests

**Steps:**
1. Verify PennMUSH behavior: `think %?` → `<invocations> <recursions>`
2. Check SharpMUSH's current `%?` output
3. Fix to return both values space-separated
4. Commit: `fix(parser): %? returns both invocation and recursion counts`

---

### Task 1.8: Fix %<space> and Uppercase Substitution Capitalization

**Objective:** Ensure `% ` returns literal `% ` and uppercase substitutions like `%N` capitalize the first letter of the result.

**Files:**
- Modify: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs`
- Modify: Lexer rules if needed
- Test: Substitution tests

**Steps:**
1. Verify PennMUSH: `% ` → `% `, `%N` → capitalized enactor name, `%n` → lowercase
2. Check SharpMUSH behavior for both
3. Fix any divergence
4. Test all uppercase vs lowercase substitution pairs
5. Commit: `fix(parser): correct %<space> and uppercase substitution capitalization`

---

### Task 1.9: Add $-Substitution Support (Regexp Captures)

**Objective:** Support bare `$0`-`$9` and `$<name>` for regexp capture group substitution, used by `regedit()`/`regsub()`.

**Files:**
- Modify: `SharpMUSHLexer.g4` and `SharpMUSHParser.g4` if bare `$` needs grammar support
- Modify: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs`
- Modify: `SharpMUSH.Implementation/Functions/RegExFunctions.cs`
- Test: RegEx function tests

**Steps:**
1. Study PennMUSH `PE_DOLLAR` in parse.c — when is `$N` recognized?
2. Determine if SharpMUSH already handles this via `%$0` etc. and if bare `$0` is also needed
3. Implement as needed
4. Test: `regedit(hello world, (hello), $0 cruel)` → `hello cruel world`
5. Commit: `feat(parser): add $-substitution for regexp captures`

---

## Phase 2: Function Parity

---

### Task 2.1: Audit and Implement Missing Crypto Functions

**Objective:** Implement PennMUSH's `funcrypt.c` functions: `encrypt()`, `decrypt()`, `sha0()`, `sha1()`, `digest()`, `checksum()`, `hmac()`, `bcrypt()`.

**Files:**
- Modify: `SharpMUSH.Implementation/Functions/UtilityFunctions.cs` (or create CryptoFunctions.cs)
- Reference: `pennmush/src/funcrypt.c`
- Test: New crypto function tests

**Steps:**
1. List all functions in PennMUSH's `funcrypt.c`
2. Check which exist in SharpMUSH (search for `encrypt`, `decrypt`, `sha`, `digest`, `hmac`, `bcrypt`)
3. Implement missing ones using .NET crypto libraries
4. Match PennMUSH output format exactly (hex encoding, case, etc.)
5. Commit: `feat(functions): implement crypto functions from funcrypt.c`

---

### Task 2.2: Audit and Complete User-Defined Function Support (funufun.c)

**Objective:** Ensure full parity with PennMUSH's `u()`, `ulocal()`, `ufun()`, `udefault()`, `uldefault()`, `ulambda()`, `zfun()`, `objeval()`.

**Files:**
- Modify: `SharpMUSH.Implementation/Functions/UtilityFunctions.cs` (or relevant file)
- Reference: `pennmush/src/funufun.c`
- Test: User function tests

**Steps:**
1. List all 8 functions from PennMUSH's `funufun.c`
2. Verify each exists and produces correct output in SharpMUSH
3. Pay special attention to: register scoping in `ulocal()` vs `u()`, lambda evaluation in `ulambda()`, default-value semantics in `udefault()`/`uldefault()`
4. Fix any behavioral divergence
5. Commit: `feat(functions): complete user-defined function support`

---

### Task 2.3: Stub Audit — Find and Implement NotImplemented Functions

**Objective:** Find all SharpMUSH functions that throw `NotImplementedException` and implement them.

**Files:**
- All files in `SharpMUSH.Implementation/Functions/`
- Test: Per-function tests

**Steps:**
1. Search all function files for `NotImplemented`, `throw`, `TODO`, `FIXME`
2. Prioritize by usage frequency (common functions first)
3. Implement each using PennMUSH source as reference
4. Batch into logical groups (string stubs, math stubs, etc.)
5. Commit per batch: `feat(functions): implement <category> function stubs`

---

### Task 2.4: Cross-Reference Function-by-Function Output Parity

**Objective:** For every PennMUSH function, verify SharpMUSH produces identical output for the same inputs.

**Files:**
- Create: `SharpMUSH.Tests/FunctionParityTests/` (new test directory)
- Reference: PennMUSH help files in `pennmush/game/txt/hlp/`

**Steps:**
1. Extract PennMUSH function list from `src/function.c` (the FUNTAB array)
2. For each function, create a test case with known input/output from PennMUSH docs
3. Group by category file (funstr, funmath, funlist, fundb, funmisc, funtime, funcrypt, funufun, funjson)
4. Run tests, fix failures
5. Commit: `test: add function parity tests against PennMUSH`

---

## Phase 3: Command Behavioral Parity

Commands are 100% present but may have behavioral differences.

---

### Task 3.1: @set Behavioral Parity

**Objective:** Verify all `@set` variants match PennMUSH: flag setting, attribute setting, attribute clearing, /quiet switch.

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/BuildingCommands.cs`
- Reference: `pennmush/src/set.c`
- Test: Command tests

**Steps:**
1. Test all `@set` forms: `@set obj=FLAG`, `@set obj=!FLAG`, `@set obj/attr=value`, `@set obj/attr=` (clear)
2. Compare output messages with PennMUSH
3. Fix any message text or behavior differences
4. Commit: `fix(commands): @set behavioral parity`

---

### Task 3.2: @lock Behavioral Parity

**Objective:** Verify all lock types and boolean expression evaluation match PennMUSH exactly.

**Files:**
- Modify: `SharpMUSH.Implementation/` (boolean expression evaluator)
- Reference: `pennmush/src/boolexp.c`, `pennmush/src/lock.c`
- Test: Lock tests

**Steps:**
1. Test all lock types: `@lock obj=key`, `@lock/use`, `@lock/enter`, `@lock/command`, `@lock/listen`, etc.
2. Test boolean expressions: `obj1&obj2`, `obj1|obj2`, `!obj`, `=flag`, `+power`, `@type`, `attr:pattern`
3. Compare lock evaluation results with PennMUSH
4. Fix divergence
5. Commit: `fix(commands): @lock and boolexp parity`

---

### Task 3.3: Command Queue and @wait/@trigger Parity

**Objective:** Verify command queuing, @wait timing, @trigger behavior, and semaphore handling match PennMUSH.

**Files:**
- Modify: Queue handling in SharpMUSH.Implementation
- Reference: `pennmush/src/cque.c`
- Test: Queue/timing tests

**Steps:**
1. Test `@wait 5=action`, `@wait obj/attr=action` (semaphore), `@trigger obj/attr`
2. Verify executor/enactor/caller propagation through the queue
3. Test `@notify`, `@drain`, `@halt`
4. Compare behavior with PennMUSH
5. Commit: `fix(commands): queue and @wait/@trigger parity`

---

### Task 3.4: Movement and Exit Matching Parity

**Objective:** Verify exit matching, @teleport, and movement messages match PennMUSH.

**Files:**
- Modify: Movement handling commands
- Reference: `pennmush/src/move.c`
- Test: Movement tests

**Steps:**
1. Test exit matching: partial name matching, ambiguous exit handling, locked exits
2. Test `@teleport` with all variants and permission checks
3. Test ODROP/OSUCC/OFAIL/DROP/SUCC/FAIL messages
4. Compare with PennMUSH output
5. Commit: `fix(commands): movement and exit matching parity`

---

### Task 3.5: @command Hooks and Mogrifiers Parity

**Objective:** Verify command hooks (@hook) and output mogrifiers match PennMUSH behavior.

**Files:**
- Modify: Hook/mogrifier implementation
- Reference: `pennmush/src/command.c` (hook handling)
- Test: Hook tests

**Steps:**
1. Test `@hook/before`, `@hook/after`, `@hook/override`, `@hook/ignore`
2. Test mogrifier behavior on output
3. Compare with PennMUSH
4. Commit: `fix(commands): hook and mogrifier parity`

---

### Task 3.6: Output Message Text Parity

**Objective:** Every success/failure/error message from every command should match PennMUSH's exact wording.

**Files:**
- All command files in `SharpMUSH.Implementation/Commands/`
- Reference: PennMUSH source files

**Steps:**
1. Extract all user-visible strings from PennMUSH commands (grep for `notify`, `notify_format`)
2. Compare with SharpMUSH's output strings
3. Fix any wording differences
4. This is tedious but critical for softcode that pattern-matches on output
5. Commit: `fix(commands): align all output message text with PennMUSH`

---

## Phase 4: Object Model & Permission Parity

---

### Task 4.1: Flag and Power Parity

**Objective:** Ensure all PennMUSH flags and powers exist in SharpMUSH with identical names and behaviors.

**Files:**
- Modify: Flag/power definitions
- Reference: `pennmush/src/flags.c`, `pennmush/hdrs/flags.h`
- Test: Flag tests

**Steps:**
1. Extract complete flag list from PennMUSH
2. Compare with SharpMUSH's flag definitions
3. Add any missing flags
4. Verify flag behaviors (WIZARD, ROYALTY, INHERIT, TRUST, VISUAL, etc.)
5. Commit: `feat(objects): complete flag and power parity`

---

### Task 4.2: Attribute System Parity

**Objective:** Verify attribute inheritance (parent chain), default attributes, attribute flags, and attribute access controls match PennMUSH.

**Files:**
- Modify: Attribute handling
- Reference: `pennmush/src/attrib.c`
- Test: Attribute tests

**Steps:**
1. Test parent chain inheritance: child reads parent's attribute
2. Test attribute flags: no_command, no_inherit, visual, wizard, etc.
3. Test @atrlock behavior
4. Test standard attributes (DESCRIBE, LISTEN, AHEAR, ACONNECT, etc.)
5. Commit: `fix(objects): attribute system parity`

---

### Task 4.3: Zone and Permission Model Parity

**Objective:** Verify zone-based permissions and the full permission hierarchy match PennMUSH.

**Files:**
- Modify: Permission checking code
- Reference: `pennmush/hdrs/mushdb.h`, `pennmush/src/privtab.c`
- Test: Permission tests

**Steps:**
1. Test zone master objects and zone-based command matching
2. Test permission hierarchy: God > Wizard > Royalty > owner > others
3. Test TRUST/INHERIT flag interactions
4. Test @power assignments and checks
5. Commit: `fix(objects): zone and permission model parity`

---

### Task 4.4: Economy and Quota System

**Objective:** Implement the economy (pennies) and quota system if not already present.

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/BuildingCommands.cs` (noted as incomplete)
- Reference: `pennmush/src/rob.c`, `pennmush/src/create.c`
- Test: Economy tests

**Steps:**
1. Check if SharpMUSH has penny tracking and quota enforcement
2. Implement costs for @create, @dig, @open, @clone
3. Implement give/rob commands fully
4. Implement quota tracking and @quota command
5. Commit: `feat(objects): implement economy and quota system`

---

## Phase 5: Networking & Connection Parity

---

### Task 5.1: SSL/TLS Support

**Objective:** Add TLS support to the connection server if not present.

**Files:**
- Modify: `SharpMUSH.ConnectionServer/`
- Test: Connection tests

**Steps:**
1. Check current TLS status in SharpMUSH.ConnectionServer
2. Add TLS listener configuration
3. Test encrypted connections
4. Commit: `feat(network): add TLS support`

---

### Task 5.2: Connection Screen and MOTD Parity

**Objective:** Match PennMUSH's connection screen, MOTD, and login flow.

**Files:**
- Modify: Connection handling
- Reference: `pennmush/src/bsd.c` (connection handling)
- Test: Connection tests

**Steps:**
1. Compare login flow: connect screen → connect/create → MOTD → game
2. Match disconnect messages and timeouts
3. Verify WHO/DOING output format
4. Commit: `fix(network): connection screen and login flow parity`

---

## Phase 6: Comprehensive Integration Testing

---

### Task 6.1: Port PennMUSH Test Suite

**Objective:** Adapt PennMUSH's test suite (in `pennmush/test/`) to run against SharpMUSH.

**Files:**
- Create: Test adapter in `SharpMUSH.IntegrationTests/PennMUSHTests/`
- Reference: `pennmush/test/`

**Steps:**
1. Examine PennMUSH's test format and runner
2. Convert test cases to SharpMUSH's xUnit format
3. Run and catalog failures
4. Use failures as a prioritized bug list
5. Commit: `test: port PennMUSH test suite`

---

### Task 6.2: Softcode Regression Suite

**Objective:** Build a comprehensive softcode regression suite testing real-world MUSHcode patterns.

**Files:**
- Create: `SharpMUSH.IntegrationTests/SoftcodeRegression/`

**Steps:**
1. Collect common softcode patterns from PennMUSH community (BBS systems, combat code, building tools)
2. Test each pattern produces identical output in SharpMUSH
3. Focus on: nested evaluations, complex u() chains, regexp handling, list manipulation
4. Commit: `test: add softcode regression suite`

---

### Task 6.3: PennMUSH Database Import Validation

**Objective:** Import a PennMUSH database and verify all objects, attributes, flags, and locks are preserved.

**Files:**
- Modify: `SharpMUSH.Configuration/PennMUSHDatabaseParser.cs` if bugs found
- Test: Import validation tests

**Steps:**
1. Create a comprehensive test PennMUSH database with diverse objects
2. Import into SharpMUSH
3. Verify every object: type, flags, attributes, locks, parent, zone, owner
4. Run softcode on imported objects to verify it works
5. Commit: `test: PennMUSH database import validation`

---

## Priority Order

Execute phases in this order for maximum impact:

1. **Phase 1 (Parser)** — Parser bugs cascade everywhere. Fix these first.
2. **Phase 6.1 (Port PennMUSH tests)** — Gives us a concrete failure list.
3. **Phase 2 (Functions)** — Functions are the most-used softcode feature.
4. **Phase 3 (Command behavior)** — Commands with wrong output break user expectations.
5. **Phase 4 (Object model)** — Permissions/zones are critical for multi-user environments.
6. **Phase 5 (Networking)** — Important but least likely to break softcode.
7. **Phase 6.2-6.3 (Remaining tests)** — Ongoing validation.

---

## Verification Strategy

For each task:
1. Write a failing test that demonstrates the PennMUSH-expected behavior
2. Fix the code to make the test pass
3. Run the full test suite to ensure no regressions
4. Document what changed and why

**Done criteria:** All PennMUSH tests pass, all function parity tests pass, all softcode regression tests pass, and a real PennMUSH database can be imported and operated identically.
