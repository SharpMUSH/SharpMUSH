<!-- help-article
{
  "corpus": "help",
  "id": "inf-function",
  "lookup": "inf()",
  "aliases": [
    "inf"
  ],
  "sections": []
}
-->
# inf()

`inf(<list>, <pattern>[, <delimiter>[, <output separator>]])`

inf() is iter() with one addition: a `<list>` written as `:<start>:>:` counts up from the whole number `<start>`, and one written as `:<start>:<:` counts down from it, without end. Since the count never runs out, `<pattern>` calls ibreak() to stop it. "##" is the current number, and "#@" and inum(0) count the passes from 1.

Any other `<list>` is an ordinary list, and inf() then does what iter() does. An empty `<output separator>` puts nothing between the results; leaving it out uses `<delimiter>`.

A count that never calls ibreak() still stops: after as many passes as the game's function invocation limit, before its results pass the output limit, or when the time limit runs out.

Examples:
```sharp
> think inf(:5:>:, [if(gt(##, 8), ibreak())]##)
5 6 7 8 9
> think inf(:5:<:, [if(lt(##, 2), ibreak())]##)
5 4 3 2 1
> think inf(:3:>:, [if(eq(#@, 3), ibreak())]##, , -)
3-4-5
```

inf() comes from RhostMUSH and takes the same arguments.

::: seealso
- [iter()]
- [IBREAK()]
- [nsiter()]
- [citer()]
- [while()]
:::
