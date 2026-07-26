# SharpMUSH Parser & Lexers vs PennMUSH — Deep Analysis and ANTLR4 Standards Audit

**Date:** 2026-07-26
**Scope:** The four ANTLR4 grammars, `MUSHCodeParser`, the four visitors, custom streams/error machinery — compared against PennMUSH master (`80a1d5b9`, 2026-03-22) `process_expression()`/`boolexp.c`, and audited against official ANTLR4 documentation and community sources.
**Provenance:** Direct source reading of both codebases plus four research passes (PennMUSH internals, SharpMUSH inventory, official ANTLR4 docs, ANTLR community). Every claim marked *confirmed* was verified against source; items marked *audit* need a test to confirm.
**Purpose:** Reference document — raw material for a future `sharpmush-parser-internals` skill.

---

## 1. The two architectures

### PennMUSH: fused single-pass parse+evaluate

There is no lexer, no token stream, no AST. One recursive C routine, `process_expression()` (`src/parse.c:2048–3195`), scans the source left-to-right exactly once and **writes evaluated output into a fixed 8 KB buffer as it scans**. Structure discovery and evaluation are the same act.

- **Hot loop:** bulk-copy all "uninteresting" chars (SSE4.2 `_mm_cmpistri` against the 15-char set `%{[(\ }>]),;=$\x1B`, `src/parse.c:2191–2218`; `active_table` fallback), then dispatch on the special char.
- **Terminator flags (`PT_*`)**: the caller tells each recursion which single characters end it (`PT_COMMA|PT_PAREN` for function args, `PT_BRACE` for `{}`, `PT_SEMI` for action lists, `PT_EQUALS`/`PT_SPACE` for command args, `PT_GT` for `%q<…>`). Nesting hides outer terminators *by construction* — a `{}` recursion only knows about `}`, so `;` inside braces is automatically plain text. `process_expression(…, PE_NOTHING, PT_x, …)` doubles as the syntax-aware, non-evaluating splitter used by the queue (`src/cque.c:1157`), `@filter`, and command args.
- **Eval flags (`PE_*`)**: `PE_EVALUATE`, `PE_FUNCTION_CHECK` (may `name(` start a call), `PE_FUNCTION_MANDATORY` (inside `[]`: unknown fn = error), `PE_COMPRESS_SPACES`, `PE_STRIP_BRACES`, `PE_COMMAND_BRACES` (strip one leading brace group only), `PE_LITERAL` (for `lit()`), `PE_DOLLAR`, debug flags. Composites: `PE_DEFAULT = COMPRESS_SPACES|STRIP_BRACES|DOLLAR|EVALUATE|FUNCTION_CHECK`; `PE_UDEFAULT` = same minus DOLLAR (attribute eval).
- **Function calls** (`src/parse.c:2736–3090`): on `(` with EVALUATE+FUNCTION_CHECK, the function name is **everything already evaluated at this level** — the output buffer from `startpos` to `*bp`, uppercased (`:2780–2786`) — then `*bp = startpos` erases it. FUNCTION_CHECK clears after the first `(` per level. Args are recursed one at a time into a scratch buffer with `PT_COMMA|PT_PAREN`, each with `PE_COMPRESS_SPACES|PE_EVALUATE|PE_FUNCTION_CHECK` forced; `FN_NOPARSE`/`FN_LITERAL` strip those (raw capture, delimiters still balanced by the recursion machinery). Negative `maxargs` = final arg absorbs remaining commas (`:2878–2894`). Zero args = one empty arg, collapsed to zero if `minargs==0` (`:2974–2979`).
- **No syntax errors exist.** Unclosed `[`, `(`, `{`, trailing `\`, trailing `%`, stray closers — all degrade to sensible output (see §4 table). Error *strings* (`#-1 …`) are ordinary output text.
- **Limits** are cooperative counters on the shared `pe_info`: recursion (`fun_recursions`, shipped default 50), invocations (shipped 25000, compiled 2500), parser depth `CALL_LIMIT` (shipped 100 — explicitly a C-stack guard; once crossed, stays pinned), plus a **1500 ms CPU slice per queue entry** via `setitimer(ITIMER_PROF)` → `cpu_time_limit_hit` checked at every entry (`src/parse.c:2077–2091`, `src/timer.c:316–345`). `BUFFER_LEN` 8192 silently truncates everything. Halted executors evaluate nothing (`eflags = PE_NOTHING`, `:2092–2093`).
- **Markup is in-band**: `TAG_START`/`TAG_END` (`\002`/`\003`) sentinel bytes inline in the char stream; markup counts against the 8 K; raw `ESC…m` copied atomically; `FN_STRIPANSI` args get `remove_markup()`.
- **Command pipeline**: queue splits action lists on `;` via `PE_NOTHING|PT_SEMI` (player-typed lines never split: `QUEUE_NOLIST`); `command_parse` evaluates the **command word itself** with `(PE_DEFAULT & ~PE_FUNCTION_CHECK) | PE_COMMAND_BRACES`, `PT_SPACE` (`src/command.c:1287–1292`) — so `%`-subs and `[]` work in command names; unique-prefix match via prefix table; args split/evaluated per table flags (`EQSPLIT`, `LS/RS_ARGS`, `*_NOPARSE`, `RS_BRACE`) before the command body runs; **$-commands match the evaluated line** (`src/game.c:1256–1364`).
- **Boolexp is a real compiler**: recursive descent (`E→T|E`, `T→F&T`, `F→!F|A` — **AND binds tighter than OR**, `src/boolexp.c:1378–1430`) → AST → optimizer → bytecode for a tiny register VM; names resolve to dbrefs at parse time; locks *can* fail to parse (player notified, `TRUE_BOOLEXP` sentinel).

