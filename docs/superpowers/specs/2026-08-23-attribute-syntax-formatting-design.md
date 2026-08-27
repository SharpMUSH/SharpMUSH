# Attribute syntax flags: formatted `@examine` output

**Date:** 2026-08-23
**Branch:** `feat/attribute-syntax-formatting`
**Status:** design approved, pending implementation plan

## Summary

Add two attribute flags, `cmdsyntax` and `funsyntax`, that declare the softcode
dialect stored in an attribute. When an attribute carries one, commands that
display it render the value as formatted code: indented, broken across lines,
syntax-highlighted, with syntax errors marked inline and summarised beneath.

The formatting is **semantics-preserving**. Every line break lands where the
delimiter is genuinely acting as a structural separator, so the rendered form is
a valid program identical in behaviour to the stored one. A player can select it,
paste it back, and get the same code. The stored value is never modified.

**The precise scope of that guarantee** (see §3 for how it narrowed during
implementation): the rendered form evaluates identically to the stored one *when
it is evaluated*, under the dialect the attribute's flag declares. Text that a
caller invokes with `/noeval` — or through any `ParseMode.NoParse`/`NoEval` path —
is reproduced **verbatim**, including whitespace, because `VisitFunction` returns
the whole call's raw source in that mode. Under such a caller no reformatting of
any kind is safe, so this is not a hazard the formatter can detect and avoid: the
stored text cannot know its future callers, and the only safe behaviour would be
to never format at all. It is a boundary of the guarantee, not a defect. It stays
harmless as long as formatting remains display-only; it would need revisiting only
if some future change rewrote stored attribute text rather than rendering it.

## Motivation

Softcode is stored as a single line. A non-trivial `@switch` or a nested
`u()`/`iter()` chain is effectively unreadable in `@examine` output today, which
emits the raw value verbatim after a highlighted header. Players work around this
with external editors.

Two things block a naive fix. Whitespace in MUSHcode is mostly literal data, so a
pretty-printer that inserts newlines freely silently corrupts what it displays.
And the same text is ambiguous between dialects: an attribute that does not begin
with `$` may be a command list invoked by `@trigger` or a function body invoked by
`u()`, and nothing in the text distinguishes them. Both parse, differently, and
the wrong choice yields wrong highlighting and spurious errors.

The flags resolve the second problem, which makes the first tractable.

## Non-goals

- `@decompile` is deliberately excluded. Its output must stay byte-exact for
  backup and transfer.
- No parse-tree caching. Noted under Risks; a follow-up if profiling demands it.
- No reformatting of stored values, ever.
- No new permission machinery. The flag table has no permission field
  (`SharpMUSH.Library/Models/SharpAttributeFlag.cs`) and these flags are cosmetic.

## 1. The flags

| Name | Symbol | Meaning | `ParseType` |
|---|---|---|---|
| `cmdsyntax` | `x` | value is a command list | `ParseType.CommandList` |
| `funsyntax` | `f` | value is a function expression | `ParseType.Function` |

The flags map 1:1 onto `ParseType` (`SharpMUSH.Library/ParserInterfaces/IMUSHCodeParser.cs:79-88`),
which is the enum every existing tooling API already takes. That correspondence is
the reason for two flags rather than one flag plus a heuristic: the entry rule *is*
the ambiguity.

If both flags are set, `cmdsyntax` wins — a command list may contain function
calls, but not the reverse.

Symbols `x` and `f` were chosen from the unclaimed set. Every mnemonic letter for
"command" is already taken (`c` is `no_clone`, `C` is `case`, `$` is `no_command`),
so `x` is arbitrary by necessity.

### Naming rationale

`command` and `function` were considered and rejected. `command` would read as the
antonym of the existing `no_command` while being unrelated to it — `no_command`
suppresses `$`-command matching (behaviour), where these flags only affect display.
`SharpAttributeExtensions.IsCommand` (`:47`) also already means "has no
`no_command` flag *and* starts with `$`", so the natural extension name is taken.
`function` collides with user-defined `@function`s and the `UserFunction` semantic
token type.

