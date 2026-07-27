# Follow-up scope: content-addressed parse-tree cache (P1.3)

**Status:** not started. Deferred as its own benchmark-gated, focused PR — it is a hot-path change and a different concern from the parity/hardening PRs.

**Goal:** stop re-parsing identical softcode text on every evaluation. `FunctionParse` (lex → token-rewrite → parse → visit) runs in full on each call; the Mediator query cache only caches the attribute's DB *fetch*, not the parse. Caching the parse *tree*, keyed on the exact text, makes repeated evaluation of the same text parse once and visit many times.

**Primary win:** the `u()`-in-a-loop pattern — `iter(<list>, u(me/FN, ##))` re-parses `FN`'s body once per element today. Also helps the command-argument re-parse on the dispatch path. (The `iter` `##` no-`##` path already reuses its subtree via the deferred closure; this is about the *re-parse* paths.)

## Gate on a benchmark first

Two-stage SLL (merged in the hardening PR) already cut parse cost substantially, so the marginal value is smaller than when this was first proposed. **Before implementing, measure** with `SharpMUSH.Benchmarks` (there are `SubstitutionBenchmarks`, `StringFunctionBenchmarks`, `ListFunctionBenchmarks`, `CommandParseBenchmarks`): confirm the *parse* is still a meaningful fraction of per-evaluation cost (vs. the *visit*, which is the actual work and is not cacheable). Add a `u()`-in-a-tight-loop benchmark specifically, since that is the pattern the cache targets. If parse is already a small fraction post-SLL, this may not be worth the hot-path complexity — decide with the number, not a hunch.

## Why it is correct with no invalidation

Content-addressing (key = the exact plaintext) means **no cache invalidation is ever required**:

- **Parsing is pure.** Lex+parse of a given string always yields the same tree; nothing about parsing depends on `ParserState`. Only *visiting* uses state, and each evaluation runs its own visitor.
- **Trees are read-only after parse.** `SharpMUSHParserVisitor` only reads contexts (children, `Start`/`Stop`, `GetText`); it never mutates the tree. So multiple evaluations — including concurrent ones — can visit one shared cached tree safely, each with its own visitor and `ParserState`. (Confirm no visitor override writes to a context before relying on this.)
- **Token indices align.** The cached tree's token `StartIndex`/`StopIndex` are offsets into the plaintext. The key *is* the plaintext, so they line up with any `MString` whose plaintext equals the key.
- **Markup is per-call, not cached.** The visitor slices `source` (the current call's `MString`) by those indices, so two `MString`s with the same plaintext but different markup correctly share a tree yet render their own markup. This means the cache can be keyed on plaintext alone.

So an attribute edited between calls simply has different text → a different key → a different (or absent) entry. No staleness, no invalidation hook needed. This is what makes P1.3 *safer* than a typical cache, and it is the crux — do not add an invalidation path; if you find yourself needing one, the key is wrong.

## What to cache and where

- **Hook:** `MUSHCodeParser.ParseInternalCore` (and the duplicated body in `CommandListParseVisitor`) — between the lex/parse and the visit. On a cache hit, skip the lexer + `BufferedTokenSpanStream.Fill()` + the two token-rewrite passes + the parser, and go straight to constructing a fresh visitor over the cached context with the current `source`.
- **Cache value:** the root `ParserRuleContext` for the entry rule. Note the tree holds references to its tokens, and the `OptimizedToken`s lazily slice text from the input string they were built with, so the cache entry transitively retains that input string — fine, but it is where the memory goes.
- **Key:** entry-rule + full plaintext (or a collision-resistant digest of it — never a truncated `GetHashCode`) **+ a grammar/semantic-version component**. Different start rules produce different trees for the same text, so the rule must be in the key; and a long-lived cache (or one persisted across a redeploy) must never hand back a tree produced by an incompatible grammar or visitor, so bump the version component whenever the grammar or the tree's meaning changes.
- **Infrastructure:** reuse FusionCache with a dedicated keyed cache, mirroring `BooleanExpressionParser`'s `compiled-expressions` (see `Startup.cs` and `BooleanExpressionParser.cs`) — size-capped (~1024 entries or a MemoryCache `SizeLimit`) so memory is bounded. That is the policy default unless an operator tunes it.
- **Only cache clean parses.** If the parse produced syntax errors (strict `#-1 PARSER FAILURE`, or the lenient recovery tree), do not cache — errors interact with the strict/lenient path and are rare enough not to matter. Cache only the success path.

## Risks / correctness surface

- **Memory.** Each entry retains a tree + tokens + the input string. Bound it with the size cap; watch it does not grow with novel input (MUSH softcode is shape-repetitive, so it should plateau).
- **Thread-safety of shared trees.** Relies on ANTLR contexts being immutable post-parse and the visitor being read-only. Verify no visitor path mutates a context; a single counter-example breaks concurrent reuse.
- **Interaction with two-stage SLL.** The cached tree is whatever the two-stage path produced (SLL, or LL after fallback). That is fine — it is the authoritative tree for that text — but the cache must sit *after* the two-stage resolution, not inside stage 1.
- **`source` binding.** The visitor must be constructed with the *current* call's `MString`, never the one from when the tree was cached, or markup from the wrong call leaks in. This is the single easiest mistake to make.

## Test plan

- A benchmark first (above) to justify and then to show the win.
- Correctness: an attribute evaluated in a loop returns identical results with and without the cache; editing the attribute between calls takes effect immediately (different text → different key — prove there is no staleness); the same plaintext with different markup renders each call's own markup (proves `source` is per-call, not cached).
- A concurrency test: many parallel evaluations of the same text produce consistent results (guards the shared-tree read-only assumption).
- Full suite green with the cache on; also run once with it disabled via config to confirm parity.