### SharpMUSH: ANTLR4 parse-then-visit

Four grammars in `SharpMUSH.Parser.Generated/` (main `SharpMUSHLexer`/`SharpMUSHParser` + `SharpMUSHBoolExp*`), generated by **antlr-ng** via Antlr4BuildTasks 12.14.0 against `Antlr4.Runtime.Standard` 4.13.1 (net10.0).

- **Lexer** (`SharpMUSHLexer.g4`): default mode has ~14 token types + greedy catch-all `OTHER` (fat text token). Three island modes, each popping after one token-ish unit: `SUBSTITUTION` (entered by `%`, one token per sub kind), `ESCAPING` (`\` + `ANY .`), `ANSI` (raw ` … m`). Delimiter tokens fold trailing whitespace (`OBRACK: '[' WS`, `COMMAWS: ',' WS`, `FUNCHAR: [0-9a-zA-Z_~@`]+ '(' WS`) — leading-space-eating is baked into the lexer. **The function name + `(` is a single lexical token** (`FUNCHAR`), which is the deepest single divergence from Penn (see §4.1).
- **Parser** (`SharpMUSHParser.g4`): 7 entry rules (`startPlainString`, `startSingleCommandString`, `startCommandString`, `startPlainSingleCommandArg`, `startPlainCommaCommandArgs`, `startEqSplitCommandArgs`, `startEqSplitCommand`). Context-sensitivity (Penn's PT/PE flags) is encoded as **mutable `@parser::members` state mutated by embedded actions + semantic predicates** (`inFunction`, `inBraceDepth`, `savedFunction` stacks, `lookingFor*` flags). A documented AdaptivePredict hang forced a near-duplicate rule (`braceExplicitEvaluationString`) and the token-rewriting workaround.
- **Facade** (`MUSHCodeParser.cs`): singleton record; fresh `StringSpanInputStream` (custom `ICharStream` over the plain text) + `SharpMUSHLexer` + `OptimizedTokenFactory` (lazy text slices) + `BufferedTokenSpanStream` (custom, `Fill()` then array) + two token-rewrite passes (orphaned `]`/`}` → `OTHER`) + fresh `SharpMUSHParser` + fresh visitor **per parse call**. `PredictionMode` configurable, **default LL** (`DebugOptions.cs:19`). Strict mode (function eval): any syntax error → `#-1 PARSER FAILURE` string without visiting; lenient mode (command args): visit ANTLR's recovery tree with `LenientErrorStrategy` (empty-text synthetic tokens).
- **Visitor** (`SharpMUSHParserVisitor`, 2416 lines): `SharpMUSHParserBaseVisitor<ValueTask<CallState?>>` — the exact async-visitor recipe Sam Harwell recommends (TResult task type + `DefaultResult` override + hand-rolled awaiting `VisitChildren`). Carries Penn's semantics in visit methods: one-level brace strip + function suppression inside function-arg braces (`_suppressFunctionEval`), bracket re-enable, `PE_COMPRESS_SPACES` analogues (markup-aware `compressSpaces` + edge trims), NoParse/Literal argument capture (`CreateDeferredEvaluation` stores the **subtree** in a closure — no re-parse), Penn-shaped limits (`TotalInvocations`/`CallDepth`/`FunctionRecursionDepths`/`LimitExceeded` shared mutable refs on immutable `ParserState` stack). Command dispatch (Penn's `command_parse` + $-command walk) lives in `EvaluateCommands` inside the visitor.
- **Markup is out-of-band**: `MarkupString` = flat text + attribute runs; the lexer only ever sees plain text; markup is recovered by re-slicing the original `MString` with token offsets (`GetContextText`) — deliberately *not* `GetText()`.
- **Boolexp**: grammar → three visitors (eval → compiled LINQ `Expression`, cached in FusionCache `compiled-expressions`; normalization → canonical text with dbref resolution at lock-set; validation). Architecturally parallel to Penn's bytecode compiler.

### The philosophical difference in one sentence

Penn **discovers structure while producing output** (so structure can depend on already-produced output); SharpMUSH **commits to structure before producing any output** (so evaluation can never influence parsing). Every hard behavioral divergence below is a corollary of this sentence.

---

## 2. What the approach split makes *inevitable*

| Axis | PennMUSH (fused) | SharpMUSH (ANTLR4) | Consequence |
|---|---|---|---|
| Parse ↔ eval feedback | Output feeds back into parsing (function names from evaluated text) | Impossible by construction | §4.1 dynamic-name divergences |
| Error model | Total language; no failure concept | Grammar can reject; needs rewrites/recovery/failure strings | `#-1 PARSER FAILURE` class of divergence |
| Memory | Zero alloc per node; one 8 K buffer; silent truncation | Tokens + contexts + tree per parse; unbounded strings | Perf profile + no truncation backstop |
| Laziness | Re-scan captured raw text later | Can revisit the already-built subtree (cheaper!) or re-parse | Sharp has a structural advantage it only partially uses |
| Introspection | None (no tree) | Tree → LSP, semantic tokens, diagnostics, syntax highlighting | Sharp-only capabilities (already shipped) |
| Recursion safety | CALL_LIMIT guards C stack inside the same pass | Visitor limits run **after** the ANTLR parse, which has no guard | §6 stack-overflow exposure |
| Concurrency | Single-threaded server | Instance-per-parse over shared static DFA | Sound; contention only at extreme core counts |
| Unicode | Byte-oriented | UTF-16 code units (`StringSpanInputStream` indexes `string`) | Astral chars: `%😀` splits surrogates in SUBSTITUTION mode (audit) |

---

## 3. Verified parity (things SharpMUSH gets right that are easy to get wrong)

- Function check only at expression-level start; `add(1,2)add(3,4)` → `3add(3,4)` (grammar: `function` only as first alternative of `evaluationString`).
- `fn()` = one empty arg, collapsed to zero when `MinArgs==0` (`CallFunction` — mirrors `parse.c:2974–2979`).
- One-level brace strip; function suppression inside function-arg braces; `[]` re-enables function check inside braces (`{[add(1,2)]}` → `3`); command-brace preservation via `PreserveBraces`/`RSBrace` (Penn `CS_BRACES`/`PE_COMMAND_BRACES`).
- Space semantics: leading eaten (lexer WS folding), internal runs collapsed, trailing stripped **only when the last element is literal text** — Penn's `had_space`/"last source char was a literal space" rule, so `%b`-trailing survives in both.
- `%q<name>` register names are themselves evaluated (`REG_STARTCARET explicitEvaluationString CCARET` ≈ Penn's `PT_GT` recursion).
- Semicolon splitting only for command lists, never for direct player input (`startSingleCommandString` vs `startCommandString` + `DirectInput` ≈ `QUEUE_NOLIST`); `&attr obj=value` from keyboard stores raw.
- `QUEUE_DEBUG`/`QUEUE_NODEBUG` mapping, DEBUG output format `#dbref! expr => result` with nest indent, DEBUGFORWARDLIST.
- Limit counters shared across the evaluation via reference types on immutable state; decrement-skipped-after-limit subtlety handled.
- Escaped delimiters protect terminators (`\,` etc. — ESCAPING mode ≈ Penn's fall-through-past-terminator-switch).

---

## 4. Divergence catalog

### 4.1 Dynamic function names (confirmed, by-design, partially known)

Penn's callee name is the **evaluated output** accumulated at the current level (`src/parse.c:2780–2786`), so all of these call `add`:

```
[setq(0,add)]%q0(1,2)      → 3      (SharpMUSH: "add(1,2)" literal-ish)
&FN me=add  … [get(me/FN)](1,2) → 3
```

SharpMUSH's `FUNCHAR` commits the callee lexically. Two `[Skip]`ped tests exist (`PennMUSHParserGapTests.cs:442,463`) describing this loosely ("checks the function registry at every position" — the actual mechanism is name-from-evaluated-buffer). **Unfixable without abandoning lexical function recognition; correctly documented as deliberate. Recommendation: also document in help files, since softcode idioms like `[%va(...)]` exist in the wild.**

### 4.2 Unknown function at top level (confirmed divergence)

Penn: unknown `name(` **outside** `[]` → literal text (`foo(bar)` stays `foo(bar)`, `src/parse.c:2807–2823`); only inside `[]` (`PE_FUNCTION_MANDATORY`) does it become `#-1 FUNCTION (FOO) NOT FOUND` (+ `DID YOU MEAN` suggestion, `:2788–2806`).
SharpMUSH: `CallFunction` returns `#-1 FUNCTION ({name}) NOT FOUND` **unconditionally** (`SharpMUSHParserVisitor.cs:431`).
Witness: `think hello(world)` → Penn `hello(world)`; Sharp `#-1 FUNCTION (HELLO) NOT FOUND`. Everyday-visible; softcode writes prose with parens.
Fix: in `VisitFunction`, when lookup misses **and** the context is not inside a `bracketPattern`, fall back to literal text (the parse tree makes the "inside brackets" test trivial). Also consider Penn's suggestion string for the bracket case.

### 4.3 Lock AND/OR precedence (confirmed bug, security-relevant)

Penn: `E→T|E`, `T→F&T` — AND binds tighter (`src/boolexp.c:1378–1430`). `a&b|c` ≡ `(a&b)|c`.
SharpMUSH: `lockExprList: lockAndExpr | lockOrExpr | lockExpr;` with `lockAndExpr: lockExpr AND lockExprList` — equal precedence, right-fold: `a&b|c` ≡ `a&(b|c)` (`SharpMUSHBoolExpParser.g4:16–20`), and the eval visitor faithfully compiles that shape (`Expression.AndAlso(lockExpr, lockExprList)`).
**Witness:** lock `#FALSE & #FALSE | #TRUE` — passes in Penn, **fails** in SharpMUSH. Inverse (over-permissive) witnesses exist too: any lock of shape `a & b | c` where a is false and c true. This silently changes who passes locks.
Fix: restructure to `lockExprList: lockOrExpr; lockOrExpr: lockAndExpr (OR lockAndExpr)*; lockAndExpr: lockExpr (AND lockExpr)*;` and fold left in the three visitors (eval, normalization, validation). Normalization output stays textually compatible.

### 4.4 Even/odd argument validation never fires (confirmed bug)

`SharpMUSHParserVisitor.cs:540–548` switches on the **whole** flags value:

```csharp
switch (attribute.Flags) {
    case FunctionFlags.UnEvenArgsOnly when args.Length % 2 == 0: …
    case FunctionFlags.EvenArgsOnly  when args.Length % 2 != 0: …
}
```

Every real declaration combines flags (`letq`: `NoParse | UnEvenArgsOnly`; `setr`: `Regular | EvenArgsOnly`; `case`/`switch` families), so no case ever matches; wrong-arity calls proceed silently where Penn errors. Fix: `if (attribute.Flags.HasFlag(FunctionFlags.UnEvenArgsOnly) && …)`.

### 4.5 Command word is not evaluated (confirmed divergence)

Penn evaluates the command word (subs + brackets, no function check) before lookup (`src/command.c:1287–1292`): `[strcat(th,ink)] hi` and `%q0 hi` (q0=`think`) both run `think`. SharpMUSH's `EvaluateCommands` takes `firstCommandMatch.GetText().TrimStart()` — raw source text. Decide deliberately: match Penn (evaluate word before dispatch) or document as incompatibility. Note Penn's `]` noeval prefix and one-char aliases interact here.

### 4.6 $-command matching input: raw vs evaluated (confirmed divergence + internal inconsistency)

Penn matches `$`-patterns against the **fully evaluated** command line (`src/game.c`, `cptr`). SharpMUSH's `CommandDiscoveryService.MatchUserDefinedCommand` receives the **raw** slice (`commandText`; `MModule.plainText(trimmedCommandString)` at `CommandDiscoveryService.cs:43`). Meanwhile the hook OVERRIDE/EXTEND path deliberately evaluates first (`SharpMUSHParserVisitor.cs:1509–1515` — the comment explains why!). So `$foo *:…` triggered by `foo %n`: Penn matches with the name substituted; SharpMUSH matches the literal `%n`. Unify on the evaluated line (the hook comment is the spec).

### 4.7 The `#-1 PARSER FAILURE` class (by-design divergence; catalog the members)

Penn evaluates *everything*; SharpMUSH strict mode (function evaluation) errors on residual syntax failures. Confirmed members, with Penn's behavior from `src/parse.c`:

| Input | PennMUSH | SharpMUSH strict |
|---|---|---|
| `[add(1,2)` (unclosed `[`) | `3` (`:2730–2734`) | parser failure string |
| `add(1,2` (unclosed call) | `3` (`:2948–2952`) | parser failure string |
| `{a b` (unclosed `{`) | `a b` | parser failure string |
| `foo\` (trailing backslash) | `foo` (`:3115–3117`) | `ESCAPE` with no `ANY` → error |
| `foo%` (trailing percent) | `foo%` (`:2410–2413`) | `PERCENT` with no sub token → error (audit exact message) |
| `a]b`, `a}b` at top level | literal | **parity** — token-rewrite passes fix these two |

The rewrite passes (`RewriteOrphanedBracketClosers`/`BraceClosers`) already prove the total-language repair technique works at the token level. Extending it (synthesize missing closers at EOF; retype a trailing `ESCAPE`/`PERCENT` as `OTHER`) would close most of this class without touching the grammar. Alternatively accept + document; but note stored Penn softcode imported from real games *will* contain these shapes (the Myrddin BBS incident was exactly this class).

### 4.8 Arg evaluation vs arity/permission errors (confirmed, subtle)

Penn evaluates arguments (side effects fire!) **before** arity/permission errors are emitted (`src/parse.c:2954–2998`, comment "Can't do this check earlier, because of possible side effects" — though *denied* functions get eval-stripped args, `:2866–2870`). SharpMUSH validates Min/MaxArgs **before** evaluating (`CallFunction` :521–548). Witness: `add(setq(0,x),1,2,3)` — Penn sets `q0` then errors; Sharp errors without the side effect.

### 4.9 Negative maxargs / final-arg comma absorption (confirmed mechanism divergence)

Penn: `maxargs < 0` → once at the final slot, `PT_COMMA` is dropped and remaining commas become literal text of the last arg (`src/parse.c:2878–2894`, with the r1628 deprecation warning). SharpMUSH always splits and errors above `MaxArgs`. `lit()` happens to be safe (its `Literal` path re-captures the whole raw span), but every other Penn `maxargs=-N` builtin needs auditing against SharpMUSH's declarations (grep Penn's function table for negative maxargs; e.g. the `pemit`-family). Where Penn absorbs, Sharp must either join surplus args with `,` into the final one or declare `MaxArgs=int.MaxValue` and join in the body.

### 4.10 `iter()` `##` splice vs register (confirmed, security-flavored)

Penn textually splices the element into the raw action text and *then* evaluates (`replace_string2`, `src/funlist.c:2105–2107`) — element text containing softcode **executes** (classic injection footgun). SharpMUSH rewrites `##`→`%iL` and reads a register — element text stays data. Witness: `iter(%#,##)` — Penn substitutes the enactor dbref (the spliced `%#` evaluates); Sharp emits literal `%#`. Sharp is *safer*; document as deliberate incompatibility (this will bite imported softcode that relies on splice-eval, e.g. `iter(lnum(5),add(##,1))` works in both, but `iter(v(list),u(##))`-style tricks may not).

### 4.11 Limit values and limit-hit output shape

- Values: Penn shipped `mush.cnf`: recursion 50, invocations 25 000 (compiled default 2 500), call limit 100. SharpMUSH defaults: 100 / 100 000 / 1 000 (`ReadPennMUSHConfig.cs:178–184`). Same knob names, looser defaults — imported code may run in Sharp and die in Penn, not vice versa. Consider shipping Penn-parity defaults.
- Shape: Penn appends the error string (idempotently, `:2833–2835`) and **continues scanning/evaluating the remainder** (each subsequent call re-errors, deduped). SharpMUSH sets `LimitExceeded` and `VisitChildren` **abandons the rest of the tree** (`:170`). Witness: `[first-limited-thing]tail` — Penn's output still contains `tail`.

### 4.12 Missing safety guards (confirmed gaps)

1. **No CPU/wall-time slice.** Penn: 1 500 ms `ITIMER_PROF` per queue entry, checked at every `process_expression` entry; notifies "CPU usage exceeded." SharpMUSH: nothing — a single pathological evaluation (regex backtracking, huge `lnum`, deliberately slow function) runs unbounded. Async-friendly fix: stamp a deadline (`Stopwatch` timestamp + configured budget) into `ParserState` at top-level entry; check it where `LimitExceeded` is checked (`VisitChildren`, `CallFunction`); flag + unwind exactly like the invocation limit.
2. **No parse-phase depth guard.** Penn's `CALL_LIMIT` prevents C-stack overflow *during* the fused pass (`{`/`[`/`(` handlers refuse to recurse past it). SharpMUSH's counters live in the **visitor**, but the generated recursive-descent parser runs first with no guard: `[[[[[…` × ~10⁴–10⁵ in one input → `StackOverflowException` → **process death** (uncatchable in .NET). Telnet line caps may bound direct input, but attributes evaluated via `u()` are server-side inputs too. Fixes (cheap → thorough): (a) O(n) pre-scan capping delimiter nesting depth before parsing; (b) partial class on the generated parser overriding `EnterRule`/`EnterRecursionRule` calling `RuntimeHelpers.EnsureSufficientExecutionStack()` (throws catchable `InsufficientExecutionStackException`); (c) run parses on a dedicated large-`maxStackSize` thread.
3. **No `Halted` check** in the eval path (Penn: `eflags = PE_NOTHING`). Verify whether the queue layer refuses halted objects; if not, add.
4. **No output-size bound.** Penn's 8 K truncation doubles as a memory backstop. Sharp is deliberately unbounded (an improvement), but a configurable max (e.g. 1 MB per evaluation, error like the invocation limit) restores the DoS backstop without Penn's tightness.

### 4.13 Smaller confirmed/audit items

- **`lit()` drops markup** (confirmed by code path): the `Literal` branch reconstructs from `context.GetText()` (plain text) instead of re-slicing `source` — Penn's in-band markup survives `lit()`. Fix: use `GetContextText`-style slicing.
- **`% ` (percent-space)**: Penn outputs both chars `% ` (`:2422–2423`, "more natural typing"). Sharp: `OTHER_SUB` catch-all → verify output is `% ` not ` ` (audit + test).
- **Uppercase substitution rule**: Penn capitalizes the first non-markup char of the substituted value for *any* uppercase sub letter — including `%Q<x>`, `%I0`, `%V…` (`:2670–2675`). Sharp models some (%N vs %n via separate tokens); audit the full rule (`%Q0` when q0=`foo` → Penn `Foo`).
- **`%c`/`%u`** (last command raw/evaluated): tokens exist (`LASTCOMMAND_BEFORE_EVAL`/`AFTER_EVAL`); audit the values against Penn's `cmd_raw`/`cmd_evaled` timing (raw = pre-queue-split entry).
- **Unknown-fn suggestion**: Penn's bracket-case error appends `DID YOU MEAN 'X'`; Sharp doesn't. Cosmetic.
- **`MAX_ARG` (63) / `MAX_STACK_ARGS` (30)** caps don't exist in Sharp (unbounded). Rarely observable; note only.
- **UTF-16 code units**: `StringSpanInputStream` indexes `string` — `%<astral char>` puts a lone surrogate through `OTHER_SUB`. Penn is byte-oriented (also "wrong" but differently). Audit with an emoji test; the official `CodePointCharStream` exists but changes index arithmetic against `MString` (which is UTF-16 too — so *consistency* argues for staying UTF-16 and just testing surrogate behavior).

---

## 5. ANTLR4 standards alignment audit

Graded against the official docs (github.com/antlr/antlr4/doc/*, antlr.org API docs, C# runtime sources) and community sources (antlr-discussion, GitHub issues). Full citation list in §8.

### 5.1 Aligned with official/community guidance ✅

| Practice | Evidence |
|---|---|
| Split lexer/parser grammars + `tokenVocab` (required for modes) | all four `.g4`s |
| Lexer modes for islands, bounded (pop after one token) — avoids the open unbalanced-`popMode` crash bug (antlr4 #2006) since pops are unconditional | `SUBSTITUTION`/`ESCAPING` modes |
| Fat catch-all `OTHER` token ("make tokens as generic as possible" — Jim Idle; avoids the CheckStyle per-char adaptivePredict trap) | `SharpMUSHLexer.g4:28` |
| Visitor (not listener) for an evaluator needing return values + conditional descent | official listeners.md tradeoff |
| Async visitor = Harwell's exact recipe: `ValueTask<TResult>` TResult, `DefaultResult` override, awaiting `VisitChildren` override (the community footgun is *not* overriding it) | `SharpMUSHParserVisitor.cs:61,69,146` |
| `RemoveErrorListeners()` + custom collecting listener on the main parser; `DiagnosticErrorListener` gated behind debug config (official dev-only advice) | `MUSHCodeParser.cs:195–202` |
| Source text recovered by re-slicing the original (markup-aware `MModule.substring`), not `ctx.GetText()` — stronger form of the official "use TokenStream.getText(ctx)" rule | `GetContextText`, visitor:263–270 |
| Fresh recognizer instances per parse — officially cheap; DFA/ATN are static per generated class, warm-up amortizes (Harwell: "the overhead … will not pose a problem") | `ParseInternalCore` |
| Custom token factory with lazy text (parallels official `copyText=false` default) | `OptimizedTokenFactory` |
| Token-stream rewriting instead of predicated `CBRACK` alternatives (community-sanctioned stopgap; avoided a real AdaptivePredict hang) | `RewriteOrphaned*` + SLL summary doc |
| No lexer predicates (Lischke: one left-edge lexer predicate disables *all* lexer DFA caching) | lexer grammar |
| Oracle testing against real PennMUSH outputs (177 golden cases) + explicit gap tests | `PennMUSHOracleTests`, `PennMUSHParserGapTests` |
| EOF-anchored entry rules (6 of 7) | parser grammar |

### 5.2 Misaligned / gaps ⚠️

1. **Prediction mode: production runs LL; the official fast path is two-stage SLL+Bail.** The project's own `ANTLR4_SLL_AND_TOKEN_REWRITING_SUMMARY.md` measured **SLL = identical results, 171× faster** on the Myrddin corpus and declared SLL the default — but `DebugOptions.ParserPredictionMode = LL` is what ships, justified by "SLL … ignores semantic predicates during prediction," which misstates the official semantics (SLL ignores *parser context*; the documented guarantee is SLL either matches LL or reports a syntax error — `PredictionMode` API doc). The canonical resolution (ParserATNSimulator doc, credited to Sam Harwell): **SLL + `BailErrorStrategy`, catch C#'s `ParseCanceledException`, rewind, retry LL** — zero correctness risk, keeps the 171×. Lischke's precondition ("faster if input is mostly correct") is maximally true for a total language. Add a fallback counter metric; if it stays ~0 in production, the grammar is SLL-clean in practice. *This is the single largest cheap win in the codebase.*
2. **Predicate-heavy stateful grammar.** Mutable `@parser::members` + embedded actions + predicates contradicts "keep application code out of grammars" and creates the prediction-vs-action-state hazard the team already hit (the documented hang; actions don't execute during prediction in *either* mode). The token-rewriting pass was the right direction — more of the context-sensitivity could move to the token layer over time (e.g. a rewrite pass could retype `COMMAWS`/`EQUALS`/`SEMICOLON`/`CPAREN` to `OTHER` contextually, deleting predicates outright). Not urgent; SLL+Bail must land first and will mask most of the cost.
3. **Lexer keeps its default `ConsoleErrorListener`** (official: remove on both lexer and parser) — stray lexer errors print to server stdout. Same for **all three `BooleanExpressionParser` paths** (`Compile`/`Validate`/`Normalize` never call `RemoveErrorListeners`) — and locks *can* fail to parse, so this leaks real user-triggered noise to console instead of the player.
4. **`CommandCommaArgsParse` invokes EOF-less `commaCommandArgs`** directly, while `ValidateAndGetErrors` uses the `startPlainCommaCommandArgs` (EOF) wrapper — trailing garbage can silently drop in the production path (official: anchor consuming entry rules with EOF).
5. **No labeled alternatives (`# Name`)** — visitor works at rule granularity with manual child inspection. Works, but labeled alts would give typed contexts per alternative (e.g. split `beginGenericText`'s six alternatives). Low priority; the grammar is small.
6. **Profiling exists only as ad-hoc tests.** Official/community loop: `parser.Profile = true` → `ParseInfo` per-decision table (sort by time and max-k), or kaby76's `trperf`, over a representative command corpus. Note the C# profiling NPE history (#2693, fixed) — verify once on 4.13.1. Candidate hot spot found by inspection: the `explicitEvaluationString` star-loop decision with its predicated alternatives.
7. **Vestigial code:** `pendingEscapedOpeners` counters in both rewrite passes are written, never read; `TraceListener.cs` is dead; `<Compile Remove="gen\**">` targets a nonexistent dir; `SharpMUSHBooleanExpressionValidationVisitor .cs` has a space in the filename.
8. **Toolchain note:** antlr-ng as generator (`<ToolType>antlr-ng</ToolType>`) with runtime 4.13.1 is bleeding-edge; pin Antlr4BuildTasks + runtime versions together (the README's own advice), and never add `<Error>true</Error>` (warnings then silently produce zero generated files). Build downloads a JRE to `~/.jre` — cache for hermetic CI.
9. **Known runtime weakness on exactly this grammar class:** antlr4 #3962 ("text with bracket placeholders": Standard C# ≈ 3× slower warm than the frozen Harwell fork, unresolved). Switching runtimes is not viable (fork dead pre-4.7); the leverage is grammar/prediction-mode work, per Vergnaud's own comment on the issue.

### 5.3 Notable architecture endorsement

The community record vindicates the two *load-bearing* SharpMUSH choices: single-mode char-ish lexing with a fat text token (vs full mode-based islands, which crash on unbalanced closers — open bug — and can't share rules across modes), and per-parse instances over pooling. Terence Parr's own StringTemplate hand-writes its lexer for exactly this "everything is text" reason — SharpMUSH is the only known ANTLR MUSH implementation, and its compromise (tiny bounded modes + token rewriting + parser predicates) is a defensible novel design. The costs (predicates, the hang workaround, #3962 exposure) are the known price of that design, not accidents.

---

## 6. Performance analysis

**Per-evaluation pipeline cost** (every `FunctionParse`/`CommandParse`): input stream + lexer + `Fill()` + 2 rewrite passes + parser (LL prediction) + visitor + `ParserState` push. Penn's equivalent is a function call and a memset-free scan. The constant factor is unavoidable; the *multiplier* is not:

1. **`iter()` with `##`**: rewrites pattern per element and calls `FunctionParse` — full pipeline × N elements (`ListFunctions.cs:529–535`). The no-`##` path already reuses the deferred subtree closure. Unify: substitute via iteration registers only (`%iL` already exists) and revisit the subtree.
2. **Command dispatch**: raw split parse (NoParse mode) → per-arg `FunctionParse` (second full pipeline per arg) → NoParse/EqSplit deferred lambdas that call `FunctionParse` *again* on demand (`ArgumentSplit`, visitor:1970–2016; TODO comment at :1981 acknowledges it). The function-argument path proves the better pattern exists: `CreateDeferredEvaluation` closes over the **subtree** — no re-lex, no re-parse. Command args deserve the same: keep the arg's `EvaluationStringContext` and revisit.
3. **Attribute evaluation (`u()`, `$`-commands, hooks, `@function`)**: every call re-parses the stored attribute text. Attribute text is immutable between writes and the visitor is stateless w.r.t. the tree (all state flows through `ParserState`) — so **parse trees are cacheable**. Precedent in-repo: `BooleanExpressionParser.Compile` FusionCache. A `FusionCache` keyed on attribute text hash → `startPlainString` tree (plus the `MString` source for slicing) would eliminate the parse phase for hot softcode entirely. This is the biggest structural win available; Penn *cannot* do this (its parse *is* its eval), so it's also a place to beat Penn.
4. **Two-stage SLL** (§5.2.1): multiplies into all of the above; their own 171× measurement bounds the upside on pathological inputs, and ~10× is plausible on typical ones (LL full-context scans measured 477/102 lines).
5. **Don't bother with**: recognizer pooling (officially unnecessary), `BuildParseTree=false` (visitor needs trees), runtime switching (§5.2.9). Consider `TrimParseTree` only after measuring.

**Measurement discipline:** `SharpMUSH.Benchmarks` exists (`CommandParseBenchmarks`, `SubstitutionBenchmarks`, …) — add an SLL-vs-LL axis and a warm-vs-cold axis; wire `Profile=true` decision dumps into `ParserPerformanceDiagnosticTests` over a corpus of real game commands (the Myrddin BBS file already in-tree is ideal).

---

## 7. Prioritized roadmap

**P0 — correctness, small diffs**
1. Lock precedence fix (§4.3) + oracle tests for `a&b|c` shapes across all three boolexp visitors.
2. `FunctionFlags` even/odd validation via `HasFlag` (§4.4) + tests (`letq` with even args must error).
3. Unknown-function-at-top-level → literal text outside brackets (§4.2).
4. Boolexp + lexer error-listener hygiene (§5.2.3); `commaCommandArgs` EOF entry (§5.2.4); `lit()` markup slice (§4.13).

**P1 — the two big levers**
5. Two-stage SLL+Bail with LL-fallback counter (§5.2.1). Flip `DebugOptions` default; correct the enum doc-comment.
6. Deferred-subtree unification: command args + `iter ##` revisit contexts instead of re-parsing (§6.1–2).
7. Attribute parse-tree cache (FusionCache, invalidated on attribute write) (§6.3).

**P2 — robustness parity**
8. Evaluation time budget (CPU-slice analogue) checked beside `LimitExceeded` (§4.12.1).
9. Parse-depth guard: nesting pre-scan and/or `EnsureSufficientExecutionStack` partial class (§4.12.2).
10. Output-size ceiling (configurable; not 8 K) (§4.12.4); Halted check (§4.12.3); Penn-parity limit defaults decision (§4.11).

**P3 — compat decisions & polish**
11. Decide-and-document: command-word evaluation (§4.5), $-command matching on evaluated line (§4.6 — the hook path is already the spec), limit-hit tail behavior (§4.11), negative-maxargs audit (§4.9), `% `/uppercase-sub/`%c`-`%u` audits (§4.13), parser-failure class reduction via extended token rewriting (§4.7).
12. Differential harness: dockerized PennMUSH + generated corpora replayed through both engines, diffing outputs — industrializes what `PennMUSHOracleTests` does by hand.
13. Grammar polish: labeled alternatives, predicate reduction via contextual token retyping, profiling harness in CI, dead-code removal (§5.2.7).

---

## 8. Key sources

**PennMUSH** (master `80a1d5b9`): `src/parse.c` (process_expression :2048–3195; interesting-char sets :2191–2228; terminators :2242–2294; `%`-subs :2355–2677; braces :2678–2708; brackets :2709–2735; function calls :2736–3090; spaces :3092–3104; backslash :3105–3118; exit :3127–3195), `hdrs/parse.h` (PE_*/PT_* :297–384), `src/command.c` (command word :1287–1292; argparse :960–1049; run_command :1546–1667), `src/cque.c` (:1112–1177 action-list split; CPU timer :1143), `src/game.c` (:1256–1364 $-command walk), `src/boolexp.c` (grammar :8–17; descent :1089–1440; bytecode :1919–1948), `src/funlist.c` (iter :2026–2131), `src/markup.c`, `hdrs/ansi.h` (:41–44 tags), `src/timer.c` (:316–345 CPU slice), `game/mushcnf.dst` (:360–380 limits).

**SharpMUSH**: `SharpMUSH.Parser.Generated/*.g4`, `SharpMUSH.Implementation/MUSHCodeParser.cs`, `Visitors/SharpMUSHParserVisitor.cs`, `Visitors/SharpMUSHBooleanExpression*.cs`, `StringSpanInputStream.cs`, `BufferedTokenSpanStream.cs`, `OptimizedTokenFactory.cs`, `ParserErrorListener.cs`, `LenientErrorStrategy.cs`, `BooleanExpressionParser.cs`, `SharpMUSH.Library/ParserInterfaces/{ParserState,CallState}.cs`, `SharpMUSH.Configuration/Options/DebugOptions.cs`, `CoPilot Files/{ANTLR4_SLL_AND_TOKEN_REWRITING_SUMMARY,PARSER_OPTIMIZATION_ANALYSIS,GRAMMAR_PENNMUSH_COMPARISON}.md`, `SharpMUSH.Tests/Parser/*`.

**ANTLR4 official** (all fetched): doc/{grammars,lexer-rules,parser-rules,predicates,left-recursion,listeners,options,wildcard,unicode,interpreters,csharp-target,getting-started,parsing-binary-files}.md; doc/faq/{general,lexical,parse-trees,error-handling}.md (note: doc/faq/performance.md does not exist); antlr.org API docs for ParserATNSimulator (two-stage pattern + DFA sharing + locking discipline), PredictionMode, BailErrorStrategy, DiagnosticErrorListener, CommonTokenFactory, Parser; C# runtime sources (CharStreams/AntlrInputStream — no `[Obsolete]` in C#; `ParseCanceledException`; static `decisionToDFA` in codegen template); issues #374 (two-stage origin), #4232 (DFA memory: "grow endlessly" on novel input; ~10× without cache), #3962 (Standard-vs-fork gap on bracket-tags-in-text grammar).

**Community** (antlr-discussion + issues): Harwell on instance reuse (B2TaUFm29jE), on performance checklist (ynvgmjjsZTw), on async visitors (#2442); Idle on generic tokens (aBObOXmlMt8) and CheckStyle char-level ambiguity (8uclWFreMSs); Parr on many-alternatives landmine (QmHbMHZhBPo); Lischke on thread safety (ppulI_Z-Tf8), on SLL-if-input-correct + lexer-predicate cache kill (PpgPQU5jA3Q), on stack overflow inevitability (SO 40571941); #2006 (popMode crash, open); #4344 (DFA lock contention + kaby76 profiling methodology); #2693 (C# profiling NPE history); tunnelvisionlabs/antlr4cs#143 (ClearDFA race); Antlr4BuildTasks README (version pinning, `<Error>true</Error>` footgun); StringTemplate4 source (hand-written STLexer).
