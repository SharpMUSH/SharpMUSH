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
| B | inside a brace, at any depth: a `;`, `,` or `=` there ends no command or argument outside the brace |
| K | inside a register, argument or capture name (`%q<...>`, `%<...>`, `$<...>`) |
| P | with paren_groups: inside a group (its `)` closes it) |
| G | paren_groups is on |

A bracket and a name start over: what they hold is read as a plain string, whatever is around them,
so only the bracket's `]` or the name's `>` ends them, as PennMUSH's `process_expression` reads a
`[` with `PT_BRACKET` alone and a name with `PT_GT` alone. `cat(a,[x,y])` is `a x,y`, `think [x;y]`
does not split the command, and the comma in `cat(a,%q<x,y>)` is part of the name.

That leaves 60 reachable combinations. The generator writes every rule for each of them, then
keeps one copy of each set of copies that have the same body and refer to copies that are alike in
turn. A rule has as many copies as there are different ways to read it: `beginGenericText` depends
only on which tokens are text, while `evaluationString` also depends on what its calls, braces and
names hold. That gives 144 rules.

A copy is named for what is true of every context it is read in: `Top` or `Call` (outside or
inside a call; neither when the copy is read the same either way), `Group` inside a paren group,
then the letters of the tokens that are text there (R `)`, S `;`, C `,`, E `=`, A `>`), then `_G`
with paren_groups. Two copies that would get the same name are numbered. `beginGenericText__Top_RSCEA`
takes all five as text; `function__Call_SEA` is a call whose `)` and `,` are structure.

Entry points come in pairs: `startPlainString` and `startPlainString__G`. The parser's
`ParenGroups` property picks one; `SoftcodeParsePipeline.Enter` calls `StartPlainString()` and the
others, which dispatch on it.

## A call's tail

`evaluationString` is either a leading call followed by `callTail`, or an `explicitEvaluationString`.
`explicitEvaluationString` cannot begin with a `name(` token (`FUNCHAR`); that is how a string that
starts with one is known to start with a call, with no choice for prediction to make. `callTail` is
what follows the call, and takes what may come later in any string, where a `FUNCHAR` is text:
`add(1,2)mul(2,3)` is `3mul(2,3)`, as in PennMUSH, which looks for a function only at the first `(`.
In an argument the tail's `,` and `)` keep their meaning there, as they do after any other text.

## Paren groups

With paren_groups on, a bare `(` opens a group in which `,` `=` and `;` are text, as PennMUSH's
`process_expression` does. Groups are rules (`closedGroup`, and `openTail` for a group that never
closes, which is allowed only outside a call). The bridge splices each group into its parent as it
exits (`SharpMUSHParser.ExitRule`), so the tree has the shape the evaluator expects: the group's
text is part of the run of text around it.

## The C# bridge

`SharpMUSHParser.Contexts.cs` is generated with the grammar. It gives each family of copies one
interface: every `function__*` context is an `IFunctionContext`, with the accessors the evaluator
uses (`evaluationString()`, `FUNCHAR()`, `CPAREN()`). The generator reads the accessors off the
grammar as ANTLR does: an element that can occur more than once in a rule gets an array accessor
and an indexed one. What does not depend on the grammar (`ISoftcodeContext`, the paren-group splice,
`ParenGroups`) is written by hand in `SharpMUSHParser.Tree.cs`. `SharpMUSHParserRuleVisitor<T>` sends every
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

## Error messages

A parse failure reads `#-1 PARSER FAILURE: Expected X at ...`. `ParserErrorListener` names what
closes the innermost construct still open (`)` or `,` in a call, `]`, `}`, or `>` in a name). When
the construct is empty and needs something first, it says so (`an expression inside []`,
`a name inside <>`), and a trailing `%` or `\` asks for what must follow it. It does not list the
tokens that could start text.

The predicate grammar often reported an unclosed bracket as `Expected end of input` at the `[`,
because prediction had already ruled the bracket out. The context grammar reports
`Expected ] at end of expression`.