Prefix matching was checked and is unaffected: `AttributeService.cs:640-643` orders
candidates by name length and takes the shortest, so `c` continues to resolve to
`case`. `x`, `f`, `cmdsyntax`, and `funsyntax` are all unclaimed prefixes.

### Plumbing

The flag list is duplicated across three providers and all three must change:

- **ArangoDB** — new `Migration_AddSyntaxFlags.cs`, `Id => 20260823_001`, following
  the UPSERT-keyed-on-name pattern of `Migration_AddApprovedFlag.cs` but targeting
  `DatabaseConstants.AttributeFlags`. Migrations are discovered by reflection and
  duplicate ids throw at startup; `20260823_001` was verified free across all
  worktrees, and must be re-checked at merge time.
- **SurrealDB** — two tuples in the seed array at
  `SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs:550-574`. This seed is
  always-run and idempotent (`:169-171`), so no migration id is needed.
- **Memgraph** — two tuples at
  `SharpMUSH.Database.Memgraph/MemgraphDatabase.Migration.cs:558-581`. Uses
  `MERGE ... ON CREATE SET`, which creates new flags correctly.

Plus `IsCmdSyntax` / `IsFunSyntax` and a `SyntaxParseType(this SharpAttribute) →
ParseType?` helper in `SharpMUSH.Library/Extensions/SharpAttributeExtensions.cs`.

## 2. Architecture

Three independent passes over the same source text, joined by character offsets:

| Pass | Produces | Status |
|---|---|---|
| **Classify** | token list with categories and ranges | exists — `IMUSHCodeParser.GetSemanticTokens` |
| **Lay out** | line breaks and indent levels | **new** |
| **Diagnose** | error spans and messages | exists — `IMUSHCodeParser.ValidateAndGetErrors` |

Keeping them independent means each degrades alone. Classification falls back to
flat lexer output past the nesting limit (`MUSHCodeParser.cs:800`); layout and
diagnostics are unaffected. Code with syntax errors still gets laid out.

Layout is driven by the token stream rather than the parse tree. No public API
returns a `ParserRuleContext`, obtaining one costs a full LL parse, and on
erroneous input ANTLR yields a damaged error-recovery tree — precisely the case
this feature most needs to render. A delimiter-depth walk over tokens works on
malformed input by construction.

### Component placement

A new `SoftcodeFormatter` in `SharpMUSH.Library/Services/`. It takes the token list
and error list as **inputs** rather than an `IMUSHCodeParser`, so it unit-tests
with no parse infrastructure. A thin helper at each call site performs the two
parser calls and hands the results over.

`SemanticTokenAnsiPalette` moves from `SharpMUSH.Documentation/MarkdownToAsciiRenderer/`
into `SharpMUSH.Library/`. It maps Library types (`SemanticTokenType`,
`SemanticTokenModifier`) to ANSI and belongs there; `SharpMUSH.Library` cannot
reference `SharpMUSH.Documentation` (the dependency runs the other way).
`ANSILibrary` is a folder inside `SharpMUSH.MarkupString`, which `SharpMUSH.Library`
already references, so `Ansi` remains available after the move.

`SharpMUSH.Implementation` already references `SharpMUSH.Library`,
`SharpMUSH.Documentation`, and `SharpMUSH.MarkupString`, so `GeneralCommands` needs
no new project reference.

## 3. Where breaks are legal

The lexer's whitespace fragment is `fragment WS: [ \r\n\f\t]*`
(`SharpMUSH.Parser.Generated/SharpMUSHLexer.g4:10`), and it is attached to seven
lexer rules, covering six distinct delimiter characters (`OPAREN` and `FUNCHAR`
both end in `(`):

