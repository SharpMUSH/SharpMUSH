# Parser Improvement Handoff — Work Order

**Date:** 2026-07-26
**For:** any worker (human or Claude session) picking up parser correctness/performance work with no prior context.
**Evidence base:** `CoPilot Files/PARSER_PENNMUSH_VS_ANTLR4_DEEP_ANALYSIS.md` (same directory) — the full comparative analysis with all file:line citations, PennMUSH ground truth, and ANTLR4 source URLs. Read its §1 (architectures) and skim §4 (divergence catalog) before starting. This handoff is the *actionable layer*: what to change, where, how to prove it.

## Status

**P0, P1.1, P2, and P3 are shipped across three stacked PRs off `main`:** #714 (`fix/parser-pennmush-parity`, P0 parity), #716 (`fix/parser-depth-guard`, the remote-DoS depth guard), and #717 (`feature/parser-hardening-phase2`, two-stage SLL + HALT enforcement + output ceiling + the P3 compatibility decisions). The only remaining items — the attribute parse-tree cache (P1.3) and real `%c`/`%u` command identity (P3 #11) — are deliberately deferred as their own scoped follow-ups (`PARSER_PARSE_TREE_CACHE_SCOPING.md`, `PARSER_CMD_IDENTITY_SCOPING.md`); neither is implemented. P2.1 (CPU budget) was resolved with no code (already covered by `FunctionInvocationLimit`).

| Item | Commit | Outcome |
|---|---|---|
| P0.1 lock precedence | `045c25d1` | Fixed. Grammar gained precedence layers; all three visitors fold left. 4 of the new cases fail without it. |
| P0.2 arg parity | `763f1382` | Fixed, **but the analysis was partly wrong** — see correction below. |
| P0.3 unknown function | `0a9c2c38` | Fixed, plus error wording aligned to PennMUSH. 9 of 10 new cases fail without it. |
| P0.4 hygiene | `5cac02e2` | All five sub-items done; `Validate` now also rejects malformed locks. |

Full suite after each: **0 failures** (P0 landed at 4,954 tests; it has since grown past 5,015 with the P1/P2/P3 work); solution builds clean. Every fix was verified by reverting it and confirming the new tests fail (details in the commit messages), and every behavioural change is confirmed against a live PennMUSH oracle (now 42/42).

### Corrections to the analysis, found while implementing

1. **P0.2 was not entirely dead code.** `FunctionFlags.Regular == 0`, so `Regular | EvenArgsOnly == EvenArgsOnly` and `setr` *did* validate under the old `switch (attribute.Flags)`. Only flags combined with a non-zero flag (the three `NoParse | UnEvenArgsOnly` functions) were skipped. Verified: with the fix reverted, the `letq` cases fail and the `setr` case passes.
2. **`case`/`caseall` should never have carried `UnEvenArgsOnly`.** PennMUSH backs CASE/CASEALL/SWITCH with `fun_switch`, which checks only `minargs`; the canonical `case(str,pat,res,default)` is even-numbered. Making the parity check live without removing that flag breaks the common form — confirmed by re-adding the flag and watching those cases fail. Penn enforces parity only in `fun_letq` and `fun_setq` (src/funmisc.c:329,365).
3. **The unknown-function error wording also diverged.** `ErrorMessages.Returns.NoSuchFunction` was `#-1 COULD NOT FIND FUNCTION: {0}` while `fn()` emitted Penn's `#-1 FUNCTION (NAME) NOT FOUND` inline. Unified on Penn's wording; the six `@function` test assertions that encoded the old wording called their deleted functions *unbracketed*, which is no longer an error at all, so they now bracket the call.
4. **`Validate` accepted malformed locks.** Once the lock parser had a collecting error listener it became clear the validation visitor was walking ANTLR's recovery tree and reporting survivors as valid. `LockService.Set` gates on `Validate`, so this was the gate that let unparseable locks be stored.

### Oracle verification against a real PennMUSH

Every behaviour changed in P0 is confirmed against a running PennMUSH server, not just source reading: `SharpMUSH.Tests/PennMUSH/test_parser_parity.t` holds 27 cases that pass 27/27 on upstream Penn, and each has a matching SharpMUSH test. That file carries the build and run recipe, including the one non-obvious step — build with `env -i`, because a conda/homebrew ICU on the include path links against a system ICU of a different soversion and the final link fails on `u_isprint_NN`.

