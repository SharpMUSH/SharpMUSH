<!-- help-article
{
  "corpus": "help",
  "id": "privatize-function",
  "lookup": "privatize()",
  "aliases": [
    "privatize"
  ],
  "sections": [
    {
      "id": "private-evaluation",
      "heading": "Private evaluation",
      "lookup": "privatize private evaluation"
    }
  ]
}
-->
# privatize()

`privatize(<on>)`<br>
`privatize(<on>, <code>[, <registers>[, <omit>[, <multi>]]])`

localize(), ulocal() and the other functions that keep the q-registers safe start their code with a copy of the caller's registers, so that code can still read them. privatize() makes them start with no registers at all instead, so code you call cannot see values it was not given.

With one argument, privatize() turns this on (a true `<on>`) or off (a false one) for the rest of the evaluation, and returns nothing. It is off when a command starts.

Examples:
```sharp
> &SHOW #123=(before: [r(0)])[setq(0,internal)] [r(0)]
> think [setq(0,external)][localize(u(#123/SHOW))] %q0
(before: external) internal external
> think [privatize(1)][setq(0,external)][localize(u(#123/SHOW))] %q0
(before: ) internal external
```

With more arguments, privatize() evaluates `<code>` privately: see [privatize private evaluation].

privatize() comes from RhostMUSH and takes the same arguments.

::: seealso
- [LOCALIZE()]
- [ULOCAL()]
- [u2local()]
- [setq()]
:::

## Private evaluation

`privatize(<on>, <code>[, <registers>[, <omit>[, <multi>]]])` evaluates `<code>` as localize() would and returns its result. When `<on>` is true the localized registers start out empty, and the switch is on for anything `<code>` localizes in turn; afterwards it goes back to what it was.

`<registers>` limits this to the registers it names; the others pass straight through, so whatever `<code>` sets in them stays set. Each letter or digit of `<registers>` names one register. When `<multi>` is 1, `<registers>` is a space-separated list of register names instead, which can name any register. A true `<omit>` turns the list around: the registers it names are the ones that pass through.

```sharp
> &PAIR #123=[setr(0,ONE)]:[r(1)]
> think [setr(0,TWO)]:[setr(1,CHEESE)], [privatize(1,u(#123/PAIR))], %q0:%q1
TWO:CHEESE, ONE:, TWO:CHEESE
> think [setr(0,TWO)]:[setr(1,CHEESE)], [privatize(1,u(#123/PAIR),0)], %q0:%q1
TWO:CHEESE, ONE:CHEESE, TWO:CHEESE
> think [setr(0,TWO)]:[setr(1,CHEESE)], [privatize(1,u(#123/PAIR),1)], %q0:%q1
TWO:CHEESE, ONE:, ONE:CHEESE
```

In the second example only register 0 is private, so the code still reads CHEESE from register 1. In the third only register 1 is, so the ONE the code put in register 0 is still there afterwards.