```
OBRACK:    '[' WS
OBRACE:    '{' WS
COMMAWS:   ',' WS
EQUALS:    '=' WS
SEMICOLON: ';' WS
OPAREN:    '(' WS
FUNCHAR:   [0-9a-zA-Z_~@`]+ '(' WS
```

Everywhere else whitespace falls into `OTHER` (`:28`) and is literal data.

> **Correction (2026-08-23, during implementation).** The claim below that a
> newline after any of these tokens is "free" was **wrong as originally stated**,
> and the Task 2 review caught it. Whitespace absorption is a *lexer-level*
> property: the token's text includes the whitespace either way. Whether that
> whitespace survives depends on whether the token acts as a **structural
> delimiter** in its parse context. `VisitBeginGenericText`
> (`SharpMUSHParserVisitor.cs:2503`) emits `GetContextText` — the raw token text,
> absorbed whitespace included — so wherever one of these tokens is ordinary text
> rather than a delimiter, a break there is literal data and changes the program.
>
> Concretely: a `,` separates only inside a function argument list (`@emit a, b`
> is text); a `;` separates only at command-list top level (`switch(a,b;c)` is
> text); and `(` **never** groups at all — the grammar opens `function` on
> `FUNCHAR`, so a bare paren reaches the parser as generic text.
>
> The implemented rule is therefore narrower than what follows: break after
> `FUNCHAR`, but only when the function name **resolves**; after `COMMAWS` only
> when the enclosing group was opened by a resolving `FUNCHAR`; after `SEMICOLON`
> only at root **and only under a command-list parse type**. `OBRACK` survived
> the §7 corpus and stays. `OPAREN`, `EQUALS`, and `OBRACE` are never break
> positions.
>
> **The generalisation, which is the real lesson.** The equivalence corpus found
> three defects, and all three are the same shape: *a token is structural only in
> a context the token stream alone cannot see.* A comma separates only inside a
> function argument list. A function call is a call only when its name resolves —
> otherwise `LiteralFunctionCall` copies the whole thing through as text, absorbed
> whitespace included. A semicolon separates commands only in a command-list
> dialect. None of these is visible to a lexical scan.
>
> So a purely lexical layout engine cannot be made safe. It needs the parser's
> context supplied explicitly, which is why `Compute` takes a function-resolution
> oracle and a `ParseType` rather than deriving everything from tokens. Both
> default to the conservative answer when absent. **Any future break position must
> be justified against this test — "the lexer absorbs whitespace here" is
> necessary and not sufficient** — and must be proven by the corpus in §7, not by
> reading the grammar. Reading the grammar is what produced the wrong answer
> three times.
>
> Note the pleasing circularity: the two flags this feature adds exist to declare
> which dialect an attribute holds, and the layout engine turned out to need
> exactly that same fact. `SyntaxParseType()` supplies it.

Two consequences define the algorithm:

1. **A newline immediately after any of those tokens is lexically free** — but
   see the correction above: lexical freedom is necessary, not sufficient. The
   token must also be acting as a structural delimiter.
2. **There is no whitespace absorption before a closing delimiter.** A newline
   placed before `)`, `]`, or `}` becomes an `OTHER` token and joins the final
   argument as literal text. **Closers must therefore cuddle the last item and
   never appear on their own line.**

Point 2 is the non-obvious one, and it is why the output looks slightly unusual.

Of the six safe break positions, v1 uses four: `(`, `[`, `,`, `;`. Breaking after
`=` and `{` is safe but rarely reads well; both are reserved for a later revision.

### Brace groups are atomic in v1

Nothing inside `{ ... }` is ever broken. Brace contents are literal in some
contexts and re-parsed as code in others, and the grammar's
`{inBraceDepth == 0}?` predicate (`SharpMUSHParser.g4:55`) means `;` does not split
inside braces. That is too subtle to guess at.

The cost is real and should be stated plainly: a `@switch` whose branches are
wrapped in `{}` — the single most common shape worth indenting — renders its
branches on one line each in v1. Relaxing this is a follow-up, gated on the
equivalence corpus (§6) demonstrating which brace contexts are safe.

## 4. The layout algorithm

A standard "group fits" algorithm.

**Build.** Walk the tiling token list maintaining a stack. Opening delimiters
(`FUNCHAR`, `OPAREN`, `OBRACK`, `OBRACE`) push a group; matching closers pop it.
Unbalanced input closes open groups implicitly at end of input — this path must not
throw, since malformed code is a first-class input. Each group carries its flat
width, computed bottom-up.

**Fit.** Render top-down tracking the current column. A group renders flat when
`column + flatWidth <= width`. Otherwise it breaks: emit the opening token, then
each direct child item on its own line at `depth + 1`, with the closing delimiter
cuddled against the final item.

**Indent.** Two spaces per level, clamped to `width / 2` so deep nesting cannot
consume the line.

**Width.** From the connection's `WIDTH` metadata, read the same way
`ConnectionFunctions.cs:1033-1044` reads it for the `width()` function. Falls back
to 78 when the executor has no connection — a queued or `@trigger`ed `@examine` has
no terminal to measure.

Because every break is *locally* legal, grouping mistakes degrade aesthetics but
never semantics. Even a mis-built group tree emits an equivalent program.

### Worked example

`funsyntax`, width 40:

```
switch(
  words(%0),
  0,
  {You said nothing at all.},
  1,
  {[ucstr(%0)]},
  {Too many words: [words(%0)] found.})
```

Every newline follows `(` or `,`. The trailing `)` cuddles the last argument. The
brace groups are untouched.

## 5. Rendering

For each token: look up `SemanticTokenAnsiPalette.GetStyle`, override with the
error style where the token overlaps an error span, and emit
`MModule.MarkupSingle(style, text)`. Assemble with `MModule.multiple`, which routes
to `ConcatMany` — a single `StringBuilder` pass. Pairwise `MModule.concat` is O(n)
per call and must not be used in a loop.

Output is reconstructed from **source offsets**, not by concatenating `token.Text`.
`RecursiveMarkdownRenderer.CodeBlock.cs:260-267` does the latter, which assumes the
token list tiles the input perfectly; if it ever does not, characters vanish
silently. Offset-based reconstruction cannot lose text.

Truecolor may be emitted freely. `OutputTransformService.ApplyAnsiTransformations`
(`SharpMUSH.ConnectionServer/Services/OutputTransformService.cs:63-86`) downgrades
xterm256 to 16 colours or strips ANSI entirely per connection capability.

### Errors

Inline: the offending span rendered in an error style — inverse video plus red
foreground, so it remains visible after the 16-colour downgrade and is not confused
with any palette colour. Beneath the block: one
summary line per error giving position, offending token, and expected tokens —
all already carried by `ParseError` (`SharpMUSH.Implementation/ParserErrorListener.cs:83-97`).
The summary is plain text, so it survives on monochrome clients where the inline
marking conveys nothing.

Error offsets are line/column based. `BuildSnippet` (`:253-262`) already derives an
absolute offset from line/column; that computation is extracted and reused rather
than duplicated. Offsets refer to the **original** text and are mapped through the
layout's offset table to their rendered positions.

## 6. Call sites

**`@examine`** (`SharpMUSH.Implementation/Commands/GeneralCommands.cs:1106-1109`).
Currently one `Notify` per attribute, with the raw value appended to a highlighted
`ATTR [flags#owner]: ` header. When a syntax flag is present, the header stays on
its own line and the code block begins at column 0 beneath it, so the width
calculation starts from a known column. The block is a single multi-line `MString`
sent as one `Notify`; `NotifyService` delegates line-ending normalisation to the
ConnectionServer (`NotifyService.cs:50-55`), so embedded `\n` is correct.

**`@grep/PRINT`** (`:6092-6135`). Same renderer. Its existing match-highlighting
(`:6106-6122`) composes as one more overlay on the same offset model.

**Set-time validation.** When a value is stored into an attribute carrying a syntax
flag, run `ValidateAndGetErrors` and notify the setter with the same summary lines.
It **never refuses the set** — PennMUSH does not validate at set time, and parity
governs. This is new plumbing; no set-time hook exists today, and errors currently
surface only at evaluation (`MUSHCodeParser.cs:376`).

## 7. Testing

The load-bearing test is an **equivalence corpus**: for a body of real softcode,
assert that evaluating the original and evaluating the formatted output produce
identical results. This is what actually demonstrates semantics preservation rather
than asserting it, and it is the gate for relaxing the atomic-brace rule later.
Corpus drawn from existing parser tests and help-file examples.

**It must compare evaluated output, not normalised token streams.** An earlier
draft compared lexer output with trailing whitespace trimmed from the seven
WS-bearing token types — which would have compared away exactly the defect
described in the §3 correction, and reported success. A comparison that normalises
the thing under test proves nothing. Run both forms through the real parser and
compare results.

Also:

- Table-driven layout tests: input, width, expected rendering. Cover flat-fits,
  break-all, nested groups, the cuddled closer, indent clamping.
- Malformed input: unbalanced `(`, `[`, `{`; text that fails to parse. Assert
  output is produced and no exception escapes.
- Error marking: known-bad inputs to expected summary lines.
- Flag round-trips through `@set` and `@set` with `!` for unset, plus prefix
  resolution (`x`, `f`, `cmd`, `fun`).
- Provider parity: all three seeds produce the flags. Integration suites run under
  Podman via Testcontainers.

Framework is TUnit; `dotnet run --project SharpMUSH.Tests -- --treenode-filter ...`.

## 8. Cleanups folded in

All in files this work already modifies:

- `SharpAttributeExtensions.cs:66` — `IsNoDebug` tests for `"nodebug"`, but the
  seeded flag name is `no_debug`. The check can never be true.
- `GeneralCommands.cs:1088` — `showPublicOnly` is computed and never read, inside
  the exact Examine block being changed.
- `AttributeService.cs:667-701` — `UnsetAttributeFlagAsync` lacks the prefix-match
  fallback that `SetAttributeFlagAsync` has, so `@set x/y=!wiz` fails where
  `@set x/y=wiz` succeeds.
- `RecursiveMarkdownRenderer.CodeBlock.cs` — switch to the shared highlight pass
  after the palette moves, removing the duplicated per-token loop.

**Flagged, not fixed:** `SharpAttributeExtensions.cs` declares `IsNoprog`,
`IsPrivate`, `IsListen`, `IsNoDump`, `IsMortalHear`, and `IsActionHear` against
flag names present in no provider seed. Either the flags are missing or the
extensions are dead. Determining which requires a PennMUSH parity audit of the
attribute flag table, which is its own piece of work.

## 9. Risks

**Cost.** Every display of a flagged attribute is a full LL parse.
`GetPredictionMode` (`MUSHCodeParser.cs:225`) forces LL for all tooling paths, and
no parse tree cache exists anywhere in the codebase. Only flagged attributes pay
it, and `@examine` is interactive, so this is judged acceptable. `@examine` on an
object with many flagged attributes is the case to watch. Mitigation if needed: the
existing `ExceedsNestingLimit` guard already bounds the worst case, and a
`FusionCache` keyed on attribute value hash is a contained follow-up.

**Pre-existing author markup.** Attribute values may already contain ANSI markup
that collides with syntax colouring. Intended resolution: author markup wins where
present, syntax colouring applies only to unstyled spans. Requires confirming how
`AnsiCode` tokens surface in the semantic token stream before being settled —
resolve during implementation, not now.

**Token tiling.** The design assumes the semantic token list covers the input
contiguously. Offset-based reconstruction (§5) makes a gap harmless rather than
lossy, but a gap would still mean unclassified text. The malformed-input tests
cover this.

## 10. Change list

| File | Change |
|---|---|
| `SharpMUSH.Database.ArangoDB/Migrations/Migration_AddSyntaxFlags.cs` | new, id `20260823_001` |
| `SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs` | +2 seed tuples |
| `SharpMUSH.Database.Memgraph/MemgraphDatabase.Migration.cs` | +2 seed tuples |
| `SharpMUSH.Library/Extensions/SharpAttributeExtensions.cs` | +2 extensions, +`SyntaxParseType`, fix `IsNoDebug` |
| `SharpMUSH.Library/Services/SoftcodeFormatter.cs` | new — layout + render |
| `SharpMUSH.Library/SemanticTokenAnsiPalette.cs` | moved from `SharpMUSH.Documentation` |
| `SharpMUSH.Documentation/MarkdownToAsciiRenderer/RecursiveMarkdownRenderer.CodeBlock.cs` | consume shared pass |
| `SharpMUSH.Implementation/Commands/GeneralCommands.cs` | Examine + `@grep/PRINT` wiring, drop dead local |
| `SharpMUSH.Library/Services/AttributeService.cs` | set-time validation, unset prefix fallback |
| `SharpMUSH.Tests/...` | layout, equivalence corpus, errors, flag plumbing |
| help files | document both flags |