Worth reusing for P1–P3: it is far cheaper to settle a "what does Penn do here?" question by adding four lines to that file than by reading `parse.c`. The upstream `test/*.t` suite does **not** cover unknown-function handling, argument parity rejection, or lock precedence, so those questions can only be answered this way.

### Gap found and closed while implementing (commit `383aacf1`)

`VisitFunction`'s `_suppressFunctionEval` branch returned raw source text, so `notafunction(strlen(%#))` kept `%#` literal where Penn yields `notafunction(strlen(#1))` — confirmed by oracle case `parity.demoted_sub_nested`. Routing that branch through the same reconstruction fixed it. The branch was only reachable from the non-call path added in `0a9c2c38`: braces, the other place that clears function recognition, parse a function name as plain text so no call context is ever built inside one — which is why `strcat({strlen(%#)})` was already correct.

---

**Everything below P0 is analyzed but NOT fixed.**

---

## 0. Orientation (15 minutes)

Read in this order:
1. `PARSER_PENNMUSH_VS_ANTLR4_DEEP_ANALYSIS.md` §1 + §4 (this directory).
2. `ANTLR4_SLL_AND_TOKEN_REWRITING_SUMMARY.md` (this directory) — history of the four grammar bugs and why token rewriting exists. **Do not touch `SharpMUSHParser.g4` without reading this** (there is a documented AdaptivePredict hang lurking behind "obvious" grammar simplifications).
3. Code hot spots:
   - `SharpMUSH.Parser.Generated/SharpMUSHLexer.g4` + `SharpMUSHParser.g4` (~230 lines total)
   - `SharpMUSH.Implementation/MUSHCodeParser.cs:152–236` (the parse funnel)
   - `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:282–290` (deferred eval), `:406–697` (function dispatch), `:1828–2020` (ArgumentSplit)
   - `SharpMUSH.Library/ParserInterfaces/ParserState.cs` (state model)

A reference PennMUSH checkout for ground-truth reading lives at commit `80a1d5b9` (github.com/pennmush/pennmush); all `src/parse.c` line numbers in the analysis doc refer to it.

### Build / test environment

- Grammar edits regenerate into `SharpMUSH.Parser.Generated/obj/` at build time (Antlr4BuildTasks 12.14.0, `<ToolType>antlr-ng</ToolType>`). First build downloads a JRE to `~/.jre` and the tool jar to `~/.m2`. **Never add `<Error>true</Error>` to the `<Antlr4>` items** — any warning then silently produces zero generated files.
- Test framework is **TUnit** (not xUnit): `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ClassName/*"`.
- Parser test suites: `SharpMUSH.Tests/Parser/` — especially `PennMUSHOracleTests` (177 golden cases vs real Penn), `PennMUSHParserGapTests` (57 known-gap cases), `RecursionAndInvocationLimitTests`, `ParserFailureTests`. Also `SharpMUSH.Tests/Functions/ParserBehaviorUnitTests`.
- Benchmarks: `SharpMUSH.Benchmarks/` (`CommandParseBenchmarks`, `SubstitutionBenchmarks`, `ListFunctionBenchmarks`). Run before/after any P1 item.
- Integration tests run on Podman testcontainers. If they wedge at "Engine Mode: SourceGenerated", clear stale Podman containers first.

---

## P0 — Correctness bugs (small diffs, do first, one PR each or one combined)

### P0.1 Lock `&`/`|` precedence bug (security-relevant)

**Problem:** PennMUSH lock grammar is `E→T|E`, `T→F&T` — AND binds tighter than OR (`pennmush/src/boolexp.c:1378–1430`). SharpMUSH's `SharpMUSHBoolExpParser.g4:16–20` (`lockExprList: lockAndExpr | lockOrExpr | lockExpr;` with `lockAndExpr: lockExpr AND lockExprList`) gives equal precedence, right-associative. `a & b | c` parses as `(a&b)|c` in Penn, `a&(b|c)` in SharpMUSH.
**Witness:** lock `#FALSE & #FALSE | #TRUE` — Penn passes, SharpMUSH fails. Over-permissive mirrors exist (`me & FLAG^WIZARD | #TRUE` style).
**Fix:** restructure the grammar to precedence-layered form:

