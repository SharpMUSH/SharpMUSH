# PennMUSH 100% Behavioral Alignment Plan (Revised)

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Achieve 100% behavioral alignment between SharpMUSH and PennMUSH — every softcode expression, command, and function should produce identical output (or closest ANTLR4-achievable equivalent).

**Architecture:** SharpMUSH uses ANTLR4 for parsing (vs PennMUSH's hand-rolled recursive descent). The grammar handles structural parsing only; all semantic behavior lives in the visitor layer. ANTLR4's context-free nature means some PennMUSH behaviors that rely on mid-parse mode switching are handled via semantic predicates in the grammar and state tracking in the visitor. This is a deliberate design choice — "closest equivalent" rather than exact replication.

**Tech Stack:** C# / .NET 10 / ANTLR4 / xUnit

**Current State (Revised):**
- Commands: 100% present (126/126 + 2 extras)
- Functions: ~75-85% (546 registrations, some stubs)
- Parser: Solid ANTLR4 grammar with 6 semantic predicates, 3 lexer modes, and extensive visitor-level workarounds. Most PennMUSH parse behaviors ARE handled.
- Tests: ~2,232 tests (757 function, 568 command, 283 markup, 238 service, 93 parser). 192 skipped. PennMUSH's 28 .t test files exist in pennmush/test/ but are NOT integrated into C# test suite.
- Key ANTLR4 workarounds already in place: _suppressFunctionEval for brace semantics, IsInsideFunctionArg() tree-walk, multiple start rules for command arg parsing, deferred evaluation lambdas for NoParse functions, bracket re-enabling function eval inside braces.

---

## ANTLR4 Constraints (Context for All Tasks)

These PennMUSH behaviors CANNOT be replicated in the ANTLR4 grammar and are handled at the visitor level:

1. **Context-sensitive delimiters** — commas/semicolons change meaning based on depth. Handled via 6 semantic predicates with member variable tracking (inFunction, inBraceDepth, etc.).
2. **Two brace modes** — command braces vs function-arg braces. Handled via _suppressFunctionEval + IsInsideFunctionArg().
3. **Mid-parse evaluation toggle** — [...] re-enables functions inside braces. Handled by saving/restoring _suppressFunctionEval in VisitBracketPattern.
4. **Dynamic command arg splitting** — handled via multiple start rules and re-parsing.
5. **Deferred evaluation** — NoParse functions get closures via CreateDeferredEvaluation().

Any new Phase 1 work should follow the same pattern: grammar stays structural, behavior goes in the visitor.

---

## Phase 1: Parser & Evaluation Engine Gaps

These are ranked by actual impact. Items already partially handled are noted.

---

### Task 1.1: Implement FunctionFlags.Literal Mode

**Objective:** The `FunctionFlags.Literal` flag (1 << 1) is defined in FunctionFlags.cs but NEVER referenced in the visitor. Implement it so functions like `lit()` can suppress ALL evaluation including %-substitutions.

**Current state:** FunctionFlags.Literal exists but is unused. NoParse mode still allows bracket evaluation and has no full-literal path.

**Files:**
- Modify: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs` — check for Literal flag in function arg handling
- Modify: Any function registered with Literal flag (find with grep)
- Test: `SharpMUSH.Tests/Functions/` or `SharpMUSH.Tests/Parser/`

**Steps:**
1. Search codebase for `Literal` flag usage — confirm it's truly unused
2. In the visitor's function argument handling, when `Literal` flag is set, return raw source text with NO evaluation (no %-subs, no [...], no function calls)
3. Verify `lit()` function (if it exists) uses this flag; if lit() doesn't exist, create it
4. Test: `lit(%#)` → `%#`, `lit([add(1,2)])` → `[add(1,2)]`
5. Commit: `feat(parser): implement FunctionFlags.Literal evaluation mode`

---

### Task 1.2: Add PE_COMPRESS_SPACES Support

**Objective:** PennMUSH's `PE_COMPRESS_SPACES` collapses multiple spaces to one during evaluation. No equivalent exists in SharpMUSH.

**Current state:** Zero implementation. Not in grammar, not in visitor, not in ParserState flags.

**Files:**
- Modify: `SharpMUSH.Library/ParserInterfaces/ParserState.cs` — add flag
- Modify: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs` — post-process
- Test: Parser tests

**Steps:**
1. Study PennMUSH: where is PE_COMPRESS_SPACES used? (likely in say/pose/emit output)
2. Add `CompressSpaces` to ParserStateFlags
3. When flag is set, collapse space runs in evaluation output
4. Apply the flag wherever PennMUSH uses PE_COMPRESS_SPACES
5. Test: evaluation with flag produces compressed spaces
6. Commit: `feat(parser): add PE_COMPRESS_SPACES support`

---

### Task 1.3: Fix %? Substitution — Return Invocations AND Recursions

**Objective:** PennMUSH's `%?` returns `<invocations> <recursions>` (two numbers). SharpMUSH returns `parser.State.Count()` (stack depth only — one number).

**Current state:** Substitutions.cs line for %? returns `parser.State.Count().ToString()` — only invocation depth.

**Files:**
- Modify: `SharpMUSH.Implementation/Substitutions/Substitutions.cs`
- Possibly modify: ParserState to track recursion count separately
- Test: Substitution tests

**Steps:**
1. Verify PennMUSH: `%?` → `<fun_invocations> <fun_recursions>` (space-separated)
2. Add recursion tracking to ParserState if not present
3. Change %? output to return both values space-separated
4. Test: `think %?` outputs two numbers
5. Commit: `fix(parser): %? returns both invocation and recursion counts`

---

### Task 1.4: Verify Uppercase Substitution Capitalization

**Objective:** Confirm %N capitalizes first letter, %S/%O/%P/%A capitalize appropriately.

**Current state:** Substitutions.cs handles %N with sentence casing. Need to verify ALL uppercase variants are covered.

**Files:**
- Modify: `SharpMUSH.Implementation/Substitutions/Substitutions.cs` (if gaps found)
- Test: Substitution tests

**Steps:**
1. List all uppercase/lowercase substitution pairs in PennMUSH
2. Verify each pair in SharpMUSH's Substitutions.cs
3. Test each pair produces correct casing
4. Commit: `fix(parser): verify/fix uppercase substitution capitalization`

---

### Task 1.5: Add PE_BUILTINONLY Flag

**Objective:** Allow restricting evaluation to built-in functions only (skip @function user-defined functions).

**Current state:** Not implemented. CallFunction() checks FunctionLibrary which contains both built-in and user-defined functions with no distinction.

**Files:**
- Modify: `SharpMUSH.Library/ParserInterfaces/ParserState.cs` — add flag
- Modify: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs` — check flag in CallFunction
- Modify: Function registration to tag built-in vs user-defined
- Test: Function tests

**Steps:**
1. Study where PennMUSH uses PE_BUILTINONLY
2. Tag functions as built-in vs user-defined in FunctionLibrary
3. When flag is set, skip user-defined functions in lookup
4. Test: user-defined function not found when flag active
5. Commit: `feat(parser): add PE_BUILTINONLY evaluation mode`

---

### Task 1.6: Add PE_USERFN Context Tracking

**Objective:** Track when evaluation is inside a user-defined function call, matching PennMUSH.

**Current state:** Not tracked. No equivalent flag in ParserState.

**Files:**
- Modify: `SharpMUSH.Library/ParserInterfaces/ParserState.cs`
- Modify: `SharpMUSH.Implementation/Functions/UtilityFunctions.cs` (u/ulocal handlers)
- Test: Function tests

**Steps:**
1. Study PennMUSH: what behaviors change under PE_USERFN?
2. Add flag to ParserState, set when entering u()/ulocal()/ufun()
3. Apply any behavioral differences gated on this flag
4. Test affected behaviors
5. Commit: `feat(parser): add PE_USERFN context tracking`

---

### Task 1.7: Q-Register Handling in NoParse/NoEval Mode

**Objective:** Address the TODO at visitor line 1930: "Handle Q-registers containing evaluation strings properly. In NoParse/NoEval mode, Q-registers with unevaluated code should still be processed."

**Current state:** Explicitly marked as TODO in the visitor.

**Files:**
- Modify: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs` (~line 1930)
- Test: Register tests in `SharpMUSH.Tests/Substitutions/`

**Steps:**
1. Study PennMUSH: how are Q-registers handled in NoParse mode?
2. Implement the correct behavior per the TODO
3. Add tests covering Q-register access in NoParse context
4. Commit: `fix(parser): handle Q-registers in NoParse/NoEval mode`

---

### Task 1.8: Implement lsargs (List-Style Arguments) Support

**Objective:** Address the TODO at visitor line 1734: "Implement lsargs (list-style arguments) support."

**Current state:** Explicitly marked as TODO. "No immediate commands require this feature yet" — but for full PennMUSH parity it's needed.

**Files:**
- Modify: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs` (~line 1734)
- Test: Command argument tests

**Steps:**
1. Study PennMUSH lsargs — how do list-style arguments differ from regular args?
2. Implement in the visitor's argument splitting logic
3. Add tests
4. Commit: `feat(parser): implement lsargs list-style arguments`

---

## Phase 2: Function Parity

---

### Task 2.1: Audit Stubs — Find All NotImplemented/TODO Functions

**Objective:** Find every function that throws NotImplementedException or has TODO markers and implement them.

**Files:**
- All files in `SharpMUSH.Implementation/Functions/`

**Steps:**
1. `grep -rn "NotImplemented\|throw.*NotImpl\|TODO\|FIXME" SharpMUSH.Implementation/Functions/`
2. List each stub with its function name and file
3. Implement each using PennMUSH source as reference
4. Prioritize by usage frequency
5. Commit per batch

---

### Task 2.2: Audit Crypto Functions (funcrypt.c Parity)

**Objective:** Verify/implement `encrypt()`, `decrypt()`, `sha0()`, `sha1()`, `digest()`, `checksum()`, `hmac()`, `bcrypt()`.

**Files:**
- Check: `SharpMUSH.Implementation/Functions/` for existing crypto
- Reference: `pennmush/src/funcrypt.c`

**Steps:**
1. List PennMUSH funcrypt.c functions
2. Check which exist in SharpMUSH
3. Implement missing ones with .NET crypto APIs
4. Match output format exactly
5. Commit: `feat(functions): crypto function parity`

---

### Task 2.3: Verify User-Defined Function Support (funufun.c)

**Objective:** Full parity for `u()`, `ulocal()`, `ufun()`, `udefault()`, `uldefault()`, `ulambda()`, `zfun()`, `objeval()`.

**Files:**
- Check: `SharpMUSH.Implementation/Functions/UtilityFunctions.cs` (or relevant file)
- Reference: `pennmush/src/funufun.c`

**Steps:**
1. Verify each of the 8 functions exists
2. Test register scoping: ulocal() preserves caller's registers, u() doesn't
3. Test ulambda() lambda evaluation
4. Test udefault()/uldefault() default-value semantics
5. Fix any divergence
6. Commit: `fix(functions): user-defined function parity`

---

### Task 2.4: Function-by-Function Output Parity Tests

**Objective:** Create parity tests for every PennMUSH function using known input/output pairs.

**Files:**
- Create: `SharpMUSH.Tests/Functions/PennMUSHParityTests/` (one file per category)
- Reference: PennMUSH help files + existing PennMUSH parity tests in C# suite

**Steps:**
1. Identify functions with no parity test coverage yet
2. Add test cases using PennMUSH docs for expected input/output
3. Run and catalog failures as a prioritized fix list
4. Commit: `test: expand function parity test coverage`

---

## Phase 3: Command Behavioral Parity

All 126 commands are present. Focus on behavioral correctness.

---

### Task 3.1: Un-skip and Fix Skipped Command Tests

**Objective:** The test suite has 192 skipped tests. Un-skip and fix as many as possible — they represent known gaps.

**Files:**
- All test files with `[Skip]` annotations

**Steps:**
1. Categorize skipped tests: DB-required (need Testcontainers), known failures, manual-only
2. For DB-required: ensure Testcontainers setup works in CI
3. For known failures (ZoneCommandTests: 2 failures): fix the underlying bugs
4. For "Skip for now" (LocateServiceCompatibility: 4): investigate and fix
5. Target: reduce 192 → <50 skipped
6. Commit per batch

---

### Task 3.2: Output Message Text Parity

**Objective:** Command output messages should match PennMUSH's exact wording. Softcode often pattern-matches on these.

**Files:**
- All command files in `SharpMUSH.Implementation/Commands/`
- Reference: PennMUSH source (grep for `notify`, `notify_format`)

**Steps:**
1. Extract PennMUSH's user-visible message strings
2. Compare with SharpMUSH's output strings
3. Fix wording differences
4. Commit: `fix(commands): align output message text with PennMUSH`

---

### Task 3.3: @lock and Boolean Expression Parity

**Objective:** Verify all lock types and boolexp evaluation match PennMUSH.

**Files:**
- Modify: Boolean expression evaluator
- Reference: `pennmush/src/boolexp.c`

**Steps:**
1. Test all lock types and boolean operators
2. Compare with PennMUSH
3. Fix divergence
4. Commit: `fix(commands): @lock and boolexp parity`

---

### Task 3.4: Command Queue / @wait / @trigger Parity

**Objective:** Verify queuing, timing, semaphores, executor/enactor/caller propagation.

**Files:**
- Queue handling
- Reference: `pennmush/src/cque.c`

**Steps:**
1. Test @wait (timed + semaphore), @trigger, @notify, @drain, @halt
2. Verify context propagation
3. Fix divergence
4. Commit: `fix(commands): queue and @wait/@trigger parity`

---

### Task 3.5: Movement, Exit Matching, and Hook/Mogrifier Parity

**Objective:** Verify exit matching, @teleport, movement messages, hooks, and mogrifiers.

**Files:**
- Movement + hook handlers
- Reference: `pennmush/src/move.c`, `pennmush/src/command.c`

**Steps:**
1. Test partial exit matching, ambiguity, locked exits
2. Test ODROP/OSUCC/OFAIL messages
3. Test @hook/before, @hook/after, @hook/override
4. Test mogrifiers
5. Commit: `fix(commands): movement and hook parity`

---

## Phase 4: Object Model & Permission Parity

---

### Task 4.1: Flag and Power Parity

**Objective:** All PennMUSH flags and powers exist with correct behaviors.

**Files:**
- Flag/power definitions
- Reference: `pennmush/src/flags.c`

**Steps:**
1. Extract PennMUSH flag list
2. Compare with SharpMUSH
3. Add missing, fix behaviors
4. Commit: `feat(objects): flag and power parity`

---

### Task 4.2: Attribute System Parity

**Objective:** Parent chain inheritance, attribute flags, @atrlock, standard attributes all match.

**Files:**
- Attribute handling
- Reference: `pennmush/src/attrib.c`

**Steps:**
1. Test parent chain, attribute flags, access controls
2. Verify all standard attributes (DESCRIBE, LISTEN, AHEAR, ACONNECT, etc.)
3. Fix divergence
4. Commit: `fix(objects): attribute system parity`

---

### Task 4.3: Economy and Quota System

**Objective:** BuildingCommands.cs has a comment noting economy/quota is unimplemented.

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/BuildingCommands.cs`
- Reference: `pennmush/src/create.c`, `pennmush/src/rob.c`

**Steps:**
1. Implement penny costs for @create/@dig/@open/@clone
2. Implement quota tracking
3. Implement give/rob fully
4. Commit: `feat(objects): economy and quota system`

---

### Task 4.4: Zone and Permission Model Parity

**Objective:** Zone-based permissions and full permission hierarchy match PennMUSH.

**Files:**
- Permission checking
- Reference: `pennmush/hdrs/mushdb.h`

**Steps:**
1. Test zone master objects, zone-based command matching
2. Test permission hierarchy
3. Fix ZoneCommandTests (2 known failures)
4. Commit: `fix(objects): zone and permission parity`

---

## Phase 5: Test Infrastructure & Integration

---

### Task 5.1: Fix 192 Skipped Tests

**Objective:** Reduce skipped tests from 192 to as few as possible.

**Breakdown of skips:**
- DB-required (~40): Need Testcontainers — fix test infrastructure
- Known failures (2): ZoneCommandTests — fix bugs
- "Skip for now" (~20): Investigate and resolve
- Admin/Wizard command tests (~30): Likely need permission setup
- Channel tests (7): Need channel system setup
- Debug/Verbose tests (9): May need special config

**Steps:**
1. Fix test infrastructure for DB-dependent tests
2. Fix known failures
3. Investigate and resolve "skip for now" tests
4. Commit per batch

---

### Task 5.2: Softcode Regression Suite

**Objective:** Test real-world MUSHcode patterns (BBS, combat code, building tools) end-to-end.

**Files:**
- Create: `SharpMUSH.IntegrationTests/SoftcodeRegression/`
- Reference: `SharpMUSH.IntegrationTests/TestData/PennMUSH_BBS_ReferenceOutput.txt` (already exists)

**Steps:**
1. Use existing BBS reference output as first regression test
2. Add complex nested evaluation patterns
3. Add u() chains with register passing
4. Add regexp/regedit patterns
5. Commit: `test: softcode regression suite`

---

## Phase 6: Networking & Polish

---

### Task 6.1: SSL/TLS Support

**Objective:** Add TLS to ConnectionServer if not present.

**Steps:**
1. Check current TLS status
2. Add TLS configuration
3. Test
4. Commit: `feat(network): TLS support`

---

### Task 6.2: Connection Screen and Login Flow Parity

**Objective:** Match PennMUSH's connect screen, MOTD, WHO/DOING format.

**Steps:**
1. Compare login flows
2. Match output format
3. Commit: `fix(network): connection flow parity`

---

## Execution Priority

1. **Phase 1** (Parser gaps) — Cascading impact
2. **Phase 2** (Function parity) — Most-used feature
3. **Phase 3** (Command behavior) — Output correctness
4. **Phase 4** (Object model) — Multi-user correctness
5. **Phase 5.1** (Un-skip tests) — Ongoing
6. **Phase 5.2** (Softcode regression suite) — Ongoing validation
7. **Phase 6** (Networking) — Lowest impact on softcode compatibility

**Done criteria:** All PennMUSH parity tests pass, all 192 skipped tests resolved, substitution coverage comprehensive, function parity tests pass, economy/quota implemented.
