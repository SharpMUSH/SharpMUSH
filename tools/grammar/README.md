# A predicate-free softcode grammar

`SharpMUSHContextParser.g4` is the softcode grammar of `SharpMUSH.Parser.Generated/SharpMUSHParser.g4`
with its semantic predicates turned into rules. It is a proposal: nothing in the product builds it.
`generate-context-parser.py` writes it; `compare/` parses the same inputs with both grammars and
reports where they differ.

```bash
python3 tools/grammar/generate-context-parser.py > tools/grammar/SharpMUSHContextParser.g4
dotnet run --project tools/grammar/compare -c Release -- --fuzz 40000 inputs.txt   # one input per line
dotnet run --project tools/grammar/compare -c Release -- --bench inputs.txt
```

## Why

Whether `,` `)` `;` `=` `>` are text or structure depends on where they are. A comma ends a function
argument inside a call and is text outside one. `SharpMUSHParser.g4` says so with predicates over
counters such as `inFunction`. ANTLR does not check a predicate until its lookahead reaches a
conflict, so in `if(c,A,[...])` it keeps both readings of the comma after `A` and reads through the
whole bracket, forking at every comma and parenthesis inside it. #1632 checks the predicates before
prediction starts, but only on the SLL pass. The LL pass that reports errors is still stock ANTLR.
Leave one `)` off a 24-call version of that expression and the current parser takes 143 seconds to
say so. This grammar takes 93 ms.

## How

Each counter the predicates read has only a few values that matter, and they combine into a fixed
set of contexts:

| fact | values |
|---|---|
| entry point | plain, command list (`;` separates), comma args (`,` separates), before an `=` split |
| inside a call | since the last brace, as `inFunction` |
| inside a brace | at any depth |
| inside a register name | `%q<…>`, `$<…>`, where `>` ends it |
| paren group (`paren_groups`) | none, inside one (its `)` closes it), in brackets or a name inside one |
| `paren_groups` | off, on |

That gives 128 reachable contexts. After merging the ones that read every token alike and lead to
alike contexts, 52 remain. Every rule that can see one of those tokens is written once per context
and named for it: `Top_` or `Call_`, then the letters of the tokens that are text there (`R` `)`,
`S` `;`, `C` `,`, `E` `=`, `A` `>`), and `_G` when `paren_groups` is on. The result has 379 rules
in 1,650 lines.

Plain text outside a call, and the call it enters:

```antlr
beginGenericText__Top_RSCEA: CPAREN | SEMICOLON | COMMAWS | EQUALS | CCARET | OPAREN | (escapedText | OTHER | DOLLAR | ansi);
evaluationString__Top_RSCEA:
      function__Call_SEA explicitEvaluationString__Top_RSCEA?
    | explicitEvaluationString__Top_RSCEA
;
function__Call_SEA: FUNCHAR (evaluationString__Call_SEA? (COMMAWS evaluationString__Call_SEA?)*)? CPAREN;
beginGenericText__Call_SEA: SEMICOLON | EQUALS | CCARET | OPAREN | (escapedText | OTHER | DOLLAR | ansi);
```

A comma in `function__Call_SEA` can only be a separator, so prediction settles it at the comma. Each
rule copy also has a precise follow set, which makes SLL prediction close to LL.

With `paren_groups` on, a group is a rule (`closedGroup`, or `openTail` for one that is never
closed) instead of a counter. The comparison tool flattens those nodes, so trees line up with the
current grammar's.

## Results

Inputs: 29,589 string literals from the test suite, the Myrddin BBS script, the jobs package's
expressions and 40,000 fuzz strings. Each was parsed with all 7 entry points, with `paren_groups` on
and off, for 974,246 parses.

| | |
|---|---|
| same tree | 493,732 |
| rejected by both | 480,037 |
| different tree | 271 |
| parsed only by the current grammar | 182 |
| parsed only by this grammar | 24 |

With `paren_groups` off, the trees are the same on every real input. The 24 parses only this grammar
accepts come from two fuzz strings with an unclosed `$<`, which the current grammar rejects: its
lookahead ignores the predicates past the first token, picks a reading, then fails that reading's
predicate.

Every other difference has `paren_groups` on, and unbalanced parentheses or an unclosed `$<`. Two
real inputs are among them, the first two kinds below:

- **A group left open inside a call.** In `s(ansi\(rG\,ansi(D\,[…]\)\))` the unescaped `ansi(` opens a
  group. The current grammar lets the final `)` close the call when giving it to the group would
  fail to parse. Here the group takes it, as PennMUSH's `process_expression` does: a `(` that starts
  no call reads to its own `)` (`src/parse.c`, `case '('`). The call is then unclosed.
- **A group still open at a separator.** The current parser's group counter carries past an `=` or
  `;` into the next argument, where it makes commas text. Here a group ends where its argument
  ends.
- **A `)` in brackets inside a group.** The current counter lets it close the outer group. Here it
  is text, as in PennMUSH, where brackets start a new `process_expression`.

On speed, the slow expression from #1629 with 200 calls parses in 8 ms in plain LL, after a
117 ms first parse that builds the DFA. Over the 29,589 inputs, both grammars take about the same time once warm (57–100 ms
against 88–137 ms, a noisy measure). The first pass takes 180 ms against 1,412 ms. With
`paren_groups` on, long runs of groups cost more than today: 100 to 800 repeats of `(a,` or
`(a (b) ` take 6–34 ms warm against 1–10 ms, and about 200–330 ms the first time, because choosing
between a closed and an open group looks ahead to the group's end.

## What adopting it would take

The parse-tree visitor (`SharpMUSHParserVisitor`) and `SoftcodeSyntaxAnalyzer` are written against
one context class per rule (`ExplicitEvaluationStringContext`, `FunctionContext`, ...). Here each rule
is up to 52 classes. The generator would also write a C# bridge, again from the same table: an
interface per base rule, with the child accessors the visitor uses, implemented by every copy, and a
base visitor that sends each `VisitFunction__Call_SEA` to one `VisitFunction`. The visitor would
then take interfaces instead of concrete contexts. Callers would choose the `__G` entry point
instead of setting `parenGroups`, and `PredicateResolvingSimulator` would go.