```antlr
lockExprList : lockOrExpr ;
lockOrExpr   : lockAndExpr (OR lockAndExpr)* ;
lockAndExpr  : lockExpr (AND lockExpr)* ;
```

Then update **all three** visitors to fold the new list shapes (left-fold; `&`/`|` are associative so fold direction only affects short-circuit order):
- `SharpMUSHBooleanExpressionVisitor.cs:78–82` (`Expression.AndAlso`/`OrElse` chains)
- `SharpMUSHBooleanExpressionNormalizationVisitor.cs:29–33` (string reassembly — output text must stay round-trippable)
- `SharpMUSHBooleanExpressionValidationVisitor .cs` (note: filename really has a space before `.cs` — fix that too while there)
**Tests:** add to `BooleanExpressionUnitTests`: `#FALSE & #FALSE | #TRUE` → true; `#TRUE | #FALSE & #FALSE` → true; `#FALSE & (#FALSE | #TRUE)` → false (parens still honored); plus normalization round-trips for `a&b|c`.
**Also:** `BooleanExpressionParser` FusionCache (`compiled-expressions`) must be invalidated on deploy — compiled lambdas from the old shape are wrong. Cache key includes the lock string only, so bump the cache key prefix or call `InvalidateCache`.

### P0.2 Even/odd argument validation never fires

