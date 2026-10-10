# Softcode grammar

`SharpMUSH.Parser.Generated/SharpMUSHParser.g4` is generated. Edit
`SharpMUSH.Parser.Generated/generate-parser.py` and run it:

```bash
python3 SharpMUSH.Parser.Generated/generate-parser.py          # rewrites the grammar and its C# bridge
python3 SharpMUSH.Parser.Generated/generate-parser.py --check  # exits nonzero if either file is stale
```

`PredictionLookaheadTests.GeneratedGrammarIsUpToDate` runs the check, so a hand edit to either
generated file fails the test run. The lexer, `SharpMUSHLexer.g4`, is written by hand.

## Why a generator

In softcode, `,` `)` `;` `=` and `>` are text in one place and structure in another. A comma
separates arguments inside a call and is text outside one; `;` separates commands only in a
command list and only outside braces; `>` ends a register name only inside `%q<...>`.

The grammar used to decide this with semantic predicates over counters the parser kept as it went
(`{ inFunction == 0 }?` and so on). ANTLR evaluates a predicate only after its lookahead reaches a
conflict, so a bracketed argument full of calls, such as `if(c,A,[setq(...)][json(...)])`, made
prediction read ahead and fork at every comma and parenthesis. The cost was exponential in the
number of calls; 12 took 13 seconds.

The generated grammar has no predicates and no parser state. Each rule that can see one of those
tokens exists once per context in which the answer differs, and the rule names say which context
that is. A comma is a separator in `function__Call_*` rules and text in `Top_*` ones because those
rules are written that way, so prediction settles each token where it stands.

## Contexts

A context is the set of facts that decide what the five tokens mean:

| Fact | Values |
|---|---|
| mode | the entry point: plain, command list (`;` separates), command args (`,` separates), or before the `=` of an `=` split |
| F | inside a function call, since the last brace (a brace starts over) |
| B | inside a brace, at any depth |
| K | inside a register, argument or capture name (`%q<...>`, `%<...>`, `$<...>`) |
| P | with paren_groups: outside a group, inside one (its `)` closes it), or in brackets or a name inside one |
| G | paren_groups is on |

That is 128 combinations. Many treat the five tokens alike and have the same successors, so the
generator merges them into 52 classes, giving 379 rules. A copy is named for its class: `Top_` or
`Call_`, then the letters of the tokens that are text there (R `)`, S `;`, C `,`, E `=`, A `>`), then
`_G` with paren_groups. `beginGenericText__Top_RSCEA` takes all five as text;
`function__Call_SEA` is a call whose `)` and `,` are structure.

Entry points come in pairs: `startPlainString` and `startPlainString__G`. The parser's
`ParenGroups` property picks one; `SoftcodeParsePipeline.Enter` calls `StartPlainString()` and the
others, which dispatch on it.

## Paren groups

With paren_groups on, a bare `(` opens a group in which `,` `=` and `;` are text, as PennMUSH's
`process_expression` does. Groups are rules (`closedGroup`, and `openTail` for a group that never
closes, which is allowed only outside a call). The bridge splices each group into its parent as it
exits (`SharpMUSHParser.ExitRule`), so the tree has the shape the evaluator expects: the group's
text is part of the run of text around it.

## The C# bridge

`SharpMUSHParser.Contexts.cs` is generated with the grammar. It gives each family of copies one
interface: every `function__*` context is an `IFunctionContext`, with the accessors the evaluator
uses (`evaluationString()`, `FUNCHAR()`, `CPAREN()`). `SharpMUSHParserRuleVisitor<T>` sends every
copy to one method (`VisitFunction(IFunctionContext)`), so the visitor, the syntax analyzer and the
layout code are written against the base rule names and never see a context suffix.

antlr-ng ignores the `baseContext` rule option, which is why the bridge uses interfaces rather than
a shared base class.

## Results

Measured on 29,589 strings from the test suite, parsed as command lists (median of warm rounds):

| Grammar | First pass | Warm |
|---|---|---|
| predicates | ~1,950 ms | 83 ms |
| context rules | ~465 ms | 65 ms |

The slow expression above, malformed and with 20 calls, took 8.6 s with predicates on the LL pass
that reports the error; it takes under a millisecond now.

## How the trees differ

Without paren_groups, every string in the test corpus gives the same tree as the predicate grammar
did. With paren_groups:

- A group left open inside a call takes the call's `)`, as `process_expression` does.
- An open group no longer runs past the `=`, `;` or `,` that ends the argument or command it is in.
- A `)` inside brackets inside a group is text.

## Error messages

A parse failure reads `#-1 PARSER FAILURE: Expected X at ...`. `ParserErrorListener` names what
closes the innermost construct still open (`)` or `,` in a call, `]`, `}`, or `>` in a name). When
the construct is empty and needs something first, it says so (`an expression inside []`,
`a name inside <>`), and a trailing `%` or `\` asks for what must follow it. It does not list the
tokens that could start text.

The predicate grammar often reported an unclosed bracket as `Expected end of input` at the `[`,
because prediction had already ruled the bracket out. The context grammar reports
`Expected ] at end of expression`.