**Problem:** `SharpMUSHParserVisitor.cs:540–548` does `switch (attribute.Flags)` with `case FunctionFlags.UnEvenArgsOnly when …` — exact-match on the whole flags value. Every real declaration combines flags (`letq` = `NoParse|UnEvenArgsOnly` at `UtilityFunctions.cs:810`; `setr` = `Regular|EvenArgsOnly` at `UtilityFunctions.cs:1521`; also `case`/`caseall` in `StringFunctions.cs:686,708`), so the check is dead code.
**Fix:** replace the switch with two `if (attribute.Flags.HasFlag(...))` checks.
**Tests:** `letq(a)` fine; `letq(a,b)` → `#-1 FUNCTION (LETQ) EXPECTS AN ODD NUMBER OF ARGUMENTS` (match Penn's message — check `ErrorMessages.Returns.GotEvenArgs` wording against Penn); `setr` with odd args errors.

### P0.3 Unknown function at top level should stay literal

**Problem:** Penn only errors on unknown function names **inside `[]`** (`PE_FUNCTION_MANDATORY`, `parse.c:2788–2806`); at top level, `foo(bar)` remains literal text (`parse.c:2807–2823`). SharpMUSH's `CallFunction` (`SharpMUSHParserVisitor.cs:427–431`) errors unconditionally. `think hello(world)` diverges.
**Fix sketch:** in `VisitFunction`/`CallFunction`, on lookup miss (after built-in discovery and `@function` resolution both miss): walk `context.Parent` — if no `BracketPatternContext` ancestor *for the current function-check scope*, return `new CallState(GetContextText(context))` (the raw source slice, markup-preserving) instead of the error. Inside brackets keep the error (optionally add Penn's `DID YOU MEAN 'X'` suggestion — nice-to-have).
**Caution:** "inside brackets" must mean the *innermost* function-check scope: `[strcat(foo(1))]` — `foo` is inside brackets transitively; Penn's rule is that mandatory-ness applies to the function whose name check was (re-)enabled by `[`. Since args are evaluated with FUNCTION_CHECK but *not* MANDATORY in Penn (`parse.c:2855` — MANDATORY is stripped for args), nested unknown calls inside a known call's args are ALSO literal in Penn. So the correct rule is: error only when the FunctionContext's nearest enclosing structural context is a `bracketPattern` (i.e., `context.Parent.Parent is BracketPatternContext` via `evaluationString`), not merely any ancestor. Write oracle tests against real Penn for: `think foo(1)`, `think [foo(1)]`, `think [strcat(foo(1))]`, `think add(foo(1),2)`.
**Tests:** the four cases above; update any existing tests asserting the old unconditional error.

### P0.4 Hygiene batch (single small PR)

1. **Lexer console noise:** `SharpMUSHLexer` instances never get `RemoveErrorListeners()` — add it beside the parser's (`MUSHCodeParser.cs:195` and the 3 sibling construction sites `:389/:569/:654`, plus `CommandListParseVisitor` `:371–380` and `Tokenize`/`GetSemanticTokens`).
2. **Boolexp console noise:** `BooleanExpressionParser.cs` `Compile`/`Validate`/`Normalize` (`:74/:98/:122`) never remove listeners — locks *can* fail to parse, and errors currently go to server stdout instead of the player. Add a collecting listener; surface failures to the caller (Penn notifies the player and falls back to `TRUE_BOOLEXP`).
3. **EOF-less entry rule:** `CommandCommaArgsParse` (`MUSHCodeParser.cs:484–486`) invokes `p.commaCommandArgs()` — no EOF anchor, trailing garbage silently drops. Switch to `p.startPlainCommaCommandArgs()` (already exists, used by `ValidateAndGetErrors`) and adjust the visitor entry (`VisitStartPlainCommaCommandArgs` is currently *not* overridden — either override it to delegate to `VisitCommaCommandArgs(context.commaCommandArgs())` or unwrap in the facade).
4. **`lit()` markup loss:** the `Literal` branch (`SharpMUSHParserVisitor.cs:568–586`) rebuilds from `context.GetText()` (plain text — drops out-of-band markup). Re-slice from `source` instead: compute the span from after `FUNCHAR` to before the final `CPAREN` token using token `StartIndex`/`StopIndex` and `MModule.substring`, like `GetContextText` does.
5. **Dead/vestigial code:** `pendingEscapedOpeners` in both `RewriteOrphaned*` passes (`MUSHCodeParser.cs:967–1001, 1009–1043`) is written, never read — delete. `SharpMUSH.Implementation/TraceListener.cs` has zero references — delete. `<Compile Remove="gen\**">` in the Parser.Generated csproj targets a nonexistent dir — delete. Stale `SharpMUSH.Generated.generated.sln` in Parser.Generated — delete.

---

## P1 — The two big performance levers

> **Status:** P1.1 is **shipped** (PR #717 — default is now `TwoStage`). P1.2 is **subsumed by P1.3** (a content-addressed parse-tree cache makes the `iter ##` and command-arg re-parses moot). P1.3 is **scoped, not implemented** (`PARSER_PARSE_TREE_CACHE_SCOPING.md`), deliberately benchmark-gated because two-stage SLL already cut parse cost. The subsections below are the original plan, kept for context.

### P1.1 Two-stage SLL→LL prediction (single biggest cheap win)

**Problem:** production default is `PredictionMode.LL` (`SharpMUSH.Configuration/Options/DebugOptions.cs:19`), while the project's own measurement (`ANTLR4_SLL_AND_TOKEN_REWRITING_SUMMARY.md` §5) found SLL produces **identical parse trees at 171×** the speed (8.9 ms vs 1531.6 ms over the Myrddin corpus; 477 LL full-context scans confirmed no divergence). The enum doc-comment's justification ("SLL … ignores semantic predicates during prediction") misstates ANTLR semantics — SLL ignores *parser context*; the official guarantee (PredictionMode API doc) is SLL either matches LL or reports a syntax error. The officially documented resolution (ParserATNSimulator doc, credited to Sam Harwell) is two-stage.
**Fix:** in `ParseInternalCore` (and the duplicated block in `CommandListParseVisitor`):

```csharp
sharpParser.Interpreter.PredictionMode = PredictionMode.SLL;
sharpParser.ErrorHandler = new BailErrorStrategy();   // strict path
try { context = entryPoint(sharpParser); }
catch (Antlr4.Runtime.Misc.ParseCanceledException)    // C# name — NOT Java's ParseCancellationException
{
    bufferedTokenSpanStream.Reset();                  // custom stream: verify Seek(0) resets p correctly
    sharpParser.Reset();
    sharpParser.Interpreter.PredictionMode = PredictionMode.LL;
    sharpParser.ErrorHandler = lenient ? new LenientErrorStrategy() : new DefaultErrorStrategy();
    context = entryPoint(sharpParser);
    _sllFallbacks.Increment();                        // telemetry counter — expect ~0 in production
}
```

Details that matter:
- The existing strict/lenient split stays: in stage 2, strict mode still consults `ParserErrorListener` and returns `#-1 PARSER FAILURE`; lenient mode visits the recovery tree. Stage 1's bail replaces *neither* — it only detects "SLL wasn't enough".
- `BufferedTokenSpanStream` is custom — verify `Reset()`/`Seek(0)` actually rewinds (`p = AdjustSeekIndex(0)`) and that `Fill()` state survives. Add a unit test for re-parse-after-bail on the same stream instance.
- Wire `_sllFallbacks` into `ITelemetryService` so production data can prove the grammar is SLL-clean.
- Update `DebugOptions`: default becomes `SLL` (two-stage), keep `LL` as an escape hatch, **rewrite the enum doc-comments** (current text is wrong), and correct `ANTLR4_SLL_AND_TOKEN_REWRITING_SUMMARY.md` §5's "SLL is the default" claim to match reality once it actually is.
**Verification:** full test suite (4800+) green in SLL two-stage; `AntlrParseTreeDiagnosticTests` SLL-vs-LL equivalence still passes; benchmark suite before/after (expect large gains on `CommandParseBenchmarks`); fallback counter stays 0 across the corpus.

### P1.2 Stop re-parsing already-parsed code

Three sites re-lex/re-parse text whose parse tree already exists. The correct pattern already exists in-repo: `CreateDeferredEvaluation` (`SharpMUSHParserVisitor.cs:282–290`) closes over the **subtree** and re-visits — no re-parse.

1. **Command-arg deferred lambdas** (`ArgumentSplit`, `SharpMUSHParserVisitor.cs:1970–2016`): NoParse/EqSplit paths build `async () => (await prs.FunctionParse(x)).Message` — a full pipeline per deferred evaluation (TODO comment at `:1981` acknowledges). The split parse (NoParse mode) already produced `EvaluationStringContext`s; carry them (or the `CallState` from the split visitor) into the deferred closure and `VisitChildren` them instead. Watch: the deferring parser (`prs`) differs from the split parser (`newNoParseParser` with `ParseMode.NoParse`) — the revisit must run under `prs`'s state, which `CreateDeferredEvaluation`'s pattern (visitor instance + context) supports since visitor state flows from `parser.CurrentState`.
2. **`iter()` `##` path** (`ListFunctions.cs:523–535`): when the pattern contains `##`, it string-replaces to `%iL` then `FunctionParse`s **per element** — N full pipelines. Since the rewrite target is always the same text, parse once outside the loop and re-visit per element (the iteration registers already carry the element). The no-`##` path (`:535` deferred closure) is already right.
3. **Attribute parse-tree cache** (the big one): every `u()`/`$`-command/hook/`@function` call re-parses stored attribute text (`AttributeService.cs:283,441`). Attribute text is immutable between writes; the visitor keeps all mutable state in `ParserState`; parse trees + the source `MString` are therefore reusable. Precedent: `BooleanExpressionParser.Compile`'s FusionCache (`compiled-expressions`, 1 h TTL). Implement `FunctionParseCached(MString)`: key = the **full** plain text (or a collision-resistant digest — never a truncated `GetHashCode`, and include a grammar/semantic-version component so trees are not reused across incompatible parser versions) plus the entry rule; value = `(StartPlainStringContext tree, MString source, BufferedTokenSpanStream stream)` — the visitor needs the stream alive only for token text, which `OptimizedToken` lazily slices from the input string, so retain the input string reference. Content-addressing means **no invalidation is ever required**: a changed attribute is a different key, and old entries simply age out under the size cap. Do not add an invalidation hook — if you find yourself needing one, the key is wrong. Add cache hit/miss telemetry.
   **Risk check before building:** confirm the visitor never mutates contexts (it doesn't — it only reads) and that `ParserRuleContext` is safe for concurrent *readers* (it is — immutable after parse; the tree is only written during parsing). Two concurrent evaluations of the same cached tree must each use their own visitor instance (already the pattern).
**Verification:** perf suite (`ParserThroughputTests`, `ListFunctionBenchmarks` for `iter`), plus correctness: `PennMUSHOracleTests` + `ParserBehaviorUnitTests` green; add a test that a cached attribute re-evaluates with *current* `%0`/registers (state independence proof) and that editing an attribute takes effect immediately (invalidation proof).

---

## P2 — Robustness parity with Penn

> **Status:** shipped except where noted. **P2.1 (time budget) — RESOLVED, no code**: runaway protection is already met by `FunctionInvocationLimit`, and a wall-clock deadline would misfire on DB-latency-slow evals (true per-eval CPU time is infeasible in async .NET). **P2.2 (parse-phase stack guard) — DONE** (PR #716). **P2.3 (output ceiling) — DONE** (5 MB single-function cap, PR #717). **P2.4 (halted executor) — DONE** (HALT enforced on `u()`/`ufun` and `$`-command dispatch, PR #717). **Limit defaults** were settled in the P3 table below.

1. **Evaluation time budget** (Penn: 1500 ms CPU slice per queue entry, `setitimer(ITIMER_PROF)`; SharpMUSH: nothing). Add `Deadline` (a `long` Stopwatch timestamp) to the shared limit objects in `ParserState` (beside `LimitExceeded`); set at top-level entry (`CommandParse`/queue task start); check where `LimitExceeded` is checked (`VisitChildren:170`, `CallFunction`) — on expiry set `LimitExceeded`, return a `#-1 CPU USAGE EXCEEDED`-style message once, notify the enactor like Penn does. Config knob mirroring `queue_entry_cpu_time`.
2. **Parse-phase stack guard. — DONE** (branch `fix/parser-depth-guard`, PR #716).** The generated recursive-descent parser runs before any visitor limit and has no depth cap — `[[[[…`×10⁵ in one input (typed, or stored in an attribute) can `StackOverflowException` → **process death**. Two layers: (a) cheap O(n) pre-scan in `ParseInternalCore` counting max `[`/`{`/`(` nesting; reject > configured limit (default ~500; Penn's shipped call_limit is 100) with a MUSH error string; (b) optional belt-and-braces: partial class on generated `SharpMUSHParser` overriding `EnterRule` to call `RuntimeHelpers.EnsureSufficientExecutionStack()` (throws catchable `InsufficientExecutionStackException`) — catch in `ParseInternalCore`, convert to error string.
3. **Output-size ceiling.** Penn truncates everything at 8 K (silent) — also a memory backstop. Don't copy the tightness; add a configurable per-evaluation cap (e.g. 1 MB) enforced in `BatchMergeResults`/`ConcatMany` paths → on exceed, set `LimitExceeded` with a distinct error. Prevents `repeat(x,999999999)`-style memory DoS.
4. **Halted executor check.** Penn: `Halted(executor)` → evaluate nothing (`parse.c:2092–2093`). Verify SharpMUSH's queue refuses halted objects; if not, add the check at evaluation entry (`FunctionParse`/`CommandListParse`) — return input text unevaluated (Penn's `PE_NOTHING` copies raw).
5. **Limit defaults decision:** SharpMUSH ships 100/100 000/1 000 (recursion/invocation/call) vs Penn's shipped 50/25 000/100. Either match Penn's mush.cnf values or document the deliberate loosening in help files.

---

## P3 — Compatibility decisions (each needs a deliberate yes/no, then either a fix or a documented incompatibility + gap test)

| # | Divergence | Penn behavior (citation) | Current SharpMUSH | Recommendation |
|---|---|---|---|---|
| 1 | Command word evaluation | Word evaluated with subs+`[]`, no fn check (`command.c:1287–1292`): `[strcat(th,ink)] hi` runs think | Raw `GetText()` match (`EvaluateCommands`) | Decide; if fixing, evaluate the word before trie/socket/channel matching |
| 2 | `$`-command match input | Matches **evaluated** line (`game.c` `cptr`) | Raw slice (`CommandDiscoveryService.cs:43`); hook OVERRIDE path already evaluates (visitor `:1509–1515` — that comment is the spec) | Fix — unify on evaluated line |
| 3 | Parser-failure class | Unclosed `[`/`(`/`{`, trailing `\`/`%` all evaluate (`parse.c` §4.7 of analysis doc) | `#-1 PARSER FAILURE` in strict mode | Narrow via extended token rewriting (synthesize closers at EOF; retype trailing `ESCAPE`/`PERCENT` → `OTHER`) — the existing rewrite passes are the template; keeps grammar untouched |
| 4 | Arg eval before arity errors | Args evaluated (side effects fire) before `EXPECTS … ARGUMENTS` (`parse.c:2954–2998`) | Validates first, no side effects | Probably keep Sharp's order (safer); add gap test documenting it |
| 5 | Negative maxargs | Final arg absorbs commas (`parse.c:2878–2894`) | Always splits; errors above MaxArgs | Audit Penn's function table for `maxargs<0` entries; where SharpMUSH declares those functions, join surplus args with `,` into the last |
| 6 | Limit-hit tail | Penn keeps evaluating remainder, dedups error string (`parse.c:2833–2835`) | `LimitExceeded` abandons rest of tree | Probably keep Sharp's (cheaper, safer); gap test |
| 7 | `iter ##` splice | Textual splice, element text **executes** (`funlist.c:2105–2107`) | `%iL` register (data, injection-safe) | Keep Sharp's (safer); document loudly in help — imported code relying on splice-eval breaks |
| 8 | Dynamic function names | Name = evaluated output prefix (`parse.c:2780–2786`): `%q0(1,2)` calls fn named in q0 | Impossible (lexical FUNCHAR); 2 `[Skip]` tests | Keep as documented incompatibility; add help-file entry |
| 9 | `% ` (percent-space) | Emits both chars `% ` (`parse.c:2422–2423`) | Unverified (`OTHER_SUB` path) | Write the test, fix if divergent |
| 10 | Uppercase-sub capitalization | ANY uppercase sub letter capitalizes first output char — incl. `%Q0`, `%I0`, `%V…` (`parse.c:2670–2675`) | Partial (%N modeled; rest unverified) | Audit `Substitutions.cs`; add oracle tests |
| 11 | `%c`/`%u` timing | cmd_raw = pre-split raw; cmd_evaled = post-parse reassembly | Tokens exist; timing unverified | Oracle tests |
| 12 | Unknown-fn suggestion in `[]` | `DID YOU MEAN 'X'` (`parse.c:2795–2799`) | No suggestion | Nice-to-have with P0.3 |
| 13 | Astral-plane `%<emoji>` | Penn byte-oriented | UTF-16 code units — lone-surrogate risk in SUBSTITUTION mode | Test; document; only act if broken output (MString is UTF-16 too — consistency argues no stream change) |

**Differential harness (multiplies everything above):** stand up dockerized PennMUSH (repo has no blocker; Penn builds trivially) + a corpus runner that pipes expressions through both engines and diffs. `PennMUSHOracleTests` is the hand-made version; industrialize it (generate corpora: random nesting of the 15 special chars + function calls from the shared roster). Every P3 decision then gets its witness row automatically, and regressions in parity become CI failures.

---

## Gotchas for whoever picks this up

- **The grammar bites back.** Innocent-looking predicate additions to `beginGenericText`/`explicitEvaluationString` have caused AdaptivePredict *hangs* (not slowness — hangs). The safe extension point is the token-rewriting layer (`RewriteOrphaned*` in `MUSHCodeParser.cs`), not new grammar predicates. History in `ANTLR4_SLL_AND_TOKEN_REWRITING_SUMMARY.md`.
- **Visitor TResult is `ValueTask<CallState?>`** — every override must await children via the hand-rolled `VisitChildren`; the generated base's default would fire-and-forget tasks.
- **Markup is out-of-band.** Never reconstruct user text with `context.GetText()` — re-slice the `source` MString with token offsets (`GetContextText`). `GetText()` is only acceptable for identifier-ish text (function names).
- **Two copies of Penn semantics knowledge** now exist: the analysis doc (ground truth with citations) and scattered code comments. When they disagree, the analysis doc's parse.c citations win — verify against the Penn checkout.
- **`MUSHCodeParser` is a singleton record**; `Push`/`FromState` return copies. Shared-mutable limit state (`InvocationCounter`, `LimitExceededFlag`) is *deliberately* reference-typed — don't "fix" that.
- **TreatWarningsAsErrors** is on in most projects including Parser.Generated.
- CI note from repo memory: component tests exist in BOTH `SharpMUSH.Tests.BUnit` AND `SharpMUSH.Tests`; running only one misses failures.

## Definition of done per phase

- **P0:** all four items merged with tests; oracle suite green; no console output during parse-error tests (assert via captured stdout).
- **P1:** SLL two-stage default with fallback telemetry ~0 across test corpus; benchmark deltas recorded in this file; attribute-tree cache behind a config flag first, default-on after a soak.
- **P2:** the `[[[[`-bomb test passes (server survives, error string returned); time-budget test passes (deliberately slow eval unwinds).
- **P3:** each row has either a merged fix + oracle test or a `[Skip]`-documented gap test + help-file entry. The table above updated in place with outcomes.
